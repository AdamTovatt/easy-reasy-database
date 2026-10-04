using System.Data.Common;

namespace EasyReasy.Database.Testing
{
    /// <summary>
    /// Fake implementation of IDbTransactionSession for unit testing services.
    /// In service unit tests, repositories are mocked and don't actually use the connection/transaction.
    /// This fake tracks whether Commit/Rollback/Dispose were called for verification in tests.
    /// </summary>
    public class FakeDbSession : IDbTransactionSession
    {
        /// <summary>
        /// Gets the database connection. Always returns null in this fake since mocked repositories don't use it.
        /// </summary>
        public DbConnection Connection => null!;

        /// <summary>
        /// Gets the transaction: one <see cref="FakeDbTransaction"/> per session, the same instance on every read,
        /// whether the session is read as an <see cref="IDbTransactionSession"/> or as an <see cref="IDbSession"/>.
        /// </summary>
        public DbTransaction Transaction { get; } = new FakeDbTransaction();

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

