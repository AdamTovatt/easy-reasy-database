using EasyReasy.Database.Logging;
using EasyReasy.Database.Logging.Broadcasting;
using EasyReasy.Database.Logging.Models;
using EasyReasy.Database.Logging.Serialization;
using Npgsql;
using NpgsqlTypes;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;

namespace EasyReasy.Database.Logging.Npgsql.Sinks
{
    /// <summary>
    /// High-throughput Serilog sink that writes operational log events to PostgreSQL using Npgsql's
    /// binary <c>COPY FROM STDIN</c> protocol — one connection per batch, no per-row round trips.
    /// Binds the <c>properties</c> column as native <c>jsonb</c>. Replaces the agnostic
    /// <c>INSERT</c> sink when <c>AddNpgsqlOperationalLogging</c> is called. Best-effort: a write
    /// failure is reported to <see cref="SelfLog"/> and the batch dropped, matching the core sink.
    /// </summary>
    public sealed class NpgsqlOperationalLogSink : IBatchedLogEventSink
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly IOperationalLogBroadcaster? _broadcaster;
        private readonly string _copyCommand;

        /// <summary>
        /// Creates the sink. <paramref name="broadcaster"/> is optional — when supplied, every
        /// successfully-persisted row is published to it after the COPY completes.
        /// </summary>
        public NpgsqlOperationalLogSink(
            NpgsqlDataSource dataSource,
            OperationalLoggingOptions options,
            IOperationalLogBroadcaster? broadcaster = null)
        {
            _dataSource = dataSource;
            _broadcaster = broadcaster;
            _copyCommand =
                $"COPY {options.TableName} " +
                "(created_at, level, source_context, message, message_template, exception, correlation_id, user_id, properties) " +
                "FROM STDIN (FORMAT BINARY)";
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
                await CopyBatchAsync(events);
                BroadcastBatch(events);
            }
            catch (Exception exception)
            {
                SelfLog.WriteLine("EasyReasy Npgsql operational log sink failed to write batch of {0}: {1}", events.Count, exception);
            }
        }

        /// <inheritdoc/>
        public Task OnEmptyBatchAsync() => Task.CompletedTask;

        private async Task CopyBatchAsync(IReadOnlyCollection<OperationalLogEvent> events)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
            await using NpgsqlBinaryImporter writer = await connection.BeginBinaryImportAsync(_copyCommand);

            foreach (OperationalLogEvent logEvent in events)
            {
                await writer.StartRowAsync();

                await writer.WriteAsync(logEvent.CreatedAt, NpgsqlDbType.TimestampTz);
                await writer.WriteAsync(logEvent.Level, NpgsqlDbType.Text);
                await WriteNullableTextAsync(writer, logEvent.SourceContext);
                await writer.WriteAsync(logEvent.Message, NpgsqlDbType.Text);
                await WriteNullableTextAsync(writer, logEvent.MessageTemplate);
                await WriteNullableTextAsync(writer, logEvent.Exception);
                await WriteNullableTextAsync(writer, logEvent.CorrelationId);
                await WriteNullableTextAsync(writer, logEvent.UserId);
                await WriteNullableJsonbAsync(writer, logEvent.Properties);
            }

            await writer.CompleteAsync();
        }

        private void BroadcastBatch(IReadOnlyCollection<OperationalLogEvent> events)
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

        private static async Task WriteNullableTextAsync(NpgsqlBinaryImporter writer, string? value)
        {
            if (value == null)
            {
                await writer.WriteNullAsync();
            }
            else
            {
                await writer.WriteAsync(value, NpgsqlDbType.Text);
            }
        }

        private static async Task WriteNullableJsonbAsync(NpgsqlBinaryImporter writer, string? value)
        {
            if (value == null)
            {
                await writer.WriteNullAsync();
            }
            else
            {
                await writer.WriteAsync(value, NpgsqlDbType.Jsonb);
            }
        }
    }
}
