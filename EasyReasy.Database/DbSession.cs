using System.Data.Common;

namespace EasyReasy.Database
{
    /// <summary>
    /// Implementation of IDbSession that wraps a connection without a transaction.
    /// Each command auto-commits when executed. A session with a transaction is a <see cref="DbTransactionSession"/>.
    /// Handles proper disposal of the connection.
    /// </summary>
    public sealed class DbSession : IDbSession
    {
        private bool _disposed;

        /// <summary>
        /// Gets the database connection.
        /// </summary>
        public DbConnection Connection { get; }

        /// <summary>
        /// Gets the transaction. Always null, because this session never has a transaction.
        /// </summary>
        public DbTransaction? Transaction => null;

        /// <summary>
        /// Initializes a new instance of the <see cref="DbSession"/> class.
        /// The session takes ownership of the connection and disposes it.
        /// </summary>
        /// <param name="connection">The database connection. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when connection is null.</exception>
        public DbSession(DbConnection connection)
        {
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        /// <summary>
        /// Always fails, because this session has no transaction to commit.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <exception cref="InvalidOperationException">Always; the returned task faults with it.</exception>
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromException(new InvalidOperationException("No active transaction to commit"));
        }

        /// <summary>
        /// Always fails, because this session has no transaction to roll back.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <exception cref="InvalidOperationException">Always; the returned task faults with it.</exception>
        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromException(new InvalidOperationException("No active transaction to rollback"));
        }

        /// <summary>
        /// Disposes the session, releasing the connection.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            await Connection.DisposeAsync();

            _disposed = true;
        }
    }
}
