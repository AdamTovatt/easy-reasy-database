using EasyReasy.Database.Logging;
using EasyReasy.Database.Logging.Models;
using EasyReasy.Database.Logging.Npgsql;
using EasyReasy.Database.Logging.Npgsql.Partitions;
using EasyReasy.Database.Logging.Npgsql.Sinks;
using EasyReasy.Database.Logging.Reading;
using EasyReasy.Database.Pagination;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Serilog.Events;
using Serilog.Parsing;

namespace EasyReasy.Database.Logging.Npgsql.Tests
{
    /// <summary>
    /// Exercises the PostgreSQL COPY sink and partition maintenance against a real database.
    /// Connects to <c>EASYREASY_LOGGING_TEST_CONNECTION_STRING</c> if set, otherwise a local
    /// default. (Deliberately not <c>DATABASE_CONNECTION_STRING</c>, which the surrounding
    /// environment may set to an unrelated, non-PostgreSQL value.)
    /// </summary>
    public class NpgsqlOperationalLoggingTests : IAsyncLifetime
    {
        private const string Table = "oplog_test";

        private sealed class FixedTimeProvider : TimeProvider
        {
            private readonly DateTimeOffset _now;

            public FixedTimeProvider(DateTimeOffset now) => _now = now;

            public override DateTimeOffset GetUtcNow() => _now;
        }

        private static readonly string ConnectionString =
            Environment.GetEnvironmentVariable("EASYREASY_LOGGING_TEST_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=easy-reasy-db-mapping;Username=postgres;Password=postgres";

        private readonly OperationalLoggingOptions _options = new OperationalLoggingOptions { TableName = Table };
        private NpgsqlDataSource _dataSource = null!;

        public async Task InitializeAsync()
        {
            Serilog.Debugging.SelfLog.Enable(Console.Error);

            _dataSource = NpgsqlDataSource.Create(ConnectionString);

            await ExecuteAsync($"DROP TABLE IF EXISTS {Table} CASCADE");
            await ExecuteAsync(OperationalLogSchema.CreateTableSql(Table));
        }

        public async Task DisposeAsync()
        {
            await ExecuteAsync($"DROP TABLE IF EXISTS {Table} CASCADE");
            await _dataSource.DisposeAsync();
        }

        private static LogEvent CreateLogEvent(string template, LogEventLevel level, params LogEventProperty[] properties)
        {
            MessageTemplate parsed = new MessageTemplateParser().Parse(template);
            return new LogEvent(DateTimeOffset.UtcNow, level, exception: null, parsed, properties);
        }

        private async Task ExecuteAsync(string sql)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
            await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        private async Task<List<string>> ListChildPartitionsAsync()
        {
            const string sql = @"
                SELECT child.relname
                FROM pg_inherits
                JOIN pg_class parent ON parent.oid = pg_inherits.inhparent
                JOIN pg_class child  ON child.oid  = pg_inherits.inhrelid
                WHERE parent.relname = @table_name";

            List<string> names = new List<string>();
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
            await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("table_name", Table);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(0));
            }

            return names;
        }

        [Fact]
        public async Task EmitBatchAsync_WritesRowsReadableThroughTheCoreReadRepository()
        {
            NpgsqlOperationalLogSink sink = new NpgsqlOperationalLogSink(_dataSource, _options);

            LogEvent info = CreateLogEvent(
                "User {UserId} did {Action}",
                LogEventLevel.Information,
                new LogEventProperty("UserId", new ScalarValue("u1")),
                new LogEventProperty("Action", new ScalarValue("login")),
                new LogEventProperty("CorrelationId", new ScalarValue("trace-xyz")),
                new LogEventProperty("SourceContext", new ScalarValue("MyApp.Accounts")));
            LogEvent error = CreateLogEvent("boom", LogEventLevel.Error);

            await sink.EmitBatchAsync(new[] { info, error });

            OperationalLogReadRepository repository = new OperationalLogReadRepository(_dataSource, _options);
            PagedResult<OperationalLogEntry> page = await repository.GetAsync(new OperationalLogFilters(), page: 1, perPage: 50);

            Assert.Equal(2, page.TotalCount);

            OperationalLogEntry infoRow = page.Items.Single(row => row.Level == "Information");
            Assert.Equal("u1", infoRow.UserId);
            Assert.Equal("trace-xyz", infoRow.CorrelationId);
            Assert.Equal("MyApp.Accounts", infoRow.SourceContext);
            Assert.NotNull(infoRow.Properties);
            // The non-column property landed in the jsonb blob with a camelCased key.
            Assert.Contains("\"action\"", infoRow.Properties);
            Assert.Contains("login", infoRow.Properties);
        }

        [Fact]
        public async Task RunCycleAsync_DropsPartitionsOlderThanRetention_AndKeepsThoseInsideTheWindow()
        {
            // A fixed clock makes the retention boundary deterministic: now = 2025-06-15, retention
            // 90 days → cutoff month = 2025-03. Anything before March 2025 is dropped; March onward
            // is kept, and the current + future months are (re)created by EnsureFutureMonths.
            FixedTimeProvider clock = new FixedTimeProvider(new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero));

            await ExecuteAsync($"CREATE TABLE IF NOT EXISTS {Table}_2020_01 PARTITION OF {Table} FOR VALUES FROM ('2020-01-01') TO ('2020-02-01')");
            await ExecuteAsync($"CREATE TABLE IF NOT EXISTS {Table}_2025_02 PARTITION OF {Table} FOR VALUES FROM ('2025-02-01') TO ('2025-03-01')");
            await ExecuteAsync($"CREATE TABLE IF NOT EXISTS {Table}_2025_04 PARTITION OF {Table} FOR VALUES FROM ('2025-04-01') TO ('2025-05-01')");

            OperationalLogPartitionMaintenanceService service = new OperationalLogPartitionMaintenanceService(
                _dataSource,
                _options,
                new NpgsqlOperationalLoggingOptions { Retention = TimeSpan.FromDays(90) },
                clock,
                NullLogger<OperationalLogPartitionMaintenanceService>.Instance);

            await service.RunCycleAsync(CancellationToken.None);

            List<string> children = await ListChildPartitionsAsync();

            // Before the cutoff month → dropped.
            Assert.DoesNotContain($"{Table}_2020_01", children);
            Assert.DoesNotContain($"{Table}_2025_02", children);
            // On/after the cutoff month → kept.
            Assert.Contains($"{Table}_2025_04", children);
            // The clock's current month is ensured to exist.
            Assert.Contains($"{Table}_2025_06", children);
        }
    }
}
