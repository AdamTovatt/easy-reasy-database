using System.Data.Common;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;
using EasyReasy.EnvironmentVariables;

namespace EasyReasy.Database.Mapping.Npgsql.Tests
{
    /// <summary>
    /// Tests for <see cref="NpgsqlDataSourceBuilderExtensions.MapDbNameEnum{T}"/>.
    /// Uses the database table and enum type created by <see cref="TestDatabaseFixture"/>,
    /// but creates its own data source via <c>MapDbNameEnum</c>.
    /// </summary>
    [Collection("Database")]
    public class MapDbNameEnumTests : IAsyncLifetime
    {
        private readonly TestDatabaseFixture _fixture;
        private DbDataSource _mappedDataSource = null!;
        private DbConnection _connection = null!;
        private DbTransaction _transaction = null!;

        public MapDbNameEnumTests(TestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        public async Task InitializeAsync()
        {
            string connectionString = EnvironmentVariables.DatabaseConnectionString.GetValue();

            NpgsqlDataSourceFactory factory = new NpgsqlDataSourceFactory(builder =>
            {
                builder.MapDbNameEnum<TestStatus>();
            });
            _mappedDataSource = factory.CreateDataSource(connectionString);

            _connection = await _mappedDataSource.OpenConnectionAsync();
            _transaction = await _connection.BeginTransactionAsync();
        }

        public async Task DisposeAsync()
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();

            if (_mappedDataSource is IDisposable disposable)
            {
                disposable.Dispose();
            }

            TypeHandlerRegistry.Clear();
        }

        [Fact]
        public async Task WriteAndRead_WithMapDbNameEnum_RoundTrips()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO npgsql_mapping_test (name, status) VALUES (@name, @status)",
                new { name = "map_round_trip", status = TestStatus.Active },
                _transaction);

            MappingTestEntity? result = await _connection.QuerySingleOrDefaultAsync<MappingTestEntity>(
                "SELECT name AS Name, status AS Status FROM npgsql_mapping_test WHERE name = @name",
                new { name = "map_round_trip" },
                _transaction);

            Assert.NotNull(result);
            Assert.Equal(TestStatus.Active, result.Status);
        }

        [Fact]
        public async Task AllValues_WithMapDbNameEnum_RoundTrip()
        {
            TestStatus[] allStatuses = [TestStatus.Active, TestStatus.Inactive, TestStatus.Pending];

            foreach (TestStatus status in allStatuses)
            {
                string name = $"map_all_{status}";
                await _connection.ExecuteAsync(
                    "INSERT INTO npgsql_mapping_test (name, status) VALUES (@name, @status)",
                    new { name, status },
                    _transaction);
            }

            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                "SELECT name AS Name, status AS Status FROM npgsql_mapping_test WHERE name LIKE 'map_all_%' ORDER BY name",
                transaction: _transaction);

            List<MappingTestEntity> list = results.ToList();
            Assert.Equal(3, list.Count);
            Assert.Equal(TestStatus.Active, list[0].Status);
            Assert.Equal(TestStatus.Inactive, list[1].Status);
            Assert.Equal(TestStatus.Pending, list[2].Status);
        }

        [Fact]
        public async Task FilterByEnum_WithMapDbNameEnum_Works()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO npgsql_mapping_test (name, status) VALUES (@name, @status)",
                new { name = "map_filter_active", status = TestStatus.Active },
                _transaction);

            await _connection.ExecuteAsync(
                "INSERT INTO npgsql_mapping_test (name, status) VALUES (@name, @status)",
                new { name = "map_filter_inactive", status = TestStatus.Inactive },
                _transaction);

            IEnumerable<MappingTestEntity> results = await _connection.QueryAsync<MappingTestEntity>(
                "SELECT name AS Name, status AS Status FROM npgsql_mapping_test WHERE status = @status AND name LIKE 'map_filter_%'",
                new { status = TestStatus.Active },
                _transaction);

            List<MappingTestEntity> list = results.ToList();
            Assert.Single(list);
            Assert.Equal("map_filter_active", list[0].Name);
        }

        [Fact]
        public void MapDbNameEnum_MissingDbEnumAttribute_ThrowsInvalidOperationException()
        {
            global::Npgsql.NpgsqlDataSourceBuilder builder = new global::Npgsql.NpgsqlDataSourceBuilder("Host=localhost");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            {
                builder.MapDbNameEnum<DayOfWeek>();
            });

            Assert.Contains("DayOfWeek", exception.Message);
            Assert.Contains("[DbEnum]", exception.Message);
        }
    }
}
