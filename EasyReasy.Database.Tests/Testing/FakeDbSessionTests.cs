using EasyReasy.Database.Testing;

namespace EasyReasy.Database.Tests.Testing
{
    public class FakeDbSessionTests
    {
        [Fact]
        public void Connection_WhenAccessed_ReturnsNull()
        {
            FakeDbSession session = new FakeDbSession();

            Assert.Null(session.Connection);
        }

        [Fact]
        public void Transaction_WhenAccessed_ReturnsNull()
        {
            FakeDbSession session = new FakeDbSession();

            Assert.Null(session.Transaction);
        }

        [Fact]
        public async Task CommitAsync_WhenCalled_SetsWasCommitted()
        {
            FakeDbSession session = new FakeDbSession();

            await session.CommitAsync();

            Assert.True(session.WasCommitted);
        }

        [Fact]
        public async Task CommitAsync_WhenCalledMultipleTimes_RemainsCommitted()
        {
            FakeDbSession session = new FakeDbSession();

            await session.CommitAsync();
            await session.CommitAsync();

            Assert.True(session.WasCommitted);
        }

        [Fact]
        public async Task RollbackAsync_WhenCalled_SetsWasRolledBack()
        {
            FakeDbSession session = new FakeDbSession();

            await session.RollbackAsync();

            Assert.True(session.WasRolledBack);
        }

        [Fact]
        public async Task RollbackAsync_WhenCalledMultipleTimes_RemainsRolledBack()
        {
            FakeDbSession session = new FakeDbSession();

            await session.RollbackAsync();
            await session.RollbackAsync();

            Assert.True(session.WasRolledBack);
        }

        [Fact]
        public async Task DisposeAsync_WhenCalled_SetsWasDisposed()
        {
            FakeDbSession session = new FakeDbSession();

            await session.DisposeAsync();

            Assert.True(session.WasDisposed);
        }

        [Fact]
        public async Task DisposeAsync_WhenCalledMultipleTimes_RemainsDisposed()
        {
            FakeDbSession session = new FakeDbSession();

            await session.DisposeAsync();
            await session.DisposeAsync();

            Assert.True(session.WasDisposed);
        }

        [Fact]
        public void Reset_WhenCalled_ClearsAllFlags()
        {
            FakeDbSession session = new FakeDbSession();

            session.CommitAsync();
            session.RollbackAsync();
            session.DisposeAsync();

            session.Reset();

            Assert.False(session.WasCommitted);
            Assert.False(session.WasRolledBack);
            Assert.False(session.WasDisposed);
        }

        [Fact]
        public void CommitAsync_WhenNotCalled_WasCommittedIsFalse()
        {
            FakeDbSession session = new FakeDbSession();

            Assert.False(session.WasCommitted);
        }

        [Fact]
        public void RollbackAsync_WhenNotCalled_WasRolledBackIsFalse()
        {
            FakeDbSession session = new FakeDbSession();

            Assert.False(session.WasRolledBack);
        }

        [Fact]
        public void DisposeAsync_WhenNotCalled_WasDisposedIsFalse()
        {
            FakeDbSession session = new FakeDbSession();

            Assert.False(session.WasDisposed);
        }
    }
}

