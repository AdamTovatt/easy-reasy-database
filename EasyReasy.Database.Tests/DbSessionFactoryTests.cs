using EasyReasy.Database.Sqlite;
using EasyReasy.Database.Tests.TestDoubles;
using System.Data.Common;

namespace EasyReasy.Database.Tests
{
    public class DbSessionFactoryTests
    {
        [Fact]
        public async Task CreateSessionAsync_WhenCalled_CreatesSessionWithoutTransaction()
        {
            SqliteDataSourceFactory factory = new SqliteDataSourceFactory();
            DbDataSource dataSource = factory.CreateDataSource("Data Source=:memory:");

            DbSessionFactory sessionFactory = new DbSessionFactory(dataSource);

            await using (IDbSession session = await sessionFactory.CreateSessionAsync())
            {
                Assert.NotNull(session.Connection);
                Assert.Null(session.Transaction);
            }
        }

        [Fact]
        public async Task CreateSessionWithTransactionAsync_WhenCalled_CreatesSessionWithTransaction()
        {
            SqliteDataSourceFactory factory = new SqliteDataSourceFactory();
            DbDataSource dataSource = factory.CreateDataSource("Data Source=:memory:");

            DbSessionFactory sessionFactory = new DbSessionFactory(dataSource);

            await using (IDbTransactionSession session = await sessionFactory.CreateSessionWithTransactionAsync())
            {
                Assert.NotNull(session.Connection);
                Assert.Same(session.Connection, session.Transaction.Connection);
            }
        }

        [Fact]
        public async Task CreateSessionWithTransactionAsync_WhenBeginTransactionFails_DisposesConnection()
        {
            TrackingDbConnection connection = new TrackingDbConnection();
            DbSessionFactory sessionFactory = new DbSessionFactory(new SingleConnectionDataSource(connection));

            await Assert.ThrowsAsync<InvalidOperationException>(() => sessionFactory.CreateSessionWithTransactionAsync());

            Assert.True(connection.WasDisposed);
        }

        [Fact]
        public async Task CreateSessionWithTransactionAsync_WhenReadAsEitherInterface_ReturnsSameTransaction()
        {
            SqliteDataSourceFactory factory = new SqliteDataSourceFactory();
            DbDataSource dataSource = factory.CreateDataSource("Data Source=:memory:");

            DbSessionFactory sessionFactory = new DbSessionFactory(dataSource);

            await using (IDbTransactionSession session = await sessionFactory.CreateSessionWithTransactionAsync())
            {
                IDbSession plainSession = session;

                Assert.NotNull(plainSession.Transaction);
                Assert.Same(session.Transaction, plainSession.Transaction);
            }
        }

        [Fact]
        public async Task CreateSessionAsync_WhenDisposed_ConnectionIsClosed()
        {
            SqliteDataSourceFactory factory = new SqliteDataSourceFactory();
            DbDataSource dataSource = factory.CreateDataSource("Data Source=:memory:");

            DbSessionFactory sessionFactory = new DbSessionFactory(dataSource);

            IDbSession session = await sessionFactory.CreateSessionAsync();
            DbConnection connection = session.Connection;

            await session.DisposeAsync();

            Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        }

        [Fact]
        public async Task CreateSessionWithTransactionAsync_WhenDisposed_ConnectionIsClosed()
        {
            SqliteDataSourceFactory factory = new SqliteDataSourceFactory();
            DbDataSource dataSource = factory.CreateDataSource("Data Source=:memory:");

            DbSessionFactory sessionFactory = new DbSessionFactory(dataSource);

            IDbTransactionSession session = await sessionFactory.CreateSessionWithTransactionAsync();
            DbConnection connection = session.Connection;

            await session.DisposeAsync();

            Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        }

        [Fact]
        public async Task CreateSessionAsync_WhenUsed_CanExecuteQueries()
        {
            SqliteDataSourceFactory factory = new SqliteDataSourceFactory();
            DbDataSource dataSource = factory.CreateDataSource("Data Source=:memory:");

            DbSessionFactory sessionFactory = new DbSessionFactory(dataSource);

            await using (IDbSession session = await sessionFactory.CreateSessionAsync())
            {
                DbCommand command = session.Connection.CreateCommand();
                command.CommandText = "CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT)";
                await command.ExecuteNonQueryAsync();

                command.CommandText = "INSERT INTO test (name) VALUES ('test')";
                int rowsAffected = await command.ExecuteNonQueryAsync();

                Assert.Equal(1, rowsAffected);
            }
        }

        [Fact]
        public async Task CreateSessionWithTransactionAsync_WhenUsed_CanExecuteQueriesInTransaction()
        {
            SqliteDataSourceFactory factory = new SqliteDataSourceFactory();
            DbDataSource dataSource = factory.CreateDataSource("Data Source=:memory:");

            DbSessionFactory sessionFactory = new DbSessionFactory(dataSource);

            await using (IDbTransactionSession session = await sessionFactory.CreateSessionWithTransactionAsync())
            {
                DbCommand command = session.Connection.CreateCommand();
                command.Transaction = session.Transaction;
                command.CommandText = "CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT)";
                await command.ExecuteNonQueryAsync();

                command.CommandText = "INSERT INTO test (name) VALUES ('test')";
                int rowsAffected = await command.ExecuteNonQueryAsync();

                Assert.Equal(1, rowsAffected);
            }
        }
    }
}

