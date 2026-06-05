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
            return CreateLogEventAt(DateTimeOffset.UtcNow, template, level, properties);
        }

        private static LogEvent CreateLogEventAt(DateTimeOffset timestamp, string template, LogEventLevel level, params LogEventProperty[] properties)
        {
            MessageTemplate parsed = new MessageTemplateParser().Parse(template);
            return new LogEvent(timestamp, level, exception: null, parsed, properties);
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
            Assert.Equal("one", first!.Message);
            Assert.Equal("two", second!.Message);
            Assert.False(reader.TryRead(out _));
        }

        [Fact]
        public async Task GetAsync_PagesThroughResults_WithoutOverlapOrGaps()
        {
            OperationalLogSink sink = new OperationalLogSink(_dataSource, _options);
            await sink.EmitBatchAsync(new[]
            {
                CreateLogEvent("m1", LogEventLevel.Information),
                CreateLogEvent("m2", LogEventLevel.Information),
                CreateLogEvent("m3", LogEventLevel.Information),
                CreateLogEvent("m4", LogEventLevel.Information),
                CreateLogEvent("m5", LogEventLevel.Information),
            });

            OperationalLogReadRepository repository = new OperationalLogReadRepository(_dataSource, _options);

            PagedResult<OperationalLogEntry> page1 = await repository.GetAsync(new OperationalLogFilters(), page: 1, perPage: 2);
            PagedResult<OperationalLogEntry> page2 = await repository.GetAsync(new OperationalLogFilters(), page: 2, perPage: 2);
            PagedResult<OperationalLogEntry> page3 = await repository.GetAsync(new OperationalLogFilters(), page: 3, perPage: 2);

            // Total is the unpaged count on every page; page sizes honor the limit and final remainder.
            Assert.Equal(5, page1.TotalCount);
            Assert.Equal(5, page2.TotalCount);
            Assert.Equal(5, page3.TotalCount);
            Assert.Equal(2, page1.Items.Count);
            Assert.Equal(2, page2.Items.Count);
            Assert.Single(page3.Items);

            // The three pages partition all five rows with no overlap and no gaps.
            HashSet<string> seen = new HashSet<string>();
            foreach (OperationalLogEntry entry in page1.Items.Concat(page2.Items).Concat(page3.Items))
            {
                Assert.True(seen.Add(entry.Message), $"row '{entry.Message}' appeared on more than one page");
            }
            Assert.Equal(new[] { "m1", "m2", "m3", "m4", "m5" }, seen.OrderBy(message => message).ToArray());
        }

        [Fact]
        public async Task EmitBatchAsync_WithMoreRowsThanOneInsertChunk_PersistsEveryRow()
        {
            // Derive the count from the chunk size so the test keeps forcing the multi-chunk INSERT
            // loop (here: a full chunk, a second full chunk, and a partial remainder) even if the
            // constant changes — rather than silently stopping at a hard-coded value.
            int rowCount = (OperationalLogSink.MaxRowsPerInsert * 2) + 20;

            OperationalLogSink sink = new OperationalLogSink(_dataSource, _options);
            LogEvent[] events = Enumerable.Range(0, rowCount)
                .Select(index => CreateLogEvent($"row-{index:D4}", LogEventLevel.Information))
                .ToArray();

            await sink.EmitBatchAsync(events);

            OperationalLogReadRepository repository = new OperationalLogReadRepository(_dataSource, _options);
            PagedResult<OperationalLogEntry> page = await repository.GetAsync(new OperationalLogFilters(), page: 1, perPage: rowCount + 50);

            Assert.Equal(rowCount, page.TotalCount);
            Assert.Equal(rowCount, page.Items.Count);
            Assert.Equal(rowCount, page.Items.Select(entry => entry.Message).Distinct().Count());
        }

        [Fact]
        public async Task GetAsync_WithSourceContextFilter_ReturnsOnlyMatchingRows()
        {
            OperationalLogSink sink = new OperationalLogSink(_dataSource, _options);
            await sink.EmitBatchAsync(new[]
            {
                CreateLogEvent("a", LogEventLevel.Information, new LogEventProperty("SourceContext", new ScalarValue("App.Accounts"))),
                CreateLogEvent("b", LogEventLevel.Information, new LogEventProperty("SourceContext", new ScalarValue("App.Billing"))),
                CreateLogEvent("c", LogEventLevel.Information, new LogEventProperty("SourceContext", new ScalarValue("App.Accounts"))),
            });

            OperationalLogReadRepository repository = new OperationalLogReadRepository(_dataSource, _options);
            PagedResult<OperationalLogEntry> page = await repository.GetAsync(
                new OperationalLogFilters { SourceContext = "App.Accounts" }, page: 1, perPage: 50);

            Assert.Equal(2, page.TotalCount);
            Assert.All(page.Items, entry => Assert.Equal("App.Accounts", entry.SourceContext));
        }

        [Fact]
        public async Task GetAsync_WithFromAndToFilters_RestrictsToTheTimeWindow()
        {
            OperationalLogSink sink = new OperationalLogSink(_dataSource, _options);
            await sink.EmitBatchAsync(new[]
            {
                CreateLogEventAt(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), "old", LogEventLevel.Information),
                CreateLogEventAt(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), "new", LogEventLevel.Information),
            });

            OperationalLogReadRepository repository = new OperationalLogReadRepository(_dataSource, _options);

            PagedResult<OperationalLogEntry> from2022 = await repository.GetAsync(
                new OperationalLogFilters { From = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc) }, page: 1, perPage: 50);
            PagedResult<OperationalLogEntry> to2022 = await repository.GetAsync(
                new OperationalLogFilters { To = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc) }, page: 1, perPage: 50);

            Assert.Equal("new", Assert.Single(from2022.Items).Message);
            Assert.Equal("old", Assert.Single(to2022.Items).Message);
        }
    }
}
