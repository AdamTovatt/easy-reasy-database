using System.Data.Common;

namespace EasyReasy.Database
{
    /// <summary>
    /// Factory interface for creating database data sources.
    /// </summary>
    public interface IDataSourceFactory
    {
        /// <summary>
        /// Creates a new database data source from the provided connection string.
        /// </summary>
        /// <param name="connectionString">The database connection string.</param>
        /// <returns>A configured database data source.</returns>
        DbDataSource CreateDataSource(string connectionString);
    }
}

