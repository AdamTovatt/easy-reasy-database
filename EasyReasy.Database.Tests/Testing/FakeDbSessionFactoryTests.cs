using EasyReasy.Database.Testing;

namespace EasyReasy.Database.Tests.Testing
{
    public class FakeDbSessionFactoryTests
    {
        [Fact]
        public async Task CreateSessionAsync_WhenCalled_ReturnsSession()
        {
            FakeDbSessionFactory factory = new FakeDbSessionFactory();

            IDbSession session = await factory.CreateSessionAsync();

            Assert.NotNull(session);
            Assert.IsType<FakeDbSession>(session);
        }

        [Fact]
        public async Task CreateSessionAsync_WhenCalled_IncrementsCreateSessionCallCount()
        {
            FakeDbSessionFactory factory = new FakeDbSessionFactory();

            await factory.CreateSessionAsync();
            await factory.CreateSessionAsync();

            Assert.Equal(2, factory.CreateSessionCallCount);
        }

        [Fact]
        public async Task CreateSessionWithTransactionAsync_WhenCalled_ReturnsSession()
        {
            FakeDbSessionFactory factory = new FakeDbSessionFactory();

            IDbSession session = await factory.CreateSessionWithTransactionAsync();

            Assert.NotNull(session);
            Assert.IsType<FakeDbSession>(session);
        }

        [Fact]
        public async Task CreateSessionWithTransactionAsync_WhenCalled_IncrementsCreateSessionWithTransactionCallCount()
        {
            FakeDbSessionFactory factory = new FakeDbSessionFactory();

            await factory.CreateSessionWithTransactionAsync();
            await factory.CreateSessionWithTransactionAsync();

            Assert.Equal(2, factory.CreateSessionWithTransactionCallCount);
        }

        [Fact]
        public async Task CreateSessionAsync_WhenCalledMultipleTimes_ReturnsSameSession()
        {
            FakeDbSessionFactory factory = new FakeDbSessionFactory();

            IDbSession session1 = await factory.CreateSessionAsync();
            IDbSession session2 = await factory.CreateSessionAsync();

            Assert.Same(session1, session2);
        }

        [Fact]
        public async Task CreateSessionWithTransactionAsync_WhenCalledMultipleTimes_ReturnsSameSession()
        {
            FakeDbSessionFactory factory = new FakeDbSessionFactory();

            IDbSession session1 = await factory.CreateSessionWithTransactionAsync();
            IDbSession session2 = await factory.CreateSessionWithTransactionAsync();

            Assert.Same(session1, session2);
        }

        [Fact]
        public async Task CreateSessionAsync_AndCreateSessionWithTransactionAsync_ReturnSameSession()
        {
            FakeDbSessionFactory factory = new FakeDbSessionFactory();

            IDbSession session1 = await factory.CreateSessionAsync();
            IDbSession session2 = await factory.CreateSessionWithTransactionAsync();

            Assert.Same(session1, session2);
        }

        [Fact]
        public void Constructor_WhenCustomSessionProvided_UsesCustomSession()
        {
            FakeDbSession customSession = new FakeDbSession();
            FakeDbSessionFactory factory = new FakeDbSessionFactory(customSession);

            Assert.Same(customSession, factory.Session);
        }

        [Fact]
        public void Reset_WhenCalled_ResetsCallCounts()
        {
            FakeDbSessionFactory factory = new FakeDbSessionFactory();

            factory.CreateSessionAsync();
            factory.CreateSessionWithTransactionAsync();

            factory.Reset();

            Assert.Equal(0, factory.CreateSessionCallCount);
            Assert.Equal(0, factory.CreateSessionWithTransactionCallCount);
        }

        [Fact]
        public void Reset_WhenCalled_ResetsSessionState()
        {
            FakeDbSessionFactory factory = new FakeDbSessionFactory();

            factory.Session.CommitAsync();
            factory.Session.RollbackAsync();
            factory.Session.DisposeAsync();

            factory.Reset();

            Assert.False(factory.Session.WasCommitted);
            Assert.False(factory.Session.WasRolledBack);
            Assert.False(factory.Session.WasDisposed);
        }

        [Fact]
        public void CreateSessionCallCount_WhenNotCalled_IsZero()
        {
            FakeDbSessionFactory factory = new FakeDbSessionFactory();

            Assert.Equal(0, factory.CreateSessionCallCount);
        }

        [Fact]
        public void CreateSessionWithTransactionCallCount_WhenNotCalled_IsZero()
        {
            FakeDbSessionFactory factory = new FakeDbSessionFactory();

            Assert.Equal(0, factory.CreateSessionWithTransactionCallCount);
        }
    }
}

