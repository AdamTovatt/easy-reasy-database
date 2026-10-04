using EasyReasy.Database.Sqlite;
using EasyReasy.Database.Testing;

namespace EasyReasy.Database.Tests.Testing
{
    public class TestDatabaseManagerTests
    {
        [Fact]
        public async Task CreateTransactionSessionAsync_WhenReadAsEitherInterface_ReturnsSameTransactionOnSessionConnection()
        {
            TestDatabaseManager manager = new TestDatabaseManager(new SqliteDataSourceFactory(), () => "Data Source=:memory:");

            await using (IDbTransactionSession session = await manager.CreateTransactionSessionAsync())
            {
                IDbSession plainSession = session;

                Assert.NotNull(plainSession.Transaction);
                Assert.Same(session.Transaction, plainSession.Transaction);
                Assert.Same(session.Connection, session.Transaction.Connection);
            }
        }
    }
}
