using System.Data.Common;

namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// Tests for parameter binding edge cases, including array parameters
    /// used with PostgreSQL's ANY() operator.
    /// </summary>
    [Collection("Database")]
    public class ParameterBindingTests : IAsyncLifetime
    {
        private readonly TestDatabaseFixture _fixture;
        private DbConnection _connection = null!;
        private DbTransaction _transaction = null!;

        public ParameterBindingTests(TestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        public async Task InitializeAsync()
        {
            _connection = await _fixture.DataSource.OpenConnectionAsync();
            _transaction = await _connection.BeginTransactionAsync();
        }

        public async Task DisposeAsync()
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
        }

        [Fact]
        public async Task ArrayParameter_StringArray_WorksWithAny()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name) VALUES (@name)",
                new { name = "arr_a" },
                _transaction);

            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name) VALUES (@name)",
                new { name = "arr_b" },
                _transaction);

            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name) VALUES (@name)",
                new { name = "arr_c" },
                _transaction);

            string[] names = new[] { "arr_a", "arr_c" };

            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                "SELECT name AS Name FROM mapping_test WHERE name = ANY(@names) ORDER BY name",
                new { names },
                _transaction);

            List<MappingTestEntity> list = results.ToList();
            Assert.Equal(2, list.Count);
            Assert.Equal("arr_a", list[0].Name);
            Assert.Equal("arr_c", list[1].Name);
        }

        [Fact]
        public async Task ArrayParameter_EmptyArray_ReturnsNoRows()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name) VALUES (@name)",
                new { name = "arr_empty" },
                _transaction);

            string[] names = Array.Empty<string>();

            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                "SELECT name AS Name FROM mapping_test WHERE name = ANY(@names)",
                new { names },
                _transaction);

            Assert.Empty(results);
        }

        [Fact]
        public async Task MultipleParameterTypes_MixedInOneQuery()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value, is_active, description) VALUES (@name, @value, @isActive, @description)",
                new { name = "mixed_params", value = 42, isActive = true, description = "hello" },
                _transaction);

            MappingTestEntity? result = await _connection.QuerySingleOrDefaultAsync<MappingTestEntity>(
                @"SELECT name AS Name, value AS Value, is_active AS IsActive, description AS Description
                  FROM mapping_test
                  WHERE name = @name AND value = @value AND is_active = @isActive",
                new { name = "mixed_params", value = 42, isActive = true },
                _transaction);

            Assert.NotNull(result);
            Assert.Equal("mixed_params", result.Name);
            Assert.Equal(42, result.Value);
            Assert.True(result.IsActive);
            Assert.Equal("hello", result.Description);
        }

        [Fact]
        public async Task EmptyParameterObject_Works()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name) VALUES ('empty_params_test')",
                transaction: _transaction);

            long? count = await _connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM mapping_test WHERE name = 'empty_params_test'",
                new { },
                _transaction);

            Assert.Equal(1L, count);
        }

        [Fact]
        public async Task NullParameterObject_Works()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name) VALUES ('null_params_test')",
                transaction: _transaction);

            long? count = await _connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM mapping_test WHERE name = 'null_params_test'",
                param: null,
                _transaction);

            Assert.Equal(1L, count);
        }

        [Fact]
        public async Task DictionaryParameter_BindsDynamicParameters()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "dict_test_a", value = 10 },
                _transaction);

            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "dict_test_b", value = 20 },
                _transaction);

            Dictionary<string, object> parameters = new()
            {
                ["name"] = "dict_test_a"
            };

            MappingTestEntity? result = await _connection.QuerySingleOrDefaultAsync<MappingTestEntity>(
                "SELECT name AS Name, value AS Value FROM mapping_test WHERE name = @name",
                parameters,
                _transaction);

            Assert.NotNull(result);
            Assert.Equal("dict_test_a", result.Name);
            Assert.Equal(10, result.Value);
        }

        [Fact]
        public async Task DictionaryParameter_NullableObjectValue_BindsAll()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "dict_nullable", value = 77 },
                _transaction);

            Dictionary<string, object?> parameters = new()
            {
                ["name"] = "dict_nullable",
                ["value"] = 77,
            };

            MappingTestEntity? result = await _connection.QuerySingleOrDefaultAsync<MappingTestEntity>(
                "SELECT name AS Name, value AS Value FROM mapping_test WHERE name = @name AND value = @value",
                parameters,
                _transaction);

            Assert.NotNull(result);
            Assert.Equal("dict_nullable", result.Name);
            Assert.Equal(77, result.Value);
        }

        [Fact]
        public async Task DictionaryParameter_TypedIntValue_BindsValue()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "dict_typed", value = 5 },
                _transaction);

            Dictionary<string, int> parameters = new()
            {
                ["value"] = 5,
            };

            int? count = await _connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM mapping_test WHERE value = @value AND name = 'dict_typed'",
                parameters,
                _transaction);

            Assert.Equal(1, count);
        }

        [Fact]
        public async Task DictionaryParameter_NullValue_BindsAsDbNull()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, description) VALUES (@name, NULL)",
                new { name = "dict_null_desc" },
                _transaction);

            Dictionary<string, object?> parameters = new()
            {
                ["name"] = "dict_null_desc",
                ["description"] = null,
            };

            int? count = await _connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM mapping_test WHERE name = @name AND description IS NOT DISTINCT FROM @description",
                parameters,
                _transaction);

            Assert.Equal(1, count);
        }

        [Fact]
        public async Task DictionaryLikeParameter_NotImplementingIDictionary_Throws()
        {
            FakeReadOnlyStringDictionary parameters = new();

            ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(async () =>
                await _connection.ExecuteAsync(
                    "SELECT @name",
                    parameters,
                    _transaction));

            Assert.Contains("does not implement IDictionary", exception.Message);
        }

        [Fact]
        public async Task DictionaryParameter_MultipleKeys_BindsAll()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "dict_multi", value = 99 },
                _transaction);

            Dictionary<string, object> parameters = new()
            {
                ["name"] = "dict_multi",
                ["value"] = 99
            };

            MappingTestEntity? result = await _connection.QuerySingleOrDefaultAsync<MappingTestEntity>(
                "SELECT name AS Name, value AS Value FROM mapping_test WHERE name = @name AND value = @value",
                parameters,
                _transaction);

            Assert.NotNull(result);
            Assert.Equal("dict_multi", result.Name);
            Assert.Equal(99, result.Value);
        }
    }
}
