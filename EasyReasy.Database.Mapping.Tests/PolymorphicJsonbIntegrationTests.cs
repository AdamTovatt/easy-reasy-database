using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// End-to-end tests against real PostgreSQL JSONB to verify the polymorphic JSON handler
    /// reads correctly even after JSONB's length-first key reordering pushes the discriminator
    /// out of the first position. Uses a per-test transaction so DDL is rolled back.
    /// </summary>
    [Collection("Database")]
    public class PolymorphicJsonbIntegrationTests : IAsyncLifetime
    {
        private readonly TestDatabaseFixture _fixture;
        private DbConnection _connection = null!;
        private DbTransaction _transaction = null!;

        public PolymorphicJsonbIntegrationTests(TestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        public async Task InitializeAsync()
        {
            _connection = await _fixture.DataSource.OpenConnectionAsync();
            _transaction = await _connection.BeginTransactionAsync();

            // Create a per-transaction table for JSONB experiments. PostgreSQL allows DDL
            // inside a transaction, so the rollback in DisposeAsync removes it cleanly.
            await _connection.ExecuteAsync(
                @"CREATE TABLE polymorphic_jsonb_test (
                    id SERIAL PRIMARY KEY,
                    payload JSONB NOT NULL
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
        public async Task Handler_RoundTripsThroughJsonb_WhenDiscriminatorIsReordered()
        {
            // Use camelCase so the property names stored in JSONB are 'name' (4) / 'breed' (5)
            // / 'type' (4). PostgreSQL JSONB stores keys length-first then lex within length,
            // so 'type' will not be the first key on read-back even though we emitted it first.
            JsonSerializerOptions jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };
            TypeHandlerRegistry.AddTypeHandler(new PolymorphicJsonTypeHandler<JsonbAnimal>(jsonOptions));

            JsonbDog inserted = new JsonbDog { Name = "Rex", Breed = "labrador" };

            await _connection.ExecuteAsync(
                "INSERT INTO polymorphic_jsonb_test (payload) VALUES (@payload::jsonb)",
                new { payload = JsonSerializer.Serialize<JsonbAnimal>(inserted, jsonOptions) },
                _transaction);

            string raw = (await _connection.QuerySingleAsync<string>(
                "SELECT payload::text FROM polymorphic_jsonb_test",
                transaction: _transaction))!;

            // Test premise: after JSONB's key reordering the discriminator is no longer the
            // first key. Expressed as IndexOf > 1 (rather than coupling to *which* other key
            // happens to be first, which depends on PG's exact ordering algorithm).
            int typeIndex = raw.IndexOf("\"type\"");
            Assert.True(typeIndex > 1,
                $"Test premise: discriminator should not be the first key after JSONB reorder; got '{raw}'.");

            // Read via entity mapping — the Payload property's type is JsonbAnimal,
            // so RowDeserializer dispatches the registered PolymorphicJsonTypeHandler<JsonbAnimal>.
            JsonbRecord? loaded = await _connection.QuerySingleOrDefaultAsync<JsonbRecord>(
                "SELECT payload::text AS payload FROM polymorphic_jsonb_test LIMIT 1",
                transaction: _transaction);

            Assert.NotNull(loaded);
            JsonbDog dog = Assert.IsType<JsonbDog>(loaded.Payload);
            Assert.Equal("Rex", dog.Name);
            Assert.Equal("labrador", dog.Breed);
        }

        [Fact]
        public async Task DefaultStj_FailsOnReorderedJsonb_DocumentingTheBug()
        {
            // Demonstrates that without the order-insensitive converter the read fails.
            JsonSerializerOptions defaultOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };

            JsonbDog inserted = new JsonbDog { Name = "Rex", Breed = "labrador" };

            await _connection.ExecuteAsync(
                "INSERT INTO polymorphic_jsonb_test (payload) VALUES (@payload::jsonb)",
                new { payload = JsonSerializer.Serialize<JsonbAnimal>(inserted, defaultOptions) },
                _transaction);

            string roundTripped = (await _connection.QuerySingleAsync<string>(
                "SELECT payload::text FROM polymorphic_jsonb_test",
                transaction: _transaction))!;

            Assert.Throws<NotSupportedException>(() =>
                JsonSerializer.Deserialize<JsonbAnimal>(roundTripped, defaultOptions));
        }

        #region Test types

        [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
        [JsonDerivedType(typeof(JsonbDog), "dog")]
        [JsonDerivedType(typeof(JsonbCat), "cat")]
        public abstract class JsonbAnimal
        {
            public string Name { get; set; } = string.Empty;
        }

        public class JsonbDog : JsonbAnimal
        {
            public string Breed { get; set; } = string.Empty;
        }

        public class JsonbCat : JsonbAnimal
        {
            public string Color { get; set; } = string.Empty;
        }

        public class JsonbRecord
        {
            public JsonbAnimal? Payload { get; set; }
        }

        #endregion
    }
}
