using EasyReasy.Database.Testing;
using System.Data;

namespace EasyReasy.Database.Tests.Testing
{
    public class FakeDbTransactionTests
    {
        [Fact]
        public void Connection_WhenAccessed_ReturnsNull()
        {
            FakeDbTransaction transaction = new FakeDbTransaction();

            Assert.Null(transaction.Connection);
        }

        [Fact]
        public void IsolationLevel_WhenAccessed_ReturnsUnspecified()
        {
            FakeDbTransaction transaction = new FakeDbTransaction();

            Assert.Equal(IsolationLevel.Unspecified, transaction.IsolationLevel);
        }

        [Fact]
        public async Task CommitAsync_WhenCalledMultipleTimes_DoesNotThrow()
        {
            FakeDbTransaction transaction = new FakeDbTransaction();

            await transaction.CommitAsync();
            await transaction.CommitAsync();
        }

        [Fact]
        public async Task RollbackAsync_WhenCalledMultipleTimes_DoesNotThrow()
        {
            FakeDbTransaction transaction = new FakeDbTransaction();

            await transaction.RollbackAsync();
            await transaction.RollbackAsync();
        }
    }
}
