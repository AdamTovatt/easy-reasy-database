using System.Data.Common;

namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// Tests for GridReader used with QueryMultipleAsync for reading multiple result sets.
    /// </summary>
    [Collection("Database")]
    public class GridReaderTests : IAsyncLifetime
    {
        private readonly TestDatabaseFixture _fixture;
        private DbConnection _connection = null!;
        private DbTransaction _transaction = null!;

        public GridReaderTests(TestDatabaseFixture fixture)
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
        public async Task GridReader_ThreeResultSets_ReadsAllSequentially()
        {
            await InsertRowAsync("grid_3_a", 1);
            await InsertRowAsync("grid_3_b", 2);

            await using GridReader gridReader = await _connection.QueryMultipleAsync(
                @"SELECT EXISTS(SELECT 1 FROM mapping_test WHERE name LIKE 'grid_3_%');
                  SELECT COUNT(*) FROM mapping_test WHERE name LIKE 'grid_3_%';
                  SELECT name AS Name, value AS Value FROM mapping_test WHERE name LIKE 'grid_3_%' ORDER BY name",
                transaction: _transaction);

            bool exists = await gridReader.ReadSingleAsync<bool>();
            Assert.True(exists);

            long count = await gridReader.ReadSingleAsync<long>();
            Assert.Equal(2, count);

            IEnumerable<MappingTestEntity> rows = await gridReader.ReadAsync<MappingTestEntity>();
            List<MappingTestEntity> list = rows.ToList();
            Assert.Equal(2, list.Count);
            Assert.Equal("grid_3_a", list[0].Name);
            Assert.Equal("grid_3_b", list[1].Name);
        }

        [Fact]
        public async Task GridReader_ReadAsync_EmptyResultSet_ReturnsEmpty()
        {
            await using GridReader gridReader = await _connection.QueryMultipleAsync(
                @"SELECT name AS Name FROM mapping_test WHERE name = 'nonexistent';
                  SELECT COUNT(*) FROM mapping_test WHERE name = 'nonexistent'",
                transaction: _transaction);

            IEnumerable<MappingTestEntity> rows = await gridReader.ReadAsync<MappingTestEntity>();
            Assert.Empty(rows);

            long count = await gridReader.ReadSingleAsync<long>();
            Assert.Equal(0, count);
        }

        [Fact]
        public async Task GridReader_WithParameters_BindsCorrectly()
        {
            await InsertRowAsync("grid_param", 99);

            await using GridReader gridReader = await _connection.QueryMultipleAsync(
                @"SELECT COUNT(*) FROM mapping_test WHERE name = @name;
                  SELECT value AS Value FROM mapping_test WHERE name = @name",
                new { name = "grid_param" },
                _transaction);

            long count = await gridReader.ReadSingleAsync<long>();
            Assert.Equal(1, count);

            IEnumerable<MappingTestEntity> rows = await gridReader.ReadAsync<MappingTestEntity>();
            Assert.Equal(99, rows.Single().Value);
        }

        [Fact]
        public async Task GridReader_ReadAsync_ScalarInt_ReadsColumnZeroOfEachRow()
        {
            await InsertRowAsync("grid_scalar_a", 1);
            await InsertRowAsync("grid_scalar_b", 2);

            await using GridReader gridReader = await _connection.QueryMultipleAsync(
                "SELECT value FROM mapping_test WHERE name LIKE 'grid_scalar_%' ORDER BY name",
                transaction: _transaction);

            Assert.Equal(new[] { 1, 2 }, await gridReader.ReadAsync<int>());
        }

        [Fact]
        public async Task GridReader_ReadAsync_ScalarString_ReadsColumnZeroOfEachRow()
        {
            await InsertRowAsync("grid_scalar_a", 1);
            await InsertRowAsync("grid_scalar_b", 2);

            await using GridReader gridReader = await _connection.QueryMultipleAsync(
                "SELECT name FROM mapping_test WHERE name LIKE 'grid_scalar_%' ORDER BY name",
                transaction: _transaction);

            Assert.Equal(new[] { "grid_scalar_a", "grid_scalar_b" }, await gridReader.ReadAsync<string>());
        }

        [Fact]
        public async Task GridReader_ReadAsync_ScalarIntFollowedByAnotherResultSet_ReadsBoth()
        {
            await InsertRowAsync("grid_scalar_a", 1);
            await InsertRowAsync("grid_scalar_b", 2);

            await using GridReader gridReader = await _connection.QueryMultipleAsync(
                @"SELECT value FROM mapping_test WHERE name LIKE 'grid_scalar_%' ORDER BY name;
                  SELECT name AS Name, value AS Value FROM mapping_test WHERE name LIKE 'grid_scalar_%' ORDER BY name",
                transaction: _transaction);

            Assert.Equal(new[] { 1, 2 }, await gridReader.ReadAsync<int>());

            List<MappingTestEntity> rows = (await gridReader.ReadAsync<MappingTestEntity>()).ToList();
            Assert.Equal(new[] { "grid_scalar_a", "grid_scalar_b" }, rows.Select(row => row.Name));
        }

        [Fact]
        public async Task GridReader_ReadAsync_ScalarNonNullableInt_NullColumn_Throws()
        {
            await InsertRowAsync("grid_scalar_null", null);

            await using GridReader gridReader = await _connection.QueryMultipleAsync(
                "SELECT value FROM mapping_test WHERE name = @name",
                new { name = "grid_scalar_null" },
                _transaction);

            await Assert.ThrowsAsync<InvalidCastException>(async () => await gridReader.ReadAsync<int>());
        }

        [Fact]
        public async Task GridReader_ReadSingleAsync_DateOnly_ReadsColumnZero()
        {
            await using GridReader gridReader = await _connection.QueryMultipleAsync(
                "SELECT DATE '1990-05-15'",
                transaction: _transaction);

            Assert.Equal(new DateOnly(1990, 5, 15), await gridReader.ReadSingleAsync<DateOnly>());
        }

        [Fact]
        public async Task GridReader_ReadSingleAsync_TimeOnly_ReadsColumnZero()
        {
            await using GridReader gridReader = await _connection.QueryMultipleAsync(
                "SELECT TIME '09:30'",
                transaction: _transaction);

            Assert.Equal(new TimeOnly(9, 30), await gridReader.ReadSingleAsync<TimeOnly>());
        }

        [Fact]
        public async Task GridReader_ReadSingleAsync_ScalarInt_NoRows_Throws()
        {
            await using GridReader gridReader = await _connection.QueryMultipleAsync(
                "SELECT 1 WHERE FALSE",
                transaction: _transaction);

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await gridReader.ReadSingleAsync<int>());
        }

        [Fact]
        public async Task GridReader_ReadSingleAsync_ScalarInt_MultipleRows_Throws()
        {
            await using GridReader gridReader = await _connection.QueryMultipleAsync(
                "SELECT n FROM (VALUES (1), (2)) AS v(n)",
                transaction: _transaction);

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await gridReader.ReadSingleAsync<int>());
        }

        [Fact]
        public async Task GridReader_ReadSingleAsync_NoRows_Throws()
        {
            await using GridReader gridReader = await _connection.QueryMultipleAsync(
                "SELECT name AS Name FROM mapping_test WHERE name = 'nonexistent'",
                transaction: _transaction);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await gridReader.ReadSingleAsync<MappingTestEntity>());
        }

        private async Task InsertRowAsync(string name, int? value)
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name, value },
                _transaction);
        }
    }
}
