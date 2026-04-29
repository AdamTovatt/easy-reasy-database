using System.Data.Common;
using System.Text.Json;

namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// End-to-end test for <see cref="JsonTypeHandler{T}"/> against a real PostgreSQL
    /// JSONB column. Proves that registering the handler is enough — the row deserializer
    /// dispatches it on the property type, and a non-polymorphic shape (dictionary)
    /// round-trips through the registry without a manual map step.
    /// </summary>
    [Collection("Database")]
    public class JsonbColumnIntegrationTests : IAsyncLifetime
    {
        private readonly TestDatabaseFixture _fixture;
        private DbConnection _connection = null!;
        private DbTransaction _transaction = null!;

        public JsonbColumnIntegrationTests(TestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        public async Task InitializeAsync()
        {
            _connection = await _fixture.DataSource.OpenConnectionAsync();
            _transaction = await _connection.BeginTransactionAsync();

            await _connection.ExecuteAsync(
                @"CREATE TABLE jsonb_column_test (
                    id SERIAL PRIMARY KEY,
                    context JSONB NOT NULL
                );",
                transaction: _transaction);
        }

        public async Task DisposeAsync()
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
            TypeHandlerRegistry.Clear();
        }

        [Fact]
        public async Task Handler_RoundTripsDictionaryThroughJsonbColumn()
        {
            JsonSerializerOptions jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = null,
            };
            TypeHandlerRegistry.AddTypeHandler(
                new JsonTypeHandler<Dictionary<string, string>>(jsonOptions));

            Dictionary<string, string> original = new Dictionary<string, string>
            {
                ["coverId"] = "abc-123",
                ["correlationId"] = "xyz-789",
            };

            await _connection.ExecuteAsync(
                "INSERT INTO jsonb_column_test (context) VALUES (@context::jsonb)",
                new { context = JsonSerializer.Serialize(original, jsonOptions) },
                _transaction);

            // Read via entity mapping — Context is typed Dictionary<string, string>,
            // so RowDeserializer dispatches the registered JsonTypeHandler on it.
            JsonbDictionaryRecord? loaded = await _connection.QuerySingleOrDefaultAsync<JsonbDictionaryRecord>(
                "SELECT context::text AS context FROM jsonb_column_test LIMIT 1",
                transaction: _transaction);

            Assert.NotNull(loaded);
            Assert.NotNull(loaded!.Context);
            Assert.Equal("abc-123", loaded.Context!["coverId"]);
            Assert.Equal("xyz-789", loaded.Context["correlationId"]);
        }

        #region Test types

        public class JsonbDictionaryRecord
        {
            public Dictionary<string, string>? Context { get; set; }
        }

        #endregion
    }
}
