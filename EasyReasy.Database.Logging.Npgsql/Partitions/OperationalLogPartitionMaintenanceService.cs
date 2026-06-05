using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using EasyReasy.Database.Logging;
using Npgsql;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EasyReasy.Database.Logging.Npgsql.Partitions
{
    /// <summary>
    /// Background service that maintains monthly range partitions for the operational log table:
    /// runs shortly after startup and then daily, ensuring the current + 2 future months exist and
    /// dropping whole partitions older than the configured retention. Partition names follow the
    /// <c>{table}_{YYYY_MM}</c> convention; any child not matching it (notably <c>{table}_default</c>)
    /// is left alone by the drop pass.
    /// </summary>
    public sealed class OperationalLogPartitionMaintenanceService : BackgroundService
    {
        private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

        private static readonly Regex MonthlyPartitionRegex = new Regex(
            @"^(?<parent>[a-z0-9_]+)_(?<year>\d{4})_(?<month>\d{2})$",
            RegexOptions.Compiled);

        private readonly NpgsqlDataSource _dataSource;
        private readonly string _tableName;
        private readonly TimeSpan _retention;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<OperationalLogPartitionMaintenanceService> _logger;

        /// <summary>Creates the maintenance service.</summary>
        public OperationalLogPartitionMaintenanceService(
            DbDataSource dataSource,
            OperationalLoggingOptions options,
            NpgsqlOperationalLoggingOptions npgsqlOptions,
            TimeProvider timeProvider,
            ILogger<OperationalLogPartitionMaintenanceService> logger)
        {
            if (dataSource is not NpgsqlDataSource npgsqlDataSource)
            {
                throw new InvalidOperationException(
                    "OperationalLogPartitionMaintenanceService requires the registered DbDataSource to be an NpgsqlDataSource.");
            }

            _dataSource = npgsqlDataSource;
            _tableName = options.TableName;
            _retention = npgsqlOptions.Retention;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        /// <inheritdoc/>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(InitialDelay, _timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            using PeriodicTimer timer = new PeriodicTimer(Interval, _timeProvider);

            while (true)
            {
                try
                {
                    await RunCycleAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Operational log partition maintenance cycle failed; will retry next tick");
                }

                try
                {
                    if (!await timer.WaitForNextTickAsync(stoppingToken))
                    {
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Runs one maintenance cycle: ensure current + 2 future monthly partitions exist, then drop
        /// partitions entirely older than the retention window. Exposed for testing.
        /// </summary>
        internal async Task RunCycleAsync(CancellationToken cancellationToken)
        {
            DateTime now = _timeProvider.GetUtcNow().UtcDateTime;

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(cancellationToken);

            // Bound how long we wait for a lock — DROP needs an exclusive lock, and failing fast to
            // retry next tick beats pinning a connection behind an unrelated long query.
            await using (NpgsqlCommand setLockTimeout = new NpgsqlCommand("SET lock_timeout = '5s'", connection))
            {
                await setLockTimeout.ExecuteNonQueryAsync(cancellationToken);
            }

            await EnsureFutureMonthsAsync(connection, now, cancellationToken);
            await DropExpiredPartitionsAsync(connection, now, cancellationToken);
        }

        private async Task EnsureFutureMonthsAsync(NpgsqlConnection connection, DateTime now, CancellationToken cancellationToken)
        {
            DateTime baseMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            for (int monthOffset = 0; monthOffset <= 2; monthOffset++)
            {
                DateTime monthStart = baseMonth.AddMonths(monthOffset);
                DateTime monthEnd = monthStart.AddMonths(1);
                string partitionName = $"{_tableName}_{monthStart:yyyy}_{monthStart:MM}";

                string sql = string.Format(
                    CultureInfo.InvariantCulture,
                    "CREATE TABLE IF NOT EXISTS {0} PARTITION OF {1} FOR VALUES FROM ('{2:yyyy-MM-dd}') TO ('{3:yyyy-MM-dd}')",
                    partitionName,
                    _tableName,
                    monthStart,
                    monthEnd);

                await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        private async Task DropExpiredPartitionsAsync(NpgsqlConnection connection, DateTime now, CancellationToken cancellationToken)
        {
            DateTime cutoffMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).Add(-_retention);
            cutoffMonth = new DateTime(cutoffMonth.Year, cutoffMonth.Month, 1, 0, 0, 0, DateTimeKind.Utc);

            List<string> partitionNames = await ListChildPartitionsAsync(connection, cancellationToken);

            foreach (string partitionName in partitionNames)
            {
                Match match = MonthlyPartitionRegex.Match(partitionName);
                if (!match.Success || !string.Equals(match.Groups["parent"].Value, _tableName, StringComparison.Ordinal))
                {
                    continue;
                }

                int year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
                int month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
                DateTime partitionMonth = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);

                if (partitionMonth >= cutoffMonth)
                {
                    continue;
                }

                // Isolate per-partition server failures (e.g. lock_timeout 55P03) so one locked
                // partition doesn't block the rest of the cycle. Connection-level failures propagate.
                try
                {
                    await using NpgsqlCommand command = new NpgsqlCommand($"DROP TABLE IF EXISTS {partitionName}", connection);
                    await command.ExecuteNonQueryAsync(cancellationToken);

                    _logger.LogInformation("Dropped expired operational log partition {PartitionName} (older than {RetentionDays} days)",
                        partitionName, _retention.TotalDays);
                }
                catch (PostgresException exception)
                {
                    _logger.LogWarning(exception, "Failed to drop expired partition {PartitionName}; will retry next cycle", partitionName);
                }
            }
        }

        private async Task<List<string>> ListChildPartitionsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            const string sql = @"
                SELECT child.relname
                FROM pg_inherits
                JOIN pg_class parent ON parent.oid = pg_inherits.inhparent
                JOIN pg_class child  ON child.oid  = pg_inherits.inhrelid
                WHERE parent.relname = @table_name";

            List<string> names = new List<string>();
            await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("table_name", _tableName);

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                names.Add(reader.GetString(0));
            }

            return names;
        }
    }
}
