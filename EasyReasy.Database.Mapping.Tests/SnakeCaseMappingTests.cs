using System.Data.Common;

namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// Tests for automatic snake_case → PascalCase column mapping.
    /// Verifies that SQL queries can omit AS aliases when column names use snake_case.
    /// </summary>
    [Collection("Database")]
    public class SnakeCaseMappingTests : IAsyncLifetime
    {
        private readonly TestDatabaseFixture _fixture;
        private DbConnection _connection = null!;
        private DbTransaction _transaction = null!;

        public SnakeCaseMappingTests(TestDatabaseFixture fixture)
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
        public async Task QueryAsync_SnakeCaseColumns_MapsToPascalCaseProperties()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value, is_active) VALUES (@name, @value, @isActive)",
                new { name = "snake_test", value = 42, isActive = true },
                _transaction);

            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                "SELECT name, value, is_active FROM mapping_test WHERE name = @name",
                new { name = "snake_test" },
                _transaction);

            MappingTestEntity entity = results.Single();
            Assert.Equal("snake_test", entity.Name);
            Assert.Equal(42, entity.Value);
            Assert.True(entity.IsActive);
        }

        [Fact]
        public async Task QueryAsync_AllSnakeCaseColumns_MapsCorrectly()
        {
            await _connection.ExecuteAsync(
                @"INSERT INTO mapping_test (name, value, description, is_active, score, birth_date, start_time)
                  VALUES (@name, @value, @description, @isActive, @score, @birthDate, @startTime)",
                new
                {
                    name = "all_snake",
                    value = 99,
                    description = "full test",
                    isActive = false,
                    score = 88.5m,
                    birthDate = new DateOnly(1990, 3, 15),
                    startTime = new TimeOnly(8, 30, 0)
                },
                _transaction);

            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                @"SELECT id, name, value, created_at, description, is_active, score, birth_date, start_time
                  FROM mapping_test WHERE name = @name",
                new { name = "all_snake" },
                _transaction);

            MappingTestEntity entity = results.Single();
            Assert.Equal("all_snake", entity.Name);
            Assert.Equal(99, entity.Value);
            Assert.Equal("full test", entity.Description);
            Assert.False(entity.IsActive);
            Assert.Equal(88.5m, entity.Score);
            Assert.Equal(new DateOnly(1990, 3, 15), entity.BirthDate);
            Assert.Equal(new TimeOnly(8, 30, 0), entity.StartTime);
            Assert.NotEqual(Guid.Empty, entity.Id);
            Assert.True(entity.CreatedAt > DateTime.MinValue);
        }

        [Fact]
        public async Task QueryAsync_NullableSnakeCaseColumns_MapsNullCorrectly()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name) VALUES (@name)",
                new { name = "snake_nullable" },
                _transaction);

            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                "SELECT name, value, description, score, birth_date, start_time FROM mapping_test WHERE name = @name",
                new { name = "snake_nullable" },
                _transaction);

            MappingTestEntity entity = results.Single();
            Assert.Equal("snake_nullable", entity.Name);
            Assert.Null(entity.Value);
            Assert.Null(entity.Description);
            Assert.Null(entity.Score);
            Assert.Null(entity.BirthDate);
            Assert.Null(entity.StartTime);
        }

        [Fact]
        public async Task QueryAsync_MixedAliasAndSnakeCase_BothWork()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value, is_active, score) VALUES (@name, @value, @isActive, @score)",
                new { name = "mixed_test", value = 7, isActive = true, score = 3.14m },
                _transaction);

            // Mix of aliased (Name, Value) and snake_case (is_active, score)
            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                "SELECT name AS Name, value AS Value, is_active, score FROM mapping_test WHERE name = @name",
                new { name = "mixed_test" },
                _transaction);

            MappingTestEntity entity = results.Single();
            Assert.Equal("mixed_test", entity.Name);
            Assert.Equal(7, entity.Value);
            Assert.True(entity.IsActive);
            Assert.Equal(3.14m, entity.Score);
        }

        [Fact]
        public async Task QuerySingleAsync_ReturningClause_WithoutAliases()
        {
            MappingTestEntity result = await _connection.QuerySingleAsync<MappingTestEntity>(
                @"INSERT INTO mapping_test (name, value, description, is_active, score)
                  VALUES (@name, @value, @description, @isActive, @score)
                  RETURNING id, name, value, description, is_active, score, created_at",
                new { name = "returning_snake", value = 55, description = "ret desc", isActive = true, score = 77.7m },
                _transaction);

            Assert.NotEqual(Guid.Empty, result.Id);
            Assert.Equal("returning_snake", result.Name);
            Assert.Equal(55, result.Value);
            Assert.Equal("ret desc", result.Description);
            Assert.True(result.IsActive);
            Assert.Equal(77.7m, result.Score);
            Assert.True(result.CreatedAt > DateTime.MinValue);
        }

        [Fact]
        public void SnakeCaseToPascalCase_ConvertsCorrectly()
        {
            Assert.Equal("IsActive", RowDeserializer.SnakeCaseToPascalCase("is_active"));
            Assert.Equal("CreatedAt", RowDeserializer.SnakeCaseToPascalCase("created_at"));
            Assert.Equal("BirthDate", RowDeserializer.SnakeCaseToPascalCase("birth_date"));
            Assert.Equal("StartTime", RowDeserializer.SnakeCaseToPascalCase("start_time"));
            Assert.Equal("Id", RowDeserializer.SnakeCaseToPascalCase("id"));
            Assert.Equal("Name", RowDeserializer.SnakeCaseToPascalCase("name"));
            Assert.Equal("MyLongColumnName", RowDeserializer.SnakeCaseToPascalCase("my_long_column_name"));
            Assert.Equal("", RowDeserializer.SnakeCaseToPascalCase(""));
        }
    }
}
