using System.Data.Common;

namespace EasyReasy.Database
{
    /// <summary>
    /// Represents a database session with connection and optional transaction.
    /// Allows repositories to work with either a plain connection or within an existing transaction.
    /// Implements IAsyncDisposable for proper resource cleanup.
    /// </summary>
    public interface IDbSession : IAsyncDisposable
    {
        /// <summary>
        /// Gets the database connection.
        /// </summary>
        DbConnection Connection { get; }

        /// <summary>
        /// Gets the transaction, if one exists. Null if no transaction is active.
        /// </summary>
        DbTransaction? Transaction { get; }

        /// <summary>
        /// Commits the active transaction.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <exception cref="InvalidOperationException">Thrown when no active transaction exists.</exception>
        Task CommitAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Rolls back the active transaction.
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <exception cref="InvalidOperationException">Thrown when no active transaction exists.</exception>
        Task RollbackAsync(CancellationToken cancellationToken = default);
    }
}

