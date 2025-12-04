namespace EasyReasy.Database
{
    /// <summary>
    /// Factory interface for creating database sessions.
    /// This abstraction allows consistent session creation across repositories and services,
    /// and enables easy mocking in unit tests.
    /// </summary>
    public interface IDbSessionFactory
    {
        /// <summary>
        /// Creates a new database session without a transaction.
        /// </summary>
        /// <returns>A database session without a transaction.</returns>
        Task<IDbSession> CreateSessionAsync();

        /// <summary>
        /// Creates a new database session with an active transaction.
        /// The caller is responsible for committing or rolling back the transaction.
        /// </summary>
        /// <returns>A database session with an active transaction.</returns>
        Task<IDbSession> CreateSessionWithTransactionAsync();
    }
}

