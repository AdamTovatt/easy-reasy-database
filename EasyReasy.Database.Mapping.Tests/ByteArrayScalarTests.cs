using System.Data.Common;

namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// Reading a single <c>bytea</c> column as <c>byte[]</c>: the scalar path reads column 0, as it does for
    /// <c>string</c>, rather than treating <c>byte[]</c> as an entity type to map properties onto.
    /// <para>
    /// The values live in a temporary table created inside each test's transaction rather than in a
    /// <c>bytea</c> column on <c>mapping_test</c>: the fixture creates that table with
    /// <c>CREATE TABLE IF NOT EXISTS</c>, so a table left behind by a killed run would never gain a new
    /// column, while a temporary table is rebuilt by every test and dropped with its transaction.
    /// </para>
    /// </summary>
    [Collection("Database")]
    public class ByteArrayScalarTests : IAsyncLifetime
    {
        private static readonly byte[] SmallestValue = [0x01];
        private static readonly byte[] MiddleValue = [0x02, 0x05];
        private static readonly byte[] LongValue = [0x02, 0x06, 0x07];
        private static readonly byte[] LargestValue = [0x03];

        private readonly TestDatabaseFixture _fixture;
        private DbConnection _connection = null!;
        private DbTransaction _transaction = null!;

        public ByteArrayScalarTests(TestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        public async Task InitializeAsync()
        {
            _connection = await _fixture.DataSource.OpenConnectionAsync();
            _transaction = await _connection.BeginTransactionAsync();

            await _connection.ExecuteAsync(
                "CREATE TEMPORARY TABLE scratch (value BYTEA) ON COMMIT DROP",
                transaction: _transaction);
        }

        public async Task DisposeAsync()
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();
        }

        [Fact]
        public async Task QueryAsync_ScalarByteArray_ReturnsColumnZeroOfEachRow()
        {
            await InsertAsync(LargestValue, SmallestValue);

            IEnumerable<byte[]> values = await _connection.QueryAsync<byte[]>(
                "SELECT value FROM scratch ORDER BY value",
                transaction: _transaction);

            Assert.Equal(new[] { SmallestValue, LargestValue }, values);
        }

        [Fact]
        public async Task QueryAsync_ScalarByteArray_WithByteArrayAndIntParameters_ReturnsMatchingRows()
        {
            await InsertAsync(SmallestValue, MiddleValue, LongValue, LargestValue);

            IEnumerable<byte[]> values = await _connection.QueryAsync<byte[]>(
                @"SELECT value
                  FROM scratch
                  WHERE value > @after AND length(value) <= @maxLength
                  ORDER BY value
                  LIMIT @limit",
                new { after = SmallestValue, maxLength = 2, limit = 100 },
                _transaction);

            Assert.Equal(new[] { MiddleValue, LargestValue }, values);
        }

        [Fact]
        public async Task QueryAsync_ScalarByteArray_NullColumn_ReturnsNull()
        {
            await InsertAsync(SmallestValue);
            await InsertNullAsync();

            IEnumerable<byte[]?> values = await _connection.QueryAsync<byte[]?>(
                "SELECT value FROM scratch ORDER BY value NULLS LAST",
                transaction: _transaction);

            Assert.Equal(new[] { SmallestValue, null }, values);
        }

        [Fact]
        public async Task QueryAsync_ScalarByteArray_ZeroLengthValue_ReturnsEmptyArray()
        {
            await InsertAsync(Array.Empty<byte>());

            IEnumerable<byte[]> values = await _connection.QueryAsync<byte[]>(
                "SELECT value FROM scratch",
                transaction: _transaction);

            byte[] value = Assert.Single(values);
            Assert.NotNull(value);
            Assert.Empty(value);
        }

        [Fact]
        public async Task QuerySingleAsync_ScalarByteArray_WithParameter_ReturnsColumnZero()
        {
            await InsertAsync(SmallestValue, LargestValue);

            byte[] value = await _connection.QuerySingleAsync<byte[]>(
                "SELECT value FROM scratch WHERE value > @after",
                new { after = SmallestValue },
                _transaction);

            Assert.Equal(LargestValue, value);
        }

        [Fact]
        public async Task QuerySingleAsync_ScalarByteArray_OneRowWithNullColumn_ReturnsNull()
        {
            await InsertNullAsync();

            byte[]? value = await _connection.QuerySingleAsync<byte[]?>(
                "SELECT value FROM scratch",
                transaction: _transaction);

            Assert.Null(value);
        }

        [Fact]
        public async Task QuerySingleOrDefaultAsync_ScalarByteArray_ReturnsColumnZero()
        {
            await InsertAsync(LargestValue);

            byte[]? value = await _connection.QuerySingleOrDefaultAsync<byte[]>(
                "SELECT value FROM scratch",
                transaction: _transaction);

            Assert.Equal(LargestValue, value);
        }

        [Fact]
        public async Task QueryFirstOrDefaultAsync_ScalarByteArray_WithParameter_ReturnsColumnZeroOfTheFirstRow()
        {
            await InsertAsync(SmallestValue, MiddleValue, LargestValue);

            byte[]? value = await _connection.QueryFirstOrDefaultAsync<byte[]>(
                "SELECT value FROM scratch WHERE value > @after ORDER BY value",
                new { after = SmallestValue },
                _transaction);

            Assert.Equal(MiddleValue, value);
        }

        [Fact]
        public async Task QueryFirstOrDefaultAsync_ScalarByteArray_NoRows_ReturnsNull()
        {
            byte[]? value = await _connection.QueryFirstOrDefaultAsync<byte[]>(
                "SELECT value FROM scratch",
                transaction: _transaction);

            Assert.Null(value);
        }

        [Fact]
        public async Task GridReader_ReadSingleAsync_ScalarByteArray_WithParameter_ReturnsColumnZero()
        {
            await InsertAsync(SmallestValue, LargestValue);

            await using GridReader gridReader = await _connection.QueryMultipleAsync(
                @"SELECT value FROM scratch WHERE value > @after;
                  SELECT value FROM scratch WHERE value < @after",
                new { after = MiddleValue },
                _transaction);

            Assert.Equal(LargestValue, await gridReader.ReadSingleAsync<byte[]>());
            Assert.Equal(SmallestValue, await gridReader.ReadSingleAsync<byte[]>());
        }

        [Fact]
        public async Task GridReader_ReadAsync_ScalarByteArray_ReturnsColumnZeroOfEachRow()
        {
            await InsertAsync(LargestValue, SmallestValue);

            await using GridReader gridReader = await _connection.QueryMultipleAsync(
                "SELECT value FROM scratch ORDER BY value",
                transaction: _transaction);

            Assert.Equal(new[] { SmallestValue, LargestValue }, await gridReader.ReadAsync<byte[]>());
        }

        [Fact]
        public async Task GridReader_ReadSingleAsync_ScalarByteArray_OneRowWithNullColumn_ReturnsNull()
        {
            await InsertNullAsync();

            await using GridReader gridReader = await _connection.QueryMultipleAsync(
                "SELECT value FROM scratch",
                transaction: _transaction);

            Assert.Null(await gridReader.ReadSingleAsync<byte[]?>());
        }

        private async Task InsertAsync(params byte[][] values)
        {
            foreach (byte[] value in values)
            {
                await _connection.ExecuteAsync(
                    "INSERT INTO scratch (value) VALUES (@value)",
                    new { value },
                    _transaction);
            }
        }

        private async Task InsertNullAsync()
        {
            await _connection.ExecuteAsync("INSERT INTO scratch (value) VALUES (NULL)", transaction: _transaction);
        }
    }
}
