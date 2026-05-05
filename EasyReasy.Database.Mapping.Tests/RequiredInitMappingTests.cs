using System.Data.Common;

namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// Pin whether the mapper can hydrate a type with `required init` properties
    /// directly, eliminating the need for an intermediate mutable Row/Entity class.
    /// </summary>
    [Collection("Database")]
    public class RequiredInitMappingTests : IAsyncLifetime
    {
        private readonly TestDatabaseFixture _fixture;
        private DbConnection _connection = null!;
        private DbTransaction _transaction = null!;

        public RequiredInitMappingTests(TestDatabaseFixture fixture)
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
        public async Task QuerySingleAsync_RequiredInitEntity_MapsCorrectly()
        {
            RequiredInitMappingTestEntity result = await _connection.QuerySingleAsync<RequiredInitMappingTestEntity>(
                @"INSERT INTO mapping_test (name, value)
                  VALUES (@name, @value)
                  RETURNING id AS Id, name AS Name, value AS Value",
                new { name = "required_init_test", value = 42 },
                _transaction);

            Assert.NotEqual(Guid.Empty, result.Id);
            Assert.Equal("required_init_test", result.Name);
            Assert.Equal(42, result.Value);
        }
    }
}
