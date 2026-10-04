using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace EasyReasy.Database.Tests
{
    public class DbSessionTests
    {
        [Fact]
        public void Constructor_WhenConnectionIsNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new DbSession(null!));
        }

        [Fact]
        public async Task CommitAsync_WhenCalled_ThrowsInvalidOperationException()
        {
            await using (DbConnection connection = new SqliteConnection("Data Source=:memory:"))
            {
                await connection.OpenAsync();
                DbSession session = new DbSession(connection);

                await Assert.ThrowsAsync<InvalidOperationException>(() => session.CommitAsync());
            }
        }

        [Fact]
        public async Task RollbackAsync_WhenCalled_ThrowsInvalidOperationException()
        {
            await using (DbConnection connection = new SqliteConnection("Data Source=:memory:"))
            {
                await connection.OpenAsync();
                DbSession session = new DbSession(connection);

                await Assert.ThrowsAsync<InvalidOperationException>(() => session.RollbackAsync());
            }
        }

        [Fact]
        public async Task DisposeAsync_WhenCalled_ClosesConnection()
        {
            DbConnection connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            DbSession session = new DbSession(connection);

            await session.DisposeAsync();

            Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        }

        [Fact]
        public async Task DisposeAsync_WhenCalledMultipleTimes_DoesNotThrow()
        {
            DbConnection connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            DbSession session = new DbSession(connection);

            await session.DisposeAsync();
            await session.DisposeAsync();
        }

        [Fact]
        public void Connection_WhenSet_ReturnsCorrectConnection()
        {
            using (DbConnection connection = new SqliteConnection("Data Source=:memory:"))
            {
                DbSession session = new DbSession(connection);

                Assert.Same(connection, session.Connection);
            }
        }

        [Fact]
        public void Transaction_WhenAccessed_ReturnsNull()
        {
            using (DbConnection connection = new SqliteConnection("Data Source=:memory:"))
            {
                IDbSession session = new DbSession(connection);

                Assert.Null(session.Transaction);
            }
        }
    }
}
