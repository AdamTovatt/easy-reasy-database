using System.Data.Common;

namespace EasyReasy.Database.Mapping.Npgsql.Tests
{
    [Collection("Database")]
    public class NpgsqlDbNameEnumHandlerTests : IAsyncLifetime
    {
        private readonly TestDatabaseFixture _fixture;
        private DbConnection _connection = null!;
        private DbTransaction _transaction = null!;

        public NpgsqlDbNameEnumHandlerTests(TestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        public async Task InitializeAsync()
        {
            TypeHandlerRegistry.AddTypeHandler(new NpgsqlDbNameEnumHandler<TestStatus>("npgsql_mapping_test_status"));

            _connection = await _fixture.DataSource.OpenConnectionAsync();
            _transaction = await _connection.BeginTransactionAsync();
        }

        public async Task DisposeAsync()
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
            TypeHandlerRegistry.Clear();
        }

        [Fact]
        public async Task WriteAndRead_WithoutCast_RoundTrips()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO npgsql_mapping_test (name, status) VALUES (@name, @status)",
                new { name = "no_cast_test", status = TestStatus.Active },
                _transaction);

            MappingTestEntity? result = await _connection.QuerySingleOrDefaultAsync<MappingTestEntity>(
                "SELECT name AS Name, status AS Status FROM npgsql_mapping_test WHERE name = @name",
                new { name = "no_cast_test" },
                _transaction);

            Assert.NotNull(result);
            Assert.Equal(TestStatus.Active, result.Status);
        }

        [Fact]
        public async Task WriteAllValues_WithoutCast_RoundTrips()
        {
            TestStatus[] allStatuses = new[] { TestStatus.Active, TestStatus.Inactive, TestStatus.Pending };

            foreach (TestStatus status in allStatuses)
            {
                string name = $"all_values_{status}";
                await _connection.ExecuteAsync(
                    "INSERT INTO npgsql_mapping_test (name, status) VALUES (@name, @status)",
                    new { name, status },
                    _transaction);
            }

            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                "SELECT name AS Name, status AS Status FROM npgsql_mapping_test WHERE name LIKE 'all_values_%' ORDER BY name",
                transaction: _transaction);

            List<MappingTestEntity> list = results.ToList();
            Assert.Equal(3, list.Count);
            Assert.Equal(TestStatus.Active, list[0].Status);
            Assert.Equal(TestStatus.Inactive, list[1].Status);
            Assert.Equal(TestStatus.Pending, list[2].Status);
        }

        [Fact]
        public async Task FilterByEnumParameter_WithoutCast_Works()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO npgsql_mapping_test (name, status) VALUES (@name, @status)",
                new { name = "filter_active", status = TestStatus.Active },
                _transaction);

            await _connection.ExecuteAsync(
                "INSERT INTO npgsql_mapping_test (name, status) VALUES (@name, @status)",
                new { name = "filter_inactive", status = TestStatus.Inactive },
                _transaction);

            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                "SELECT name AS Name, status AS Status FROM npgsql_mapping_test WHERE status = @status AND name LIKE 'filter_%'",
                new { status = TestStatus.Active },
                _transaction);

            List<MappingTestEntity> list = results.ToList();
            Assert.Single(list);
            Assert.Equal("filter_active", list[0].Name);
        }
    }
}
