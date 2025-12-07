using System.Data.Common;

namespace EasyReasy.Database.Tests
{
    public class RepositoryBaseTests
    {
        [Fact]
        public async Task UseSessionAsync_WhenSessionProvided_UsesProvidedSession()
        {
            SqliteDataSourceFactory factory = new SqliteDataSourceFactory();
            DbDataSource dataSource = factory.CreateDataSource("Data Source=:memory:");
            DbSessionFactory sessionFactory = new DbSessionFactory(dataSource);

            TestRepository repository = new TestRepository(dataSource, sessionFactory);

            await using (IDbSession providedSession = await sessionFactory.CreateSessionWithTransactionAsync())
            {
                DbConnection providedConnection = providedSession.Connection;

                int result = await repository.ExecuteQueryAsync("SELECT 1", providedSession);

                Assert.Same(providedConnection, providedSession.Connection);
                Assert.Equal(0, result);
            }
        }

        [Fact]
        public async Task UseSessionAsync_WhenNoSessionProvided_CreatesNewSession()
        {
            SqliteDataSourceFactory factory = new SqliteDataSourceFactory();
            DbDataSource dataSource = factory.CreateDataSource("Data Source=:memory:");
            DbSessionFactory sessionFactory = new DbSessionFactory(dataSource);

            TestRepository repository = new TestRepository(dataSource, sessionFactory);

            int result = await repository.ExecuteQueryAsync("SELECT 1");

            Assert.Equal(0, result);
        }

        [Fact]
        public async Task UseSessionAsync_WhenNoSessionProvided_DisposesSessionAfterUse()
        {
            SqliteDataSourceFactory factory = new SqliteDataSourceFactory();
            DbDataSource dataSource = factory.CreateDataSource("Data Source=:memory:");
            DbSessionFactory sessionFactory = new DbSessionFactory(dataSource);

            TestRepository repository = new TestRepository(dataSource, sessionFactory);

            await repository.ExecuteQueryAsync("CREATE TABLE test (id INTEGER)");

            int result = await repository.ExecuteQueryAsync("SELECT COUNT(*) FROM test");

            Assert.Equal(0, result);
        }

        [Fact]
        public async Task UseSessionAsync_WhenSessionProvided_DoesNotDisposeProvidedSession()
        {
            SqliteDataSourceFactory factory = new SqliteDataSourceFactory();
            DbDataSource dataSource = factory.CreateDataSource("Data Source=:memory:");
            DbSessionFactory sessionFactory = new DbSessionFactory(dataSource);

            TestRepository repository = new TestRepository(dataSource, sessionFactory);

            await using (IDbSession providedSession = await sessionFactory.CreateSessionWithTransactionAsync())
            {
                await repository.ExecuteQueryAsync("CREATE TABLE test (id INTEGER)", providedSession);
                await repository.ExecuteQueryAsync("INSERT INTO test VALUES (1)", providedSession);

                string? count = await repository.ExecuteScalarAsync("SELECT COUNT(*) FROM test", providedSession);

                Assert.Equal("1", count);
            }
        }

        [Fact]
        public async Task UseSessionAsync_VoidVersion_WhenSessionProvided_UsesProvidedSession()
        {
            SqliteDataSourceFactory factory = new SqliteDataSourceFactory();
            DbDataSource dataSource = factory.CreateDataSource("Data Source=:memory:");
            DbSessionFactory sessionFactory = new DbSessionFactory(dataSource);

            TestRepository repository = new TestRepository(dataSource, sessionFactory);

            await using (IDbSession providedSession = await sessionFactory.CreateSessionWithTransactionAsync())
            {
                bool actionExecuted = false;

                await repository.UseSessionActionAsync(async (session) =>
                {
                    Assert.Same(providedSession, session);
                    actionExecuted = true;
                    await Task.CompletedTask;
                }, providedSession);

                Assert.True(actionExecuted);
            }
        }

        [Fact]
        public async Task UseSessionAsync_VoidVersion_WhenNoSessionProvided_CreatesNewSession()
        {
            SqliteDataSourceFactory factory = new SqliteDataSourceFactory();
            DbDataSource dataSource = factory.CreateDataSource("Data Source=:memory:");
            DbSessionFactory sessionFactory = new DbSessionFactory(dataSource);

            TestRepository repository = new TestRepository(dataSource, sessionFactory);

            bool actionExecuted = false;

            await repository.UseSessionActionAsync(async (session) =>
            {
                Assert.NotNull(session);
                actionExecuted = true;
                await Task.CompletedTask;
            });

            Assert.True(actionExecuted);
        }

        [Fact]
        public async Task CreateSessionWithTransactionAsync_WhenCalled_DelegatesToSessionFactory()
        {
            SqliteDataSourceFactory factory = new SqliteDataSourceFactory();
            DbDataSource dataSource = factory.CreateDataSource("Data Source=:memory:");
            DbSessionFactory sessionFactory = new DbSessionFactory(dataSource);

            TestRepository repository = new TestRepository(dataSource, sessionFactory);

            await using (IDbSession session = await repository.CreateSessionWithTransactionAsync())
            {
                Assert.NotNull(session.Connection);
                Assert.NotNull(session.Transaction);
            }
        }

        [Fact]
        public void DataSource_WhenSet_ReturnsCorrectDataSource()
        {
            SqliteDataSourceFactory factory = new SqliteDataSourceFactory();
            DbDataSource dataSource = factory.CreateDataSource("Data Source=:memory:");
            DbSessionFactory sessionFactory = new DbSessionFactory(dataSource);

            TestRepository repository = new TestRepository(dataSource, sessionFactory);

            Assert.Same(dataSource, repository.DataSource);
        }
    }
}

