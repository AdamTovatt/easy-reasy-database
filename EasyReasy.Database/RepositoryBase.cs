using System.Data.Common;

namespace EasyReasy.Database
{
    /// <summary>
    /// Base repository class that provides common connection management functionality.
    /// Transaction boundaries are controlled by the caller - repository never creates transactions.
    /// </summary>
    public abstract class RepositoryBase : IRepository
    {
        /// <summary>
        /// Gets the data source used by this repository.
        /// </summary>
        public DbDataSource DataSource { get; }

        /// <summary>
        /// Gets the session factory used to create database sessions.
        /// </summary>
        protected IDbSessionFactory SessionFactory { get; }

        protected RepositoryBase(DbDataSource dataSource, IDbSessionFactory sessionFactory)
        {
            DataSource = dataSource;
            SessionFactory = sessionFactory;
        }

        /// <inheritdoc/>
        public Task<IDbSession> CreateSessionWithTransactionAsync()
        {
            return SessionFactory.CreateSessionWithTransactionAsync();
        }

        /// <summary>
        /// Executes an action with a database session.
        /// If a session is provided, uses its connection and transaction.
        /// Otherwise, creates a new session without a transaction.
        /// </summary>
        /// <typeparam name="T">The return type of the action.</typeparam>
        /// <param name="action">The action to execute with the session.</param>
        /// <param name="session">Optional existing session for transaction support.</param>
        /// <returns>The result of the action.</returns>
        protected async Task<T> UseSessionAsync<T>(
            Func<IDbSession, Task<T>> action,
            IDbSession? session = null)
        {
            if (session != null)
                return await action(session);

            // The DbSession takes ownership of the connection and will dispose it
            await using (IDbSession localSession = await SessionFactory.CreateSessionAsync())
            {
                return await action(localSession);
            }
        }

        /// <summary>
        /// Executes an action with a database session.
        /// If a session is provided, uses its connection and transaction.
        /// Otherwise, creates a new session without a transaction.
        /// </summary>
        /// <param name="action">The action to execute with the session.</param>
        /// <param name="session">Optional existing session for transaction support.</param>
        protected async Task UseSessionAsync(
            Func<IDbSession, Task> action,
            IDbSession? session = null)
        {
            if (session != null)
            {
                await action(session);
                return;
            }

            // The DbSession takes ownership of the connection and will dispose it
            await using (IDbSession localSession = await SessionFactory.CreateSessionAsync())
            {
                await action(localSession);
            }
        }
    }
}

