using System.Data.Common;

namespace EasyReasy.Database
{
    /// <summary>
    /// Default implementation of IDbSessionFactory that creates sessions using a DbDataSource.
    /// </summary>
    public class DbSessionFactory : IDbSessionFactory
    {
        private readonly DbDataSource _dataSource;

        /// <summary>
        /// Initializes a new instance of the <see cref="DbSessionFactory"/> class.
        /// </summary>
        /// <param name="dataSource">The data source to create connections from.</param>
        public DbSessionFactory(DbDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        /// <inheritdoc/>
        public async Task<IDbSession> CreateSessionAsync()
        {
            // The DbSession takes ownership of the connection and will dispose it
            DbConnection connection = await _dataSource.OpenConnectionAsync();
            return new DbSession(connection, transaction: null);
        }

        /// <inheritdoc/>
        public async Task<IDbSession> CreateSessionWithTransactionAsync()
        {
            // The DbSession takes ownership of the connection and will dispose it
            DbConnection connection = await _dataSource.OpenConnectionAsync();
            DbTransaction transaction = await connection.BeginTransactionAsync();
            return new DbSession(connection, transaction);
        }
    }
}

