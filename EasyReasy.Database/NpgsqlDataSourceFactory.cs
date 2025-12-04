using CarelessApi.Domain.Customers.Models;
using Npgsql;
using System.Data.Common;

namespace EasyReasy.Database
{
    /// <summary>
    /// Npgsql-specific implementation of IDataSourceFactory.
    /// Configures the data source with required enum mappings for PostgreSQL.
    /// </summary>
    public class NpgsqlDataSourceFactory : IDataSourceFactory
    {
        /// <summary>
        /// Creates a new Npgsql data source from the provided connection string.
        /// Configures enum mappings required for proper database interaction.
        /// </summary>
        /// <param name="connectionString">The PostgreSQL connection string.</param>
        /// <returns>A configured Npgsql data source.</returns>
        public DbDataSource CreateDataSource(string connectionString)
        {
            NpgsqlDataSourceBuilder dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);

            dataSourceBuilder.MapEnum<Pronoun>("pronoun");

            return dataSourceBuilder.Build();
        }
    }
}

