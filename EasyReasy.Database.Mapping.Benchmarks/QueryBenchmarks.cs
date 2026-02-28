using System.Data.Common;
using BenchmarkDotNet.Attributes;
using EasyReasy.EnvironmentVariables;
using Npgsql;

namespace EasyReasy.Database.Mapping.Benchmarks
{
    [MemoryDiagnoser]
    public class QueryBenchmarks
    {
        private NpgsqlDataSource _dataSource = null!;
        private DbConnection _connection = null!;

        [Params(100, 1000, 5000)]
        public int RowCount { get; set; }

        [GlobalSetup]
        public async Task GlobalSetup()
        {
            string connectionString = EnvironmentVariables.DatabaseConnectionString.GetValue();

            // Phase 1: Create enum type and tables using a plain data source.
            NpgsqlDataSourceBuilder plainBuilder = new NpgsqlDataSourceBuilder(connectionString);
            await using NpgsqlDataSource plainDataSource = plainBuilder.Build();

            await using (NpgsqlConnection setupConn = await plainDataSource.OpenConnectionAsync())
            await using (NpgsqlCommand command = setupConn.CreateCommand())
            {
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS benchmark_data (
                        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                        name TEXT NOT NULL,
                        value INT NOT NULL,
                        score DECIMAL(10,2) NOT NULL,
                        is_active BOOLEAN NOT NULL DEFAULT true,
                        created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                        description TEXT
                    );

                    DELETE FROM benchmark_data;

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

            // Seed benchmark_data rows.
            await using (NpgsqlConnection seedConn = await plainDataSource.OpenConnectionAsync())
            {
                const int batchSize = 500;
                int maxRows = 5000; // Largest Params value.

                for (int i = 0; i < maxRows; i += batchSize)
                {
                    int count = Math.Min(batchSize, maxRows - i);
                    await using NpgsqlCommand cmd = seedConn.CreateCommand();
                    cmd.CommandText = $@"
                        INSERT INTO benchmark_data (name, value, score, is_active, description)
                        SELECT
                            'item_' || g,
                            (random() * 10000)::int,
                            (random() * 1000)::decimal(10,2),
                            (random() > 0.5),
                            'Description for item ' || g
                        FROM generate_series({i + 1}, {i + count}) AS g;";
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Seed mapping_test rows with enum values.
            await using (NpgsqlConnection seedConn = await plainDataSource.OpenConnectionAsync())
            {
                await using NpgsqlCommand cmd = seedConn.CreateCommand();
                cmd.CommandText = $@"
                    INSERT INTO mapping_test (name, value, status, description, is_active, score)
                    SELECT
                        'item_' || g,
                        (random() * 10000)::int,
                        (ARRAY['active','inactive','pending'])[1 + (g % 3)]::mapping_test_status,
                        'Description for item ' || g,
                        (random() > 0.5),
                        (random() * 1000)::decimal(10,2)
                    FROM generate_series(1, 5000) AS g;";
                await cmd.ExecuteNonQueryAsync();
            }

            // Phase 2: Create the data source WITH enum mapping for the benchmark connection.
            NpgsqlDataSourceBuilder mappedBuilder = new NpgsqlDataSourceBuilder(connectionString);
            mappedBuilder.MapEnum<TestStatus>("mapping_test_status");
            _dataSource = mappedBuilder.Build();

            _connection = await _dataSource.OpenConnectionAsync();

            // Register the enum handler for our mapping library.
            TypeHandlerRegistry.AddTypeHandler(new DbNameEnumHandler<TestStatus>());
        }

        [GlobalCleanup]
        public async Task GlobalCleanup()
        {
            await using NpgsqlCommand command = ((NpgsqlConnection)_connection).CreateCommand();
            command.CommandText = @"
                DROP TABLE IF EXISTS benchmark_data;
                DROP TABLE IF EXISTS mapping_test;
                DROP TYPE IF EXISTS mapping_test_status;";
            await command.ExecuteNonQueryAsync();

            await _connection.DisposeAsync();
            _dataSource.Dispose();
        }

        [Benchmark]
        public async Task<List<BenchmarkEntity>> Dapper_QueryAsync()
        {
            return (await Dapper.SqlMapper.QueryAsync<BenchmarkEntity>(
                _connection,
                "SELECT id AS Id, name AS Name, value AS Value, score AS Score, is_active AS IsActive, created_at AS CreatedAt, description AS Description FROM benchmark_data LIMIT @count",
                new { count = RowCount })).ToList();
        }

        [Benchmark]
        public async Task<List<BenchmarkEntity>> Mapping_QueryAsync()
        {
            return (await DbConnectionExtensions.QueryAsync<BenchmarkEntity>(
                (DbConnection)_connection,
                "SELECT id AS Id, name AS Name, value AS Value, score AS Score, is_active AS IsActive, created_at AS CreatedAt, description AS Description FROM benchmark_data LIMIT @count",
                new { count = RowCount })).ToList();
        }

        [Benchmark]
        public async Task<List<MappingTestEntity>> Mapping_QueryAsync_WithEnumHandler()
        {
            return (await DbConnectionExtensions.QueryAsync<MappingTestEntity>(
                (DbConnection)_connection,
                "SELECT id AS Id, name AS Name, value AS Value, status AS Status, is_active AS IsActive, created_at AS CreatedAt, description AS Description, score AS Score FROM mapping_test LIMIT @count",
                new { count = RowCount })).ToList();
        }
    }
}
