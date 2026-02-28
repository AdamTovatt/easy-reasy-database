using System.Data.Common;
using EasyReasy.Database;
using EasyReasy.EnvironmentVariables;
using Npgsql;

namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// Defines the xUnit test collection so all test classes share a single fixture instance.
    /// </summary>
    [CollectionDefinition("Database")]
    public class DatabaseCollection : ICollectionFixture<TestDatabaseFixture>
    {
    }

    /// <summary>
    /// Provides a shared PostgreSQL data source for all integration tests.
    /// Creates test-specific enum types and tables on initialization, drops them on teardown.
    /// Used as a collection fixture so it is created once per test run.
    /// </summary>
    public class TestDatabaseFixture : IAsyncLifetime
    {
        private DbDataSource _dataSource = null!;
        private string _connectionString = null!;

        /// <summary>
        /// Gets the database data source for creating connections.
        /// </summary>
        public DbDataSource DataSource => _dataSource;

        public async Task InitializeAsync()
        {
            string variablesFilePath = Path.Combine("..", "..", "EnvironmentVariables.txt");

            if (!File.Exists(variablesFilePath))
            {
                string exampleContent = EnvironmentVariableHelper.GetExampleContent(
                    "DATABASE_CONNECTION_STRING", "Host=localhost;Port=5432;Database=easy-reasy-db-mapping;Username=postgres;Password=postgres");

                File.WriteAllText(variablesFilePath, exampleContent);
            }

            EnvironmentVariableHelper.LoadVariablesFromFile(variablesFilePath);
            EnvironmentVariableHelper.ValidateVariableNamesIn(typeof(EnvironmentVariables));

            _connectionString = EnvironmentVariables.DatabaseConnectionString.GetValue();

            // Phase 1: Create the enum type and table BEFORE creating the mapped data source.
            // Npgsql's MapEnum<T> requires the PostgreSQL type to already exist when the first
            // connection is opened, otherwise it can't resolve the type OID.
            NpgsqlDataSourceFactory plainFactory = new NpgsqlDataSourceFactory();
            DbDataSource plainDataSource = plainFactory.CreateDataSource(_connectionString);

            await using (DbConnection connection = await plainDataSource.OpenConnectionAsync())
            await using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
                    DO $$ BEGIN
                        IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'mapping_test_status') THEN
                            CREATE TYPE mapping_test_status AS ENUM ('active', 'inactive', 'pending');
                        END IF;
                    END $$;

                    CREATE TABLE IF NOT EXISTS mapping_test (
                        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                        name TEXT NOT NULL,
                        value INT,
                        status mapping_test_status,
                        created_at TIMESTAMPTZ DEFAULT now(),
                        description TEXT,
                        is_active BOOLEAN DEFAULT true,
                        score DECIMAL(10,2),
                        birth_date DATE,
                        start_time TIME
                    );

                    DELETE FROM mapping_test;";
                await command.ExecuteNonQueryAsync();
            }

            if (plainDataSource is IDisposable disposable)
            {
                disposable.Dispose();
            }

            // Phase 2: Create the data source WITH enum mapping now that the type exists.
            NpgsqlDataSourceFactory mappedFactory = new NpgsqlDataSourceFactory(builder =>
            {
                builder.MapEnum<TestStatus>("mapping_test_status");
            });
            _dataSource = mappedFactory.CreateDataSource(_connectionString);
        }

        public async Task DisposeAsync()
        {
            await using DbConnection connection = await _dataSource.OpenConnectionAsync();
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = @"
                DROP TABLE IF EXISTS mapping_test;
                DROP TYPE IF EXISTS mapping_test_status;";
            await command.ExecuteNonQueryAsync();

            if (_dataSource is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
