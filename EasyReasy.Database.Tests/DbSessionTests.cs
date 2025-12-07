using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace EasyReasy.Database.Tests
{
    public class DbSessionTests
    {
        [Fact]
        public void Constructor_WhenConnectionIsNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new DbSession(null!, null));
        }

        [Fact]
        public async Task CommitAsync_WhenNoTransaction_ThrowsInvalidOperationException()
        {
            await using (DbConnection connection = new SqliteConnection("Data Source=:memory:"))
            {
                await connection.OpenAsync();
                DbSession session = new DbSession(connection, null);

                await Assert.ThrowsAsync<InvalidOperationException>(() => session.CommitAsync());
            }
        }

        [Fact]
        public async Task CommitAsync_WhenTransactionExists_CommitsSuccessfully()
        {
            await using (DbConnection connection = new SqliteConnection("Data Source=:memory:"))
            {
                await connection.OpenAsync();
                DbTransaction transaction = await connection.BeginTransactionAsync();
                DbSession session = new DbSession(connection, transaction);

                await session.CommitAsync();

                Assert.NotNull(session.Transaction);
            }
        }

        [Fact]
        public async Task RollbackAsync_WhenNoTransaction_ThrowsInvalidOperationException()
        {
            await using (DbConnection connection = new SqliteConnection("Data Source=:memory:"))
            {
                await connection.OpenAsync();
                DbSession session = new DbSession(connection, null);

                await Assert.ThrowsAsync<InvalidOperationException>(() => session.RollbackAsync());
            }
        }

        [Fact]
        public async Task RollbackAsync_WhenTransactionExists_RollsBackSuccessfully()
        {
            await using (DbConnection connection = new SqliteConnection("Data Source=:memory:"))
            {
                await connection.OpenAsync();
                DbTransaction transaction = await connection.BeginTransactionAsync();
                DbSession session = new DbSession(connection, transaction);

                await session.RollbackAsync();

                Assert.NotNull(session.Transaction);
            }
        }

        [Fact]
        public async Task DisposeAsync_WhenCalled_DisposesConnectionAndTransaction()
        {
            DbConnection connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            DbTransaction transaction = await connection.BeginTransactionAsync();
            DbSession session = new DbSession(connection, transaction);

            await session.DisposeAsync();

            Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        }

        [Fact]
        public async Task DisposeAsync_WhenCalledMultipleTimes_IsIdempotent()
        {
            DbConnection connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            DbTransaction transaction = await connection.BeginTransactionAsync();
            DbSession session = new DbSession(connection, transaction);

            await session.DisposeAsync();
            await session.DisposeAsync();

            Assert.True(true);
        }

        [Fact]
        public void Connection_WhenSet_ReturnsCorrectConnection()
        {
            using (DbConnection connection = new SqliteConnection("Data Source=:memory:"))
            {
                DbSession session = new DbSession(connection, null);

                Assert.Same(connection, session.Connection);
            }
        }

        [Fact]
        public void Transaction_WhenSet_ReturnsCorrectTransaction()
        {
            using (DbConnection connection = new SqliteConnection("Data Source=:memory:"))
            {
                connection.Open();
                DbTransaction transaction = connection.BeginTransaction();
                DbSession session = new DbSession(connection, transaction);

                Assert.Same(transaction, session.Transaction);
            }
        }

        [Fact]
        public void Transaction_WhenNotSet_ReturnsNull()
        {
            using (DbConnection connection = new SqliteConnection("Data Source=:memory:"))
            {
                DbSession session = new DbSession(connection, null);

                Assert.Null(session.Transaction);
            }
        }
    }
}

