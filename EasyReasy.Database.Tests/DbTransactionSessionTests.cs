using EasyReasy.Database.Tests.TestDoubles;
using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace EasyReasy.Database.Tests
{
    public class DbTransactionSessionTests
    {
        [Fact]
        public async Task Constructor_WhenConnectionIsNull_ThrowsArgumentNullException()
        {
            await using (DbConnection connection = new SqliteConnection("Data Source=:memory:"))
            {
                await connection.OpenAsync();
                DbTransaction transaction = await connection.BeginTransactionAsync();

                ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => new DbTransactionSession(null!, transaction));

                Assert.Equal("connection", exception.ParamName);
            }
        }

        [Fact]
        public async Task Constructor_WhenTransactionIsNull_ThrowsArgumentNullException()
        {
            await using (DbConnection connection = new SqliteConnection("Data Source=:memory:"))
            {
                ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() => new DbTransactionSession(connection, null!));

                Assert.Equal("transaction", exception.ParamName);
            }
        }

        [Fact]
        public async Task CommitAsync_WhenCalled_PersistsChanges()
        {
            await using (DbConnection connection = new SqliteConnection("Data Source=:memory:"))
            {
                await connection.OpenAsync();
                await ExecuteAsync(connection, null, "CREATE TABLE test (id INTEGER)");
                DbTransactionSession session = await BeginSessionAsync(connection);
                await ExecuteAsync(connection, session.Transaction, "INSERT INTO test VALUES (1)");

                await session.CommitAsync();

                Assert.Equal(1L, await CountRowsAsync(connection));
            }
        }

        [Fact]
        public async Task RollbackAsync_WhenCalled_DiscardsChanges()
        {
            await using (DbConnection connection = new SqliteConnection("Data Source=:memory:"))
            {
                await connection.OpenAsync();
                await ExecuteAsync(connection, null, "CREATE TABLE test (id INTEGER)");
                DbTransactionSession session = await BeginSessionAsync(connection);
                await ExecuteAsync(connection, session.Transaction, "INSERT INTO test VALUES (1)");

                await session.RollbackAsync();

                Assert.Equal(0L, await CountRowsAsync(connection));
            }
        }

        [Fact]
        public async Task DisposeAsync_WhenNotCommitted_DiscardsChanges()
        {
            // A second connection keeps the shared in-memory database alive after the session closes its own
            string connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            await using (DbConnection observer = new SqliteConnection(connectionString))
            {
                await observer.OpenAsync();
                await ExecuteAsync(observer, null, "CREATE TABLE test (id INTEGER)");
                DbConnection connection = new SqliteConnection(connectionString);
                await connection.OpenAsync();
                DbTransactionSession session = await BeginSessionAsync(connection);
                await ExecuteAsync(connection, session.Transaction, "INSERT INTO test VALUES (1)");

                await session.DisposeAsync();

                Assert.Equal(0L, await CountRowsAsync(observer));
            }
        }

        [Fact]
        public async Task DisposeAsync_WhenCalled_ClosesConnection()
        {
            DbConnection connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            DbTransactionSession session = await BeginSessionAsync(connection);

            await session.DisposeAsync();

            Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        }

        [Fact]
        public async Task DisposeAsync_WhenTransactionDisposeThrows_StillDisposesConnection()
        {
            TrackingDbConnection connection = new TrackingDbConnection();
            DbTransactionSession session = new DbTransactionSession(connection, new DisposeFailingDbTransaction());

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.DisposeAsync());

            Assert.True(connection.WasDisposed);
        }

        [Fact]
        public async Task DisposeAsync_WhenCalledMultipleTimes_DoesNotThrow()
        {
            DbConnection connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            DbTransactionSession session = await BeginSessionAsync(connection);

            await session.DisposeAsync();
            await session.DisposeAsync();
        }

        [Fact]
        public async Task Connection_WhenSet_ReturnsCorrectConnection()
        {
            await using (DbConnection connection = new SqliteConnection("Data Source=:memory:"))
            {
                await connection.OpenAsync();
                DbTransactionSession session = await BeginSessionAsync(connection);

                Assert.Same(connection, session.Connection);
            }
        }

        [Fact]
        public async Task Transaction_WhenReadAsEitherInterface_ReturnsSameTransaction()
        {
            await using (DbConnection connection = new SqliteConnection("Data Source=:memory:"))
            {
                await connection.OpenAsync();
                DbTransaction transaction = await connection.BeginTransactionAsync();
                DbTransactionSession session = new DbTransactionSession(connection, transaction);

                Assert.Same(transaction, ((IDbTransactionSession)session).Transaction);
                Assert.Same(transaction, ((IDbSession)session).Transaction);
            }
        }

        private static async Task<DbTransactionSession> BeginSessionAsync(DbConnection openConnection)
        {
            DbTransaction transaction = await openConnection.BeginTransactionAsync();
            return new DbTransactionSession(openConnection, transaction);
        }

        private static async Task ExecuteAsync(DbConnection connection, DbTransaction? transaction, string sql)
        {
            await using (DbCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync();
            }
        }

        private static async Task<long> CountRowsAsync(DbConnection connection)
        {
            await using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM test";
                return (long)(await command.ExecuteScalarAsync())!;
            }
        }
    }
}
