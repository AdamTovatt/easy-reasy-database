using System.Data.Common;

namespace EasyReasy.Database
{
    /// <summary>
    /// Base repository interface that all repository interfaces must extend.
    /// Provides access to the data source and session factory for session creation.
    /// </summary>
    public interface IRepository
    {
        /// <summary>
        /// Gets the data source used by this repository.
        /// </summary>
        DbDataSource DataSource { get; }

        /// <summary>
        /// Creates a new database session with an active transaction.
        /// Convenience method for services that need to coordinate multiple repository calls in a transaction.
        /// </summary>
        /// <returns>A database session with an active transaction.</returns>
        Task<IDbSession> CreateSessionWithTransactionAsync();
    }
}

