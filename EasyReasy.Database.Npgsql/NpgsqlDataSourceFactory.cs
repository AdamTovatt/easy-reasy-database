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
        private Action<NpgsqlDataSourceBuilder>? _builderAction;

        /// <summary>
        /// Initializes a new instance of the <see cref="NpgsqlDataSourceFactory"/> class.
        /// </summary>
        public NpgsqlDataSourceFactory() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="NpgsqlDataSourceFactory"/> class with a builder action.
        /// </summary>
        /// <param name="builerAcion">An optional action to configure the data source builder (e.g., enum mappings).</param>
        public NpgsqlDataSourceFactory(Action<NpgsqlDataSourceBuilder>? builerAcion)
        {
            _builderAction = builerAcion;
        }

        /// <summary>
        /// Creates a new Npgsql data source from the provided connection string.
        /// Applies the configured builder action if one was provided.
        /// </summary>
        /// <param name="connectionString">The PostgreSQL connection string.</param>
        /// <returns>A configured Npgsql data source.</returns>
        public DbDataSource CreateDataSource(string connectionString)
        {
            NpgsqlDataSourceBuilder dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);

            _builderAction?.Invoke(dataSourceBuilder);

            return dataSourceBuilder.Build();
        }
    }
}

