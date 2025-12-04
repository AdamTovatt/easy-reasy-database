using System.Data.Common;

namespace EasyReasy.Database
{
    /// <summary>
    /// Implementation of IDbSession that wraps a connection and optional transaction.
    /// Handles proper disposal of resources and transaction management.
    /// </summary>
    public sealed class DbSession : IDbSession
    {
        private bool _disposed;

        /// <summary>
        /// Gets the database connection.
        /// </summary>
        public DbConnection Connection { get; }

        /// <summary>
        /// Gets the transaction, if one exists. Null if no transaction is active.
        /// </summary>
        public DbTransaction? Transaction { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DbSession"/> class.
        /// </summary>
        /// <param name="connection">The database connection. Must not be null.</param>
        /// <param name="transaction">Optional transaction to associate with the session.</param>
        /// <exception cref="ArgumentNullException">Thrown when connection is null.</exception>
        public DbSession(DbConnection connection, DbTransaction? transaction)
        {
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            Transaction = transaction;
        }

        /// <summary>
        /// Commits the active transaction.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <exception cref="InvalidOperationException">Thrown when no active transaction exists.</exception>
        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            if (Transaction == null)
                throw new InvalidOperationException("No active transaction to commit");

            await Transaction.CommitAsync(cancellationToken);
        }

        /// <summary>
        /// Rolls back the active transaction.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <exception cref="InvalidOperationException">Thrown when no active transaction exists.</exception>
        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            if (Transaction == null)
                throw new InvalidOperationException("No active transaction to rollback");

            await Transaction.RollbackAsync(cancellationToken);
        }

        /// <summary>
        /// Disposes the session, releasing the connection and transaction resources.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            if (Transaction != null)
                await Transaction.DisposeAsync();

            await Connection.DisposeAsync();

            _disposed = true;
        }
    }
}

