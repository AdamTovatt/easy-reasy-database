using System.Data.Common;

namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// Tests for constructor-based entity creation, including constructor-only entities,
    /// hybrid entities (constructor + settable properties), and combined constructor + snake_case.
    /// </summary>
    [Collection("Database")]
    public class ConstructorMappingTests : IAsyncLifetime
    {
        private readonly TestDatabaseFixture _fixture;
        private DbConnection _connection = null!;
        private DbTransaction _transaction = null!;

        public ConstructorMappingTests(TestDatabaseFixture fixture)
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
        public async Task QuerySingleAsync_ConstructorEntity_MapsCorrectly()
        {
            ConstructorMappingTestEntity result = await _connection.QuerySingleAsync<ConstructorMappingTestEntity>(
                @"INSERT INTO mapping_test (name, value)
                  VALUES (@name, @value)
                  RETURNING id AS Id, name AS Name, value AS Value",
                new { name = "ctor_test", value = 42 },
                _transaction);

            Assert.NotEqual(Guid.Empty, result.Id);
            Assert.Equal("ctor_test", result.Name);
            Assert.Equal(42, result.Value);
        }

        [Fact]
        public async Task QuerySingleAsync_ConstructorEntity_NullableParamWithNull()
        {
            ConstructorMappingTestEntity result = await _connection.QuerySingleAsync<ConstructorMappingTestEntity>(
                @"INSERT INTO mapping_test (name, value)
                  VALUES (@name, NULL)
                  RETURNING id AS Id, name AS Name, value AS Value",
                new { name = "ctor_null" },
                _transaction);

            Assert.Equal("ctor_null", result.Name);
            Assert.Null(result.Value);
        }

        [Fact]
        public async Task QuerySingleAsync_ConstructorEntity_MissingColumn_UsesDefault()
        {
            // Only select id and name — value column is missing, should default to null
            ConstructorMappingTestEntity result = await _connection.QuerySingleAsync<ConstructorMappingTestEntity>(
                @"INSERT INTO mapping_test (name, value)
                  VALUES (@name, @value)
                  RETURNING id AS Id, name AS Name",
                new { name = "ctor_missing", value = 99 },
                _transaction);

            Assert.Equal("ctor_missing", result.Name);
            Assert.Null(result.Value); // default for int?
        }

        [Fact]
        public async Task QuerySingleAsync_HybridEntity_MapsConstructorAndProperties()
        {
            ConstructorWithExtraPropertiesEntity result = await _connection.QuerySingleAsync<ConstructorWithExtraPropertiesEntity>(
                @"INSERT INTO mapping_test (name, description, is_active, score)
                  VALUES (@name, @description, @isActive, @score)
                  RETURNING id AS Id, name AS Name, description AS Description, is_active AS IsActive, score AS Score",
                new { name = "hybrid_test", description = "hybrid desc", isActive = true, score = 55.5m },
                _transaction);

            Assert.NotEqual(Guid.Empty, result.Id);
            Assert.Equal("hybrid_test", result.Name);
            Assert.Equal("hybrid desc", result.Description);
            Assert.True(result.IsActive);
            Assert.Equal(55.5m, result.Score);
        }

        [Fact]
        public async Task QueryAsync_ConstructorEntity_MultipleRows()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "ctor_multi_a", value = 1 },
                _transaction);

            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "ctor_multi_b", value = 2 },
                _transaction);

            IEnumerable<ConstructorMappingTestEntity> results = await _connection.QueryAsync<ConstructorMappingTestEntity>(
                "SELECT id AS Id, name AS Name, value AS Value FROM mapping_test WHERE name LIKE 'ctor_multi_%' ORDER BY name",
                transaction: _transaction);

            List<ConstructorMappingTestEntity> list = results.ToList();
            Assert.Equal(2, list.Count);
            Assert.Equal("ctor_multi_a", list[0].Name);
            Assert.Equal(1, list[0].Value);
            Assert.Equal("ctor_multi_b", list[1].Name);
            Assert.Equal(2, list[1].Value);
        }

        [Fact]
        public async Task QuerySingleAsync_ConstructorEntity_WithSnakeCaseColumns()
        {
            ConstructorMappingTestEntity result = await _connection.QuerySingleAsync<ConstructorMappingTestEntity>(
                @"INSERT INTO mapping_test (name, value)
                  VALUES (@name, @value)
                  RETURNING id, name, value",
                new { name = "ctor_snake", value = 77 },
                _transaction);

            Assert.NotEqual(Guid.Empty, result.Id);
            Assert.Equal("ctor_snake", result.Name);
            Assert.Equal(77, result.Value);
        }

        [Fact]
        public async Task QuerySingleAsync_HybridEntity_WithSnakeCaseColumns()
        {
            ConstructorWithExtraPropertiesEntity result = await _connection.QuerySingleAsync<ConstructorWithExtraPropertiesEntity>(
                @"INSERT INTO mapping_test (name, description, is_active, score)
                  VALUES (@name, @description, @isActive, @score)
                  RETURNING id, name, description, is_active, score",
                new { name = "hybrid_snake", description = "hybrid snake desc", isActive = false, score = 33.3m },
                _transaction);

            Assert.NotEqual(Guid.Empty, result.Id);
            Assert.Equal("hybrid_snake", result.Name);
            Assert.Equal("hybrid snake desc", result.Description);
            Assert.False(result.IsActive);
            Assert.Equal(33.3m, result.Score);
        }

        [Fact]
        public async Task QueryAsync_ParameterlessEntity_StillWorks()
        {
            // Backward compatibility: parameterless entity should still work as before
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "compat_test", value = 100 },
                _transaction);

            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                "SELECT id AS Id, name AS Name, value AS Value, is_active AS IsActive FROM mapping_test WHERE name = @name",
                new { name = "compat_test" },
                _transaction);

            MappingTestEntity entity = results.Single();
            Assert.Equal("compat_test", entity.Name);
            Assert.Equal(100, entity.Value);
        }

        [Fact]
        public async Task QuerySingleOrDefaultAsync_ConstructorEntity_NoRows_ReturnsNull()
        {
            ConstructorMappingTestEntity? result = await _connection.QuerySingleOrDefaultAsync<ConstructorMappingTestEntity>(
                "SELECT id AS Id, name AS Name, value AS Value FROM mapping_test WHERE name = @name",
                new { name = "nonexistent" },
                _transaction);

            Assert.Null(result);
        }

        [Fact]
        public async Task GridReader_ConstructorEntity_Works()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "grid_ctor", value = 88 },
                _transaction);

            await using GridReader gridReader = await _connection.QueryMultipleAsync(
                @"SELECT COUNT(*) FROM mapping_test WHERE name = @name;
                  SELECT id AS Id, name AS Name, value AS Value FROM mapping_test WHERE name = @name",
                new { name = "grid_ctor" },
                _transaction);

            long count = await gridReader.ReadSingleAsync<long>();
            Assert.Equal(1, count);

            IEnumerable<ConstructorMappingTestEntity> rows = await gridReader.ReadAsync<ConstructorMappingTestEntity>();
            ConstructorMappingTestEntity entity = rows.Single();
            Assert.Equal("grid_ctor", entity.Name);
            Assert.Equal(88, entity.Value);
        }
    }
}
