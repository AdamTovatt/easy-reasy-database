using System.Data.Common;
using EasyReasy.Database;
using EasyReasy.Database.Mapping;
using EasyReasy.EnvironmentVariables;

namespace EasyReasy.Database.Mapping.Npgsql.Tests
{
    /// <summary>
    /// Tests that <see cref="NpgsqlDataSourceBuilderExtensions.MapDbNameEnum{T}"/>
    /// correctly round-trips enum values whose <see cref="DbNameAttribute"/> values
    /// differ structurally from their C# member names (e.g. <c>K2Cyber</c> ↔ <c>k2_cyber</c>).
    /// Earlier tests only covered cases where the DB name was a case-insensitive match
    /// (e.g. <c>Active</c> ↔ <c>active</c>), which Npgsql resolves on its own.
    /// </summary>
    [Collection("Database")]
    public class MismatchedEnumNameTests : IAsyncLifetime
    {
        private readonly TestDatabaseFixture _fixture;
        private DbDataSource _plainDataSource = null!;
        private DbDataSource _mappedDataSource = null!;
        private DbConnection _connection = null!;
        private DbTransaction _transaction = null!;

        public MismatchedEnumNameTests(TestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        public async Task InitializeAsync()
        {
            string connectionString = EnvironmentVariables.DatabaseConnectionString.GetValue();

            // Phase 1: create enum type and table with a plain (unmapped) data source.
            NpgsqlDataSourceFactory plainFactory = new NpgsqlDataSourceFactory();
            _plainDataSource = plainFactory.CreateDataSource(connectionString);

            await using (DbConnection conn = await _plainDataSource.OpenConnectionAsync())
            await using (DbCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    DO $$ BEGIN
                        IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'mismatched_provider_enum') THEN
                            CREATE TYPE mismatched_provider_enum AS ENUM ('k2_cyber', 'other_provider');
                        END IF;
                    END $$;

                    CREATE TABLE IF NOT EXISTS mismatched_enum_test (
                        id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                        name TEXT NOT NULL,
                        provider mismatched_provider_enum NOT NULL
                    );

                    DELETE FROM mismatched_enum_test;";
                await cmd.ExecuteNonQueryAsync();
            }

            // Phase 2: create mapped data source now that the PG enum type exists.
            NpgsqlDataSourceFactory mappedFactory = new NpgsqlDataSourceFactory(builder =>
            {
                builder.MapDbNameEnum<MismatchedProvider>();
            });
            _mappedDataSource = mappedFactory.CreateDataSource(connectionString);

            _connection = await _mappedDataSource.OpenConnectionAsync();
            _transaction = await _connection.BeginTransactionAsync();
        }

        public async Task DisposeAsync()
        {
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
            await _connection.DisposeAsync();

            // Clean up the test-specific table and enum type.
            await using (DbConnection conn = await _plainDataSource.OpenConnectionAsync())
            await using (DbCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    DROP TABLE IF EXISTS mismatched_enum_test;
                    DROP TYPE IF EXISTS mismatched_provider_enum;";
                await cmd.ExecuteNonQueryAsync();
            }

            if (_mappedDataSource is IDisposable mappedDisposable)
            {
                mappedDisposable.Dispose();
            }

            if (_plainDataSource is IDisposable plainDisposable)
            {
                plainDisposable.Dispose();
            }

            TypeHandlerRegistry.Clear();
        }

        [Fact]
        public async Task WriteAndRead_WithMismatchedEnumName_RoundTrips()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mismatched_enum_test (name, provider) VALUES (@name, @provider)",
                new { name = "test_k2", provider = MismatchedProvider.K2Cyber },
                _transaction);

            MismatchedEnumEntity? result = await _connection.QuerySingleOrDefaultAsync<MismatchedEnumEntity>(
                "SELECT name, provider FROM mismatched_enum_test WHERE name = @name",
                new { name = "test_k2" },
                _transaction);

            Assert.NotNull(result);
            Assert.Equal(MismatchedProvider.K2Cyber, result.Provider);
        }

        [Fact]
        public async Task WriteAndRead_AllMismatchedValues_RoundTrip()
        {
            await _connection.ExecuteAsync(
                "INSERT INTO mismatched_enum_test (name, provider) VALUES (@name, @provider)",
                new { name = "test_k2_all", provider = MismatchedProvider.K2Cyber },
                _transaction);

            await _connection.ExecuteAsync(
                "INSERT INTO mismatched_enum_test (name, provider) VALUES (@name, @provider)",
                new { name = "test_other_all", provider = MismatchedProvider.OtherProvider },
                _transaction);

            IEnumerable<MismatchedEnumEntity> results = await _connection.QueryAsync<MismatchedEnumEntity>(
                "SELECT name, provider FROM mismatched_enum_test WHERE name LIKE 'test_%_all' ORDER BY name",
                transaction: _transaction);

            List<MismatchedEnumEntity> list = results.ToList();
            Assert.Equal(2, list.Count);
            Assert.Equal(MismatchedProvider.K2Cyber, list[0].Provider);
            Assert.Equal(MismatchedProvider.OtherProvider, list[1].Provider);
        }
    }

    /// <summary>
    /// Enum whose C# member names differ structurally from their database values.
    /// <c>K2Cyber</c> maps to <c>k2_cyber</c> — Npgsql's default case-insensitive
    /// matching cannot resolve this, so the handler must do it.
    /// </summary>
    [DbEnum("mismatched_provider_enum")]
    public enum MismatchedProvider
    {
        [DbName("k2_cyber")]
        K2Cyber,

        [DbName("other_provider")]
        OtherProvider
    }

    public class MismatchedEnumEntity
    {
        public string Name { get; set; } = string.Empty;
        public MismatchedProvider Provider { get; set; }
    }
}
