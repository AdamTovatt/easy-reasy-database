using System.Data.Common;

namespace EasyReasy.Database.Testing
{
    /// <summary>
    /// Fake implementation of IDbSession for unit testing services.
    /// In service unit tests, repositories are mocked and don't actually use the connection/transaction.
    /// This fake tracks whether Commit/Rollback/Dispose were called for verification in tests.
    /// </summary>
    public class FakeDbSession : IDbSession
    {
        /// <summary>
        /// Gets the database connection. Always returns null in this fake since mocked repositories don't use it.
        /// </summary>
        public DbConnection Connection => null!;

        /// <summary>
        /// Gets the transaction. Always returns null in this fake since mocked repositories don't use it.
        /// </summary>
        public DbTransaction? Transaction => null;

        /// <summary>
        /// Gets a value indicating whether CommitAsync was called.
        /// </summary>
        public bool WasCommitted { get; private set; }

        /// <summary>
        /// Gets a value indicating whether RollbackAsync was called.
        /// </summary>
        public bool WasRolledBack { get; private set; }

        /// <summary>
        /// Gets a value indicating whether DisposeAsync was called.
        /// </summary>
        public bool WasDisposed { get; private set; }

        /// <inheritdoc/>
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            WasCommitted = true;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            WasRolledBack = true;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public ValueTask DisposeAsync()
        {
            WasDisposed = true;
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Resets all tracking state.
        /// </summary>
        public void Reset()
        {
            WasCommitted = false;
            WasRolledBack = false;
            WasDisposed = false;
        }
    }
}

