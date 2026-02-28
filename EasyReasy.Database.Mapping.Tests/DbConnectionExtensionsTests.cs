using System.Data.Common;

namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// Integration tests for DbConnectionExtensions against a real PostgreSQL database.
    /// </summary>
    [Collection("Database")]
    public class DbConnectionExtensionsTests : IAsyncLifetime
    {
        private readonly TestDatabaseFixture _fixture;
        private DbConnection _connection = null!;
        private DbTransaction _transaction = null!;

        public DbConnectionExtensionsTests(TestDatabaseFixture fixture)
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

        #region ExecuteAsync

        [Fact]
        public async Task ExecuteAsync_Insert_ReturnsRowCount()
        {
            int rowsAffected = await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "test1", value = 42 },
                _transaction);

            Assert.Equal(1, rowsAffected);
        }

        [Fact]
        public async Task ExecuteAsync_Update_ReturnsAffectedRowCount()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "update_test", value = 1 },
                _transaction);

            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "update_test", value = 2 },
                _transaction);

            int rowsAffected = await _connection.ExecuteAsync(
                "UPDATE mapping_test SET value = 99 WHERE name = @name",
                new { name = "update_test" },
                _transaction);

            Assert.Equal(2, rowsAffected);
        }

        [Fact]
        public async Task ExecuteAsync_Delete_ReturnsAffectedRowCount()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name) VALUES (@name)",
                new { name = "delete_test" },
                _transaction);

            int rowsAffected = await _connection.ExecuteAsync(
                "DELETE FROM mapping_test WHERE name = @name",
                new { name = "delete_test" },
                _transaction);

            Assert.Equal(1, rowsAffected);
        }

        [Fact]
        public async Task ExecuteAsync_NoMatchingRows_ReturnsZero()
        {
            int rowsAffected = await _connection.ExecuteAsync(
                "DELETE FROM mapping_test WHERE name = @name",
                new { name = "nonexistent" },
                _transaction);

            Assert.Equal(0, rowsAffected);
        }

        #endregion

        #region QueryAsync

        [Fact]
        public async Task QueryAsync_MultipleRows_ReturnsAll()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "query_a", value = 1 },
                _transaction);

            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "query_b", value = 2 },
                _transaction);

            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                "SELECT id AS Id, name AS Name, value AS Value, is_active AS IsActive FROM mapping_test WHERE name LIKE @pattern ORDER BY name",
                new { pattern = "query_%" },
                _transaction);

            List<MappingTestEntity> list = results.ToList();
            Assert.Equal(2, list.Count);
            Assert.Equal("query_a", list[0].Name);
            Assert.Equal("query_b", list[1].Name);
            Assert.Equal(1, list[0].Value);
            Assert.Equal(2, list[1].Value);
        }

        [Fact]
        public async Task QueryAsync_NoRows_ReturnsEmptyEnumerable()
        {
            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                "SELECT id AS Id, name AS Name FROM mapping_test WHERE name = @name",
                new { name = "nonexistent" },
                _transaction);

            Assert.Empty(results);
        }

        [Fact]
        public async Task QueryAsync_MapsNullableColumns()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value, description) VALUES (@name, NULL, NULL)",
                new { name = "nullable_test" },
                _transaction);

            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                "SELECT name AS Name, value AS Value, description AS Description FROM mapping_test WHERE name = @name",
                new { name = "nullable_test" },
                _transaction);

            MappingTestEntity entity = results.Single();
            Assert.Equal("nullable_test", entity.Name);
            Assert.Null(entity.Value);
            Assert.Null(entity.Description);
        }

        [Fact]
        public async Task QueryAsync_MapsDecimalColumn()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, score) VALUES (@name, @score)",
                new { name = "decimal_test", score = 99.95m },
                _transaction);

            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                "SELECT name AS Name, score AS Score FROM mapping_test WHERE name = @name",
                new { name = "decimal_test" },
                _transaction);

            MappingTestEntity entity = results.Single();
            Assert.Equal(99.95m, entity.Score);
        }

        [Fact]
        public async Task QueryAsync_MapsBooleanColumn()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, is_active) VALUES (@name, @isActive)",
                new { name = "bool_test", isActive = false },
                _transaction);

            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                "SELECT name AS Name, is_active AS IsActive FROM mapping_test WHERE name = @name",
                new { name = "bool_test" },
                _transaction);

            MappingTestEntity entity = results.Single();
            Assert.False(entity.IsActive);
        }

        #endregion

        #region QuerySingleAsync

        [Fact]
        public async Task QuerySingleAsync_OneRow_ReturnsEntity()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "single_test", value = 77 },
                _transaction);

            MappingTestEntity result = await _connection.QuerySingleAsync<MappingTestEntity>(
                "SELECT id AS Id, name AS Name, value AS Value FROM mapping_test WHERE name = @name",
                new { name = "single_test" },
                _transaction);

            Assert.Equal("single_test", result.Name);
            Assert.Equal(77, result.Value);
        }

        [Fact]
        public async Task QuerySingleAsync_NoRows_ThrowsInvalidOperationException()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _connection.QuerySingleAsync<MappingTestEntity>(
                    "SELECT id AS Id, name AS Name FROM mapping_test WHERE name = @name",
                    new { name = "nonexistent" },
                    _transaction));
        }

        [Fact]
        public async Task QuerySingleAsync_MultipleRows_ThrowsInvalidOperationException()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name) VALUES (@name)",
                new { name = "multi_single" },
                _transaction);

            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name) VALUES (@name)",
                new { name = "multi_single" },
                _transaction);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _connection.QuerySingleAsync<MappingTestEntity>(
                    "SELECT id AS Id, name AS Name FROM mapping_test WHERE name = @name",
                    new { name = "multi_single" },
                    _transaction));
        }

        [Fact]
        public async Task QuerySingleAsync_ScalarInt_ReturnsValue()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name) VALUES (@name)",
                new { name = "scalar_count" },
                _transaction);

            int count = await _connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM mapping_test WHERE name = @name",
                new { name = "scalar_count" },
                _transaction);

            Assert.Equal(1, count);
        }

        #endregion

        #region QuerySingleOrDefaultAsync

        [Fact]
        public async Task QuerySingleOrDefaultAsync_OneRow_ReturnsEntity()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "sod_test", value = 33 },
                _transaction);

            MappingTestEntity? result = await _connection.QuerySingleOrDefaultAsync<MappingTestEntity>(
                "SELECT id AS Id, name AS Name, value AS Value FROM mapping_test WHERE name = @name",
                new { name = "sod_test" },
                _transaction);

            Assert.NotNull(result);
            Assert.Equal("sod_test", result.Name);
            Assert.Equal(33, result.Value);
        }

        [Fact]
        public async Task QuerySingleOrDefaultAsync_NoRows_ReturnsNull()
        {
            MappingTestEntity? result = await _connection.QuerySingleOrDefaultAsync<MappingTestEntity>(
                "SELECT id AS Id, name AS Name FROM mapping_test WHERE name = @name",
                new { name = "nonexistent" },
                _transaction);

            Assert.Null(result);
        }

        [Fact]
        public async Task QuerySingleOrDefaultAsync_MultipleRows_ThrowsInvalidOperationException()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name) VALUES (@name)",
                new { name = "multi_sod" },
                _transaction);

            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name) VALUES (@name)",
                new { name = "multi_sod" },
                _transaction);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _connection.QuerySingleOrDefaultAsync<MappingTestEntity>(
                    "SELECT id AS Id, name AS Name FROM mapping_test WHERE name = @name",
                    new { name = "multi_sod" },
                    _transaction));
        }

        [Fact]
        public async Task QuerySingleOrDefaultAsync_ScalarString_ReturnsValue()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name) VALUES (@name)",
                new { name = "scalar_str" },
                _transaction);

            string? result = await _connection.QuerySingleOrDefaultAsync<string>(
                "SELECT name FROM mapping_test WHERE name = @name",
                new { name = "scalar_str" },
                _transaction);

            Assert.Equal("scalar_str", result);
        }

        [Fact]
        public async Task QuerySingleOrDefaultAsync_ScalarNoRows_ReturnsDefault()
        {
            string? result = await _connection.QuerySingleOrDefaultAsync<string>(
                "SELECT name FROM mapping_test WHERE name = @name",
                new { name = "nonexistent" },
                _transaction);

            Assert.Null(result);
        }

        #endregion

        #region ExecuteScalarAsync

        [Fact]
        public async Task ExecuteScalarAsync_Count_ReturnsLong()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name) VALUES (@name)",
                new { name = "count_test" },
                _transaction);

            long? count = await _connection.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM mapping_test WHERE name = @name",
                new { name = "count_test" },
                _transaction);

            Assert.Equal(1L, count);
        }

        [Fact]
        public async Task ExecuteScalarAsync_Exists_ReturnsBool()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name) VALUES (@name)",
                new { name = "exists_test" },
                _transaction);

            bool? exists = await _connection.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM mapping_test WHERE name = @name)",
                new { name = "exists_test" },
                _transaction);

            Assert.True(exists);
        }

        [Fact]
        public async Task ExecuteScalarAsync_NotExists_ReturnsFalse()
        {
            bool? exists = await _connection.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM mapping_test WHERE name = @name)",
                new { name = "nonexistent" },
                _transaction);

            Assert.False(exists);
        }

        [Fact]
        public async Task ExecuteScalarAsync_NullableReturnsNull()
        {
            string? result = await _connection.ExecuteScalarAsync<string>(
                "SELECT description FROM mapping_test WHERE name = @name LIMIT 1",
                new { name = "nonexistent" },
                _transaction);

            Assert.Null(result);
        }

        #endregion

        #region QueryMultipleAsync

        [Fact]
        public async Task QueryMultipleAsync_ReadsMultipleResultSets()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "multi_a", value = 10 },
                _transaction);

            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "multi_b", value = 20 },
                _transaction);

            await using GridReader gridReader = await _connection.QueryMultipleAsync(
                @"SELECT COUNT(*) FROM mapping_test WHERE name LIKE 'multi_%';
                  SELECT id AS Id, name AS Name, value AS Value FROM mapping_test WHERE name LIKE 'multi_%' ORDER BY name",
                transaction: _transaction);

            long count = await gridReader.ReadSingleAsync<long>();
            Assert.Equal(2, count);

            IEnumerable<MappingTestEntity> rows = await gridReader.ReadAsync<MappingTestEntity>();
            List<MappingTestEntity> list = rows.ToList();
            Assert.Equal(2, list.Count);
            Assert.Equal("multi_a", list[0].Name);
            Assert.Equal("multi_b", list[1].Name);
        }

        [Fact]
        public async Task QueryMultipleAsync_ReadSingleAsync_BoolAndLong()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name) VALUES (@name)",
                new { name = "multi_bool" },
                _transaction);

            await using GridReader gridReader = await _connection.QueryMultipleAsync(
                @"SELECT EXISTS(SELECT 1 FROM mapping_test WHERE name = @name);
                  SELECT COUNT(*) FROM mapping_test WHERE name = @name",
                new { name = "multi_bool" },
                _transaction);

            bool exists = await gridReader.ReadSingleAsync<bool>();
            Assert.True(exists);

            long count = await gridReader.ReadSingleAsync<long>();
            Assert.Equal(1, count);
        }

        #endregion

        #region Null Parameter Handling

        [Fact]
        public async Task ExecuteAsync_NullParameter_InsertsNull()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, description) VALUES (@name, @description)",
                new { name = "null_param", description = (string?)null },
                _transaction);

            MappingTestEntity? result = await _connection.QuerySingleOrDefaultAsync<MappingTestEntity>(
                "SELECT name AS Name, description AS Description FROM mapping_test WHERE name = @name",
                new { name = "null_param" },
                _transaction);

            Assert.NotNull(result);
            Assert.Null(result.Description);
        }

        #endregion

        #region No Parameters

        [Fact]
        public async Task QueryAsync_NoParameters_Works()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name) VALUES ('no_param_test')",
                transaction: _transaction);

            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                "SELECT name AS Name FROM mapping_test WHERE name = 'no_param_test'",
                transaction: _transaction);

            Assert.Single(results);
        }

        #endregion

        #region RETURNING Clause

        [Fact]
        public async Task QuerySingleAsync_ReturningClause_MapsInsertedRow()
        {
            MappingTestEntity result = await _connection.QuerySingleAsync<MappingTestEntity>(
                @"INSERT INTO mapping_test (name, value, description)
                  VALUES (@name, @value, @description)
                  RETURNING id AS Id, name AS Name, value AS Value, description AS Description, created_at AS CreatedAt",
                new { name = "returning_test", value = 55, description = "test desc" },
                _transaction);

            Assert.NotEqual(Guid.Empty, result.Id);
            Assert.Equal("returning_test", result.Name);
            Assert.Equal(55, result.Value);
            Assert.Equal("test desc", result.Description);
        }

        #endregion

        #region Full Entity Mapping

        [Fact]
        public async Task QuerySingleAsync_AllProperties_MapsCorrectly()
        {
            MappingTestEntity result = await _connection.QuerySingleAsync<MappingTestEntity>(
                @"INSERT INTO mapping_test (name, value, description, is_active, score)
                  VALUES (@name, @value, @description, @isActive, @score)
                  RETURNING id AS Id, name AS Name, value AS Value, created_at AS CreatedAt, description AS Description, is_active AS IsActive, score AS Score",
                new { name = "full_entity", value = 42, description = "full test", isActive = true, score = 99.95m },
                _transaction);

            Assert.NotEqual(Guid.Empty, result.Id);
            Assert.Equal("full_entity", result.Name);
            Assert.Equal(42, result.Value);
            Assert.True(result.CreatedAt > DateTime.MinValue);
            Assert.Equal("full test", result.Description);
            Assert.True(result.IsActive);
            Assert.Equal(99.95m, result.Score);
        }

        #endregion

        #region Guid Parameters

        [Fact]
        public async Task QuerySingleOrDefaultAsync_GuidParameter_Works()
        {
            MappingTestEntity inserted = await _connection.QuerySingleAsync<MappingTestEntity>(
                "INSERT INTO mapping_test (name) VALUES (@name) RETURNING id AS Id, name AS Name",
                new { name = "guid_test" },
                _transaction);

            MappingTestEntity? found = await _connection.QuerySingleOrDefaultAsync<MappingTestEntity>(
                "SELECT id AS Id, name AS Name FROM mapping_test WHERE id = @id",
                new { id = inserted.Id },
                _transaction);

            Assert.NotNull(found);
            Assert.Equal(inserted.Id, found.Id);
        }

        #endregion

        #region DateOnly and TimeOnly

        [Fact]
        public async Task QuerySingleAsync_MapsDateOnlyAndTimeOnly()
        {
            MappingTestEntity result = await _connection.QuerySingleAsync<MappingTestEntity>(
                @"INSERT INTO mapping_test (name, birth_date, start_time)
                  VALUES (@name, @birthDate, @startTime)
                  RETURNING name AS Name, birth_date AS BirthDate, start_time AS StartTime",
                new { name = "date_time_test", birthDate = new DateOnly(1990, 6, 15), startTime = new TimeOnly(14, 30, 0) },
                _transaction);

            Assert.Equal(new DateOnly(1990, 6, 15), result.BirthDate);
            Assert.Equal(new TimeOnly(14, 30, 0), result.StartTime);
        }

        [Fact]
        public async Task QuerySingleAsync_ScalarDateOnly_ReturnsValue()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, birth_date) VALUES (@name, @birthDate)",
                new { name = "scalar_date", birthDate = new DateOnly(2000, 1, 1) },
                _transaction);

            DateOnly result = await _connection.QuerySingleAsync<DateOnly>(
                "SELECT birth_date FROM mapping_test WHERE name = @name",
                new { name = "scalar_date" },
                _transaction);

            Assert.Equal(new DateOnly(2000, 1, 1), result);
        }

        [Fact]
        public async Task QuerySingleAsync_ScalarTimeOnly_ReturnsValue()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, start_time) VALUES (@name, @startTime)",
                new { name = "scalar_time", startTime = new TimeOnly(9, 45, 30) },
                _transaction);

            TimeOnly result = await _connection.QuerySingleAsync<TimeOnly>(
                "SELECT start_time FROM mapping_test WHERE name = @name",
                new { name = "scalar_time" },
                _transaction);

            Assert.Equal(new TimeOnly(9, 45, 30), result);
        }

        [Fact]
        public async Task QueryAsync_NullDateOnlyAndTimeOnly_MapsToNull()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name) VALUES (@name)",
                new { name = "null_date_time" },
                _transaction);

            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                "SELECT name AS Name, birth_date AS BirthDate, start_time AS StartTime FROM mapping_test WHERE name = @name",
                new { name = "null_date_time" },
                _transaction);

            MappingTestEntity entity = results.Single();
            Assert.Null(entity.BirthDate);
            Assert.Null(entity.StartTime);
        }

        #endregion

        #region QueryFirstOrDefaultAsync

        [Fact]
        public async Task QueryFirstOrDefaultAsync_OneRow_ReturnsEntity()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "first_test", value = 11 },
                _transaction);

            MappingTestEntity? result = await _connection.QueryFirstOrDefaultAsync<MappingTestEntity>(
                "SELECT name AS Name, value AS Value FROM mapping_test WHERE name = @name",
                new { name = "first_test" },
                _transaction);

            Assert.NotNull(result);
            Assert.Equal("first_test", result.Name);
            Assert.Equal(11, result.Value);
        }

        [Fact]
        public async Task QueryFirstOrDefaultAsync_NoRows_ReturnsNull()
        {
            MappingTestEntity? result = await _connection.QueryFirstOrDefaultAsync<MappingTestEntity>(
                "SELECT name AS Name FROM mapping_test WHERE name = @name",
                new { name = "nonexistent" },
                _transaction);

            Assert.Null(result);
        }

        [Fact]
        public async Task QueryFirstOrDefaultAsync_MultipleRows_ReturnsFirstWithoutThrowing()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "first_multi", value = 1 },
                _transaction);

            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "first_multi", value = 2 },
                _transaction);

            MappingTestEntity? result = await _connection.QueryFirstOrDefaultAsync<MappingTestEntity>(
                "SELECT name AS Name, value AS Value FROM mapping_test WHERE name = @name ORDER BY value",
                new { name = "first_multi" },
                _transaction);

            Assert.NotNull(result);
            Assert.Equal("first_multi", result.Name);
            Assert.Equal(1, result.Value);
        }

        [Fact]
        public async Task QueryFirstOrDefaultAsync_ScalarNoRows_ReturnsDefault()
        {
            string? result = await _connection.QueryFirstOrDefaultAsync<string>(
                "SELECT name FROM mapping_test WHERE name = @name",
                new { name = "nonexistent" },
                _transaction);

            Assert.Null(result);
        }

        #endregion

        #region DynamicParameters

        [Fact]
        public async Task DynamicParameters_Insert_BindsCorrectly()
        {
            DynamicParameters parameters = new DynamicParameters();
            parameters.Add("name", "dynamic_test");
            parameters.Add("value", 42);

            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                parameters,
                _transaction);

            MappingTestEntity? result = await _connection.QuerySingleOrDefaultAsync<MappingTestEntity>(
                "SELECT name AS Name, value AS Value FROM mapping_test WHERE name = @name",
                new { name = "dynamic_test" },
                _transaction);

            Assert.NotNull(result);
            Assert.Equal("dynamic_test", result.Name);
            Assert.Equal(42, result.Value);
        }

        [Fact]
        public async Task DynamicParameters_BulkInsert_BindsAllParameters()
        {
            string[] names = { "bulk_a", "bulk_b", "bulk_c" };
            DynamicParameters parameters = new DynamicParameters();
            List<string> valueClauses = new();

            for (int i = 0; i < names.Length; i++)
            {
                string nameParam = $"name_{i}";
                string valueParam = $"value_{i}";
                parameters.Add(nameParam, names[i]);
                parameters.Add(valueParam, i + 1);
                valueClauses.Add($"(@{nameParam}, @{valueParam})");
            }

            string sql = $"INSERT INTO mapping_test (name, value) VALUES {string.Join(", ", valueClauses)}";
            await _connection.ExecuteAsync(sql, parameters, _transaction);

            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                "SELECT name AS Name, value AS Value FROM mapping_test WHERE name LIKE 'bulk_%' ORDER BY name",
                transaction: _transaction);

            List<MappingTestEntity> list = results.ToList();
            Assert.Equal(3, list.Count);
            Assert.Equal("bulk_a", list[0].Name);
            Assert.Equal(1, list[0].Value);
            Assert.Equal("bulk_b", list[1].Name);
            Assert.Equal(2, list[1].Value);
            Assert.Equal("bulk_c", list[2].Name);
            Assert.Equal(3, list[2].Value);
        }

        [Fact]
        public async Task DynamicParameters_NullValue_InsertsNull()
        {
            DynamicParameters parameters = new DynamicParameters();
            parameters.Add("name", "dynamic_null");
            parameters.Add("description", null);

            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, description) VALUES (@name, @description)",
                parameters,
                _transaction);

            MappingTestEntity? result = await _connection.QuerySingleOrDefaultAsync<MappingTestEntity>(
                "SELECT name AS Name, description AS Description FROM mapping_test WHERE name = @name",
                new { name = "dynamic_null" },
                _transaction);

            Assert.NotNull(result);
            Assert.Null(result.Description);
        }

        #endregion
    }
}

