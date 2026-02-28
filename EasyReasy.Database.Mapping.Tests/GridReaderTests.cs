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
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "grid_3_a", value = 1 },
                _transaction);

            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "grid_3_b", value = 2 },
                _transaction);

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
            await _connection.ExecuteAsync(
                "INSERT INTO mapping_test (name, value) VALUES (@name, @value)",
                new { name = "grid_param", value = 99 },
                _transaction);

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
        public async Task GridReader_ReadSingleAsync_NoRows_Throws()
        {
            await using GridReader gridReader = await _connection.QueryMultipleAsync(
                "SELECT name AS Name FROM mapping_test WHERE name = 'nonexistent'",
                transaction: _transaction);

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await gridReader.ReadSingleAsync<MappingTestEntity>());
        }
    }
}
