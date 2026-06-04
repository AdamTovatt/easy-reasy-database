using System.Data.Common;
using System.Text;
using EasyReasy.Database.Logging.Broadcasting;
using EasyReasy.Database.Logging.Models;
using EasyReasy.Database.Logging.Serialization;
using EasyReasy.Database.Mapping;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace EasyReasy.Database.Logging.Sinks
{
    /// <summary>
    /// Provider-agnostic batched Serilog sink that writes operational log events to a SQL table
    /// using a parameterized multi-row <c>INSERT</c> via <see cref="EasyReasy.Database.Mapping"/>.
    /// Works against any provider the mapping layer supports. Buffered by Serilog's native batching
    /// (configured where the sink is wired). Best-effort: on write failure the batch is reported to
    /// <see cref="SelfLog"/> and dropped — acceptable for operational telemetry, since the console
    /// sink wired in parallel keeps everything visible during a database outage.
    /// <para>
    /// The <c>properties</c> value is bound as a plain text parameter (JSON-in-text). On PostgreSQL
    /// with a <c>jsonb</c> column, prefer the binary-COPY sink in
    /// <c>EasyReasy.Database.Logging.Npgsql</c>, which binds <c>jsonb</c> natively and is faster.
    /// </para>
    /// </summary>
    public sealed class OperationalLogSink : IBatchedLogEventSink
    {
        // Columns in fixed positional order. The parameter names and VALUES tuples are built from
        // this list so the SQL and the parameter dictionary can never drift.
        private static readonly string[] Columns =
        {
            "created_at", "level", "source_context", "message", "message_template",
            "exception", "correlation_id", "user_id", "properties",
        };

        // Cap rows per INSERT so the parameter count (rows × columns) stays well under the most
        // restrictive provider limit (SQLite defaults to 999 bound parameters). 50 × 9 = 450.
        private const int MaxRowsPerInsert = 50;

        private readonly DbDataSource _dataSource;
        private readonly OperationalLoggingOptions _options;
        private readonly IOperationalLogBroadcaster? _broadcaster;
        private readonly string _columnList;

        /// <summary>
        /// Creates the sink. <paramref name="broadcaster"/> is optional — when supplied, every
        /// successfully-persisted row is published to it after the batch commits.
        /// </summary>
        public OperationalLogSink(
            DbDataSource dataSource,
            OperationalLoggingOptions options,
            IOperationalLogBroadcaster? broadcaster = null)
        {
            _dataSource = dataSource;
            _options = options;
            _broadcaster = broadcaster;
            _columnList = string.Join(", ", Columns);
        }

        /// <inheritdoc/>
        public async Task EmitBatchAsync(IReadOnlyCollection<LogEvent> batch)
        {
            List<OperationalLogEvent> events = batch.Select(OperationalLogEventFactory.Materialize).ToList();
            if (events.Count == 0)
            {
                return;
            }

            try
            {
                await InsertBatchAsync(events);
                BroadcastBatch(events);
            }
            catch (Exception exception)
            {
                SelfLog.WriteLine("EasyReasy operational log sink failed to write batch of {0}: {1}", events.Count, exception);
            }
        }

        /// <inheritdoc/>
        public Task OnEmptyBatchAsync() => Task.CompletedTask;

        private async Task InsertBatchAsync(IReadOnlyList<OperationalLogEvent> events)
        {
            await using DbConnection connection = await _dataSource.OpenConnectionAsync();

            for (int start = 0; start < events.Count; start += MaxRowsPerInsert)
            {
                int count = Math.Min(MaxRowsPerInsert, events.Count - start);
                (string sql, Dictionary<string, object?> parameters) = BuildInsert(events, start, count);
                await connection.ExecuteAsync(sql, parameters);
            }
        }

        private (string Sql, Dictionary<string, object?> Parameters) BuildInsert(
            IReadOnlyList<OperationalLogEvent> events,
            int start,
            int count)
        {
            StringBuilder sql = new StringBuilder();
            sql.Append("INSERT INTO ").Append(_options.TableName).Append(" (").Append(_columnList).Append(") VALUES ");

            Dictionary<string, object?> parameters = new Dictionary<string, object?>();

            for (int i = 0; i < count; i++)
            {
                OperationalLogEvent logEvent = events[start + i];

                if (i > 0)
                {
                    sql.Append(", ");
                }

                sql.Append('(');
                for (int columnIndex = 0; columnIndex < Columns.Length; columnIndex++)
                {
                    if (columnIndex > 0)
                    {
                        sql.Append(", ");
                    }

                    sql.Append('@').Append('r').Append(i).Append('_').Append(Columns[columnIndex]);
                }
                sql.Append(')');

                string prefix = $"r{i}_";
                parameters[prefix + "created_at"] = logEvent.CreatedAt;
                parameters[prefix + "level"] = logEvent.Level;
                parameters[prefix + "source_context"] = logEvent.SourceContext;
                parameters[prefix + "message"] = logEvent.Message;
                parameters[prefix + "message_template"] = logEvent.MessageTemplate;
                parameters[prefix + "exception"] = logEvent.Exception;
                parameters[prefix + "correlation_id"] = logEvent.CorrelationId;
                parameters[prefix + "user_id"] = logEvent.UserId;
                parameters[prefix + "properties"] = logEvent.Properties;
            }

            return (sql.ToString(), parameters);
        }

        private void BroadcastBatch(IReadOnlyList<OperationalLogEvent> events)
        {
            if (_broadcaster == null)
            {
                return;
            }

            foreach (OperationalLogEvent logEvent in events)
            {
                _broadcaster.Publish(logEvent);
            }
        }
    }
}
