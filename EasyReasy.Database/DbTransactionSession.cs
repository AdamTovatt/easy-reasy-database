using System.Data.Common;

namespace EasyReasy.Database
{
    /// <summary>
    /// Implementation of IDbTransactionSession that wraps a connection and its active transaction.
    /// Handles proper disposal of resources and transaction management.
    /// </summary>
    public sealed class DbTransactionSession : IDbTransactionSession
    {
        private bool _disposed;

        /// <summary>
        /// Gets the database connection.
        /// </summary>
        public DbConnection Connection { get; }

        /// <summary>
        /// Gets the active transaction. Never null.
        /// </summary>
        public DbTransaction Transaction { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DbTransactionSession"/> class.
        /// The session takes ownership of both the connection and the transaction and disposes them.
        /// </summary>
        /// <param name="connection">The database connection. Must not be null.</param>
        /// <param name="transaction">The transaction to associate with the session. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when connection or transaction is null.</exception>
        public DbTransactionSession(DbConnection connection, DbTransaction transaction)
        {
            Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            Transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        }

        /// <summary>
        /// Commits the transaction.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            return Transaction.CommitAsync(cancellationToken);
        }

        /// <summary>
        /// Rolls back the transaction.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            return Transaction.RollbackAsync(cancellationToken);
        }

        /// <summary>
        /// Disposes the session, releasing the transaction and then the connection.
        /// An uncommitted transaction is rolled back. The connection is released even if releasing the transaction throws.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                await Transaction.DisposeAsync();
            }
            finally
            {
                await Connection.DisposeAsync();
            }
        }
    }
}
