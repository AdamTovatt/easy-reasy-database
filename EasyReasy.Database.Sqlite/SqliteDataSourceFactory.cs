using Microsoft.Data.Sqlite;
using System.Data.Common;

namespace EasyReasy.Database.Sqlite
{
    public class SqliteDataSourceFactory : IDataSourceFactory
    {
        public DbDataSource CreateDataSource(string connectionString)
        {
            DbProviderFactory providerFactory = SqliteFactory.Instance;
            DbDataSource dataSource = providerFactory.CreateDataSource(connectionString);
            return dataSource;
        }
    }
}
