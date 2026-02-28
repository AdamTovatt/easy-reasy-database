using System.Data.Common;
using EasyReasy.Database;
using EasyReasy.EnvironmentVariables;
using Npgsql;

namespace EasyReasy.Database.Mapping.Npgsql.Tests
{
    [CollectionDefinition("Database")]
    public class DatabaseCollection : ICollectionFixture<TestDatabaseFixture>
    {
    }

    public class TestDatabaseFixture : IAsyncLifetime
    {
        private DbDataSource _dataSource = null!;
        private string _connectionString = null!;

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
            NpgsqlDataSourceFactory plainFactory = new NpgsqlDataSourceFactory();
            DbDataSource plainDataSource = plainFactory.CreateDataSource(_connectionString);

            await using (DbConnection connection = await plainDataSource.OpenConnectionAsync())
            await using (DbCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
                    DO $$ BEGIN
                        IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'npgsql_mapping_test_status') THEN
                            CREATE TYPE npgsql_mapping_test_status AS ENUM ('active', 'inactive', 'pending');
                        END IF;
                    END $$;

                    CREATE TABLE IF NOT EXISTS npgsql_mapping_test (
                        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                        name TEXT NOT NULL,
                        status npgsql_mapping_test_status
                    );

                    DELETE FROM npgsql_mapping_test;";
                await command.ExecuteNonQueryAsync();
            }

            if (plainDataSource is IDisposable disposable)
            {
                disposable.Dispose();
            }

            // Phase 2: Create the data source WITHOUT Npgsql enum mapping.
            // We rely on NpgsqlDbNameEnumHandler to handle enum serialization via DataTypeName
            // instead of Npgsql's built-in MapEnum, so no builder action is needed.
            NpgsqlDataSourceFactory factory = new NpgsqlDataSourceFactory();
            _dataSource = factory.CreateDataSource(_connectionString);
        }

        public async Task DisposeAsync()
        {
            await using DbConnection connection = await _dataSource.OpenConnectionAsync();
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = @"
                DROP TABLE IF EXISTS npgsql_mapping_test;
                DROP TYPE IF EXISTS npgsql_mapping_test_status;";
            await command.ExecuteNonQueryAsync();

            if (_dataSource is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
