using System.Data.Common;
using System.Threading.Channels;
using EasyReasy.Database.Logging.Broadcasting;
using EasyReasy.Database.Logging.Models;
using EasyReasy.Database.Logging.Reading;
using EasyReasy.Database.Logging.Sinks;
using EasyReasy.Database.Mapping;
using EasyReasy.Database.Pagination;
using EasyReasy.Database.Sqlite;
using Serilog.Events;
using Serilog.Parsing;

namespace EasyReasy.Database.Logging.Tests
{
    /// <summary>
    /// Exercises the provider-agnostic write + read path against SQLite, proving the core does not
    /// depend on any Postgres-specific behaviour.
    /// </summary>
    public class OperationalLogSinkTests : IAsyncLifetime
    {
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"easyreasy-oplog-{Guid.NewGuid():N}.db");
        private readonly OperationalLoggingOptions _options = new OperationalLoggingOptions();
        private DbDataSource _dataSource = null!;

        public async Task InitializeAsync()
        {
            // Surface any best-effort sink failure (the sink swallows exceptions by design).
            Serilog.Debugging.SelfLog.Enable(Console.Error);

            _dataSource = new SqliteDataSourceFactory().CreateDataSource($"Data Source={_databasePath}");

            await using DbConnection connection = await _dataSource.OpenConnectionAsync();
            await connection.ExecuteAsync(@"
                CREATE TABLE operational_log (
                    id               INTEGER PRIMARY KEY AUTOINCREMENT,
                    created_at       TEXT NOT NULL,
                    level            TEXT NOT NULL,
                    source_context   TEXT,
                    message          TEXT NOT NULL,
                    message_template TEXT,
                    exception        TEXT,
                    correlation_id   TEXT,
                    user_id          TEXT,
                    properties       TEXT
                )");
        }

        public async Task DisposeAsync()
        {
            if (_dataSource is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }

            try
            {
                File.Delete(_databasePath);
            }
            catch (IOException)
            {
                // Best-effort temp cleanup; a leaked temp file is harmless.
            }
        }

        private static LogEvent CreateLogEvent(string template, LogEventLevel level, params LogEventProperty[] properties)
        {
            MessageTemplate parsed = new MessageTemplateParser().Parse(template);
            return new LogEvent(DateTimeOffset.UtcNow, level, exception: null, parsed, properties);
        }

        [Fact]
        public async Task EmitBatchAsync_WhenEventsWritten_TheyReadBackThroughTheRepository()
        {
            OperationalLogSink sink = new OperationalLogSink(_dataSource, _options);

            LogEvent info = CreateLogEvent(
                "User {UserId} did {Action}",
                LogEventLevel.Information,
                new LogEventProperty("UserId", new ScalarValue("u1")),
                new LogEventProperty("Action", new ScalarValue("login")),
                new LogEventProperty("CorrelationId", new ScalarValue("trace-123")),
                new LogEventProperty("SourceContext", new ScalarValue("MyApp.Accounts")));
            LogEvent warning = CreateLogEvent("disk space low", LogEventLevel.Warning);

            await sink.EmitBatchAsync(new[] { info, warning });

            OperationalLogReadRepository repository = new OperationalLogReadRepository(_dataSource, _options);
            PagedResult<OperationalLogEntry> page = await repository.GetAsync(new OperationalLogFilters(), page: 1, perPage: 50);

            Assert.Equal(2, page.TotalCount);

            OperationalLogEntry infoRow = page.Items.Single(row => row.Level == "Information");
            Assert.Equal("u1", infoRow.UserId);
            Assert.Equal("trace-123", infoRow.CorrelationId);
            Assert.Equal("MyApp.Accounts", infoRow.SourceContext);
            Assert.NotNull(infoRow.Properties);
            // The non-column property lands in the JSON blob with a camelCased key.
            Assert.Contains("\"action\"", infoRow.Properties);
            Assert.Contains("login", infoRow.Properties);

            // The SourceContext / CorrelationId / UserId properties were promoted to columns,
            // so they must NOT also appear in the properties blob.
            Assert.DoesNotContain("sourceContext", infoRow.Properties);
        }

        [Fact]
        public async Task GetAsync_WithLevelFilter_ReturnsOnlyMatchingRows()
        {
            OperationalLogSink sink = new OperationalLogSink(_dataSource, _options);
            await sink.EmitBatchAsync(new[]
            {
                CreateLogEvent("a", LogEventLevel.Information),
                CreateLogEvent("b", LogEventLevel.Error),
                CreateLogEvent("c", LogEventLevel.Error),
            });

            OperationalLogReadRepository repository = new OperationalLogReadRepository(_dataSource, _options);
            PagedResult<OperationalLogEntry> page = await repository.GetAsync(
                new OperationalLogFilters { Level = "Error" }, page: 1, perPage: 50);

            Assert.Equal(2, page.TotalCount);
            Assert.All(page.Items, row => Assert.Equal("Error", row.Level));
        }

        [Fact]
        public async Task EmitBatchAsync_WhenBroadcasterSupplied_PublishesEveryRow()
        {
            InMemoryOperationalLogBroadcaster broadcaster = new InMemoryOperationalLogBroadcaster();
            ChannelReader<OperationalLogEvent> reader = broadcaster.Subscribe();
            OperationalLogSink sink = new OperationalLogSink(_dataSource, _options, broadcaster);

            await sink.EmitBatchAsync(new[]
            {
                CreateLogEvent("one", LogEventLevel.Information),
                CreateLogEvent("two", LogEventLevel.Information),
            });

            Assert.True(reader.TryRead(out OperationalLogEvent? first));
            Assert.True(reader.TryRead(out OperationalLogEvent? second));
            Assert.NotNull(first);
            Assert.NotNull(second);
        }
    }
}
