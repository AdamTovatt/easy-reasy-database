using EasyReasy.Database.Testing.Npgsql;
using Npgsql;

namespace EasyReasy.Database.Testing.Npgsql.Tests
{
    /// <summary>
    /// The catalog reads and drops the database-backed tests need to check their own work. Kept out of
    /// the test classes so a change to how a database is looked up cannot end up meaning two different
    /// things in two files — these are the assertions' ground truth, so they have to agree.
    /// </summary>
    internal static class TestCluster
    {
        /// <summary>The configured connection string pointed at <paramref name="databaseName"/>.</summary>
        internal static string ConnectionStringFor(string databaseName)
        {
            return PostgresConnectionStrings.WithDatabase(TestConnection.ConnectionString, databaseName);
        }

        /// <summary>Whether <paramref name="databaseName"/> exists in the cluster.</summary>
        internal static async Task<bool> DatabaseExistsAsync(string databaseName)
        {
            return await ScalarAsync("SELECT 1 FROM pg_database WHERE datname = @name", databaseName) != null;
        }

        /// <summary>The ownership comment on <paramref name="databaseName"/>, or null if it carries none.</summary>
        internal static async Task<string?> CommentOnAsync(string databaseName)
        {
            return (string?)await ScalarAsync(
                "SELECT shobj_description(oid, 'pg_database') FROM pg_database WHERE datname = @name",
                databaseName);
        }

        /// <summary>Whether <paramref name="roleName"/> exists in the cluster.</summary>
        internal static async Task<bool> RoleExistsAsync(string roleName)
        {
            return await ScalarAsync("SELECT 1 FROM pg_roles WHERE rolname = @name", roleName) != null;
        }

        /// <summary>Whether <paramref name="memberRole"/> is a member of <paramref name="grantedRole"/>.</summary>
        internal static async Task<bool> RoleIsMemberOfAsync(string memberRole, string grantedRole)
        {
            await using NpgsqlConnection connection = await OpenMaintenanceAsync();
            await using NpgsqlCommand command = new NpgsqlCommand(
                """
                SELECT 1
                FROM pg_auth_members membership
                JOIN pg_roles granted ON granted.oid = membership.roleid
                JOIN pg_roles member ON member.oid = membership.member
                WHERE granted.rolname = @granted AND member.rolname = @member
                """,
                connection);
            command.Parameters.AddWithValue("granted", grantedRole);
            command.Parameters.AddWithValue("member", memberRole);

            return await command.ExecuteScalarAsync() != null;
        }

        /// <summary>
        /// Drops <paramref name="databaseName"/> if it is there. <c>WITH (FORCE)</c> (PostgreSQL 13 and
        /// later) so a connection left open by a failed test cannot wedge the cleanup.
        /// </summary>
        internal static async Task DropDatabaseAsync(string databaseName)
        {
            await using NpgsqlConnection connection = await OpenMaintenanceAsync();
            await using NpgsqlCommand command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>Creates <paramref name="roleName"/>, which must not already exist.</summary>
        internal static async Task CreateRoleAsync(string roleName, string options)
        {
            await using NpgsqlConnection connection = await OpenMaintenanceAsync();
            await using NpgsqlCommand command = new NpgsqlCommand($"CREATE ROLE {roleName} {options}", connection);
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Drops <paramref name="roleName"/> if it is there. Roles are CLUSTER-wide, so a test that
        /// creates one must remove it or it outlives the run and leaks into every other checkout's.
        /// </summary>
        internal static async Task DropRoleAsync(string roleName)
        {
            await using NpgsqlConnection connection = await OpenMaintenanceAsync();
            await using NpgsqlCommand command = new NpgsqlCommand($"DROP ROLE IF EXISTS {roleName}", connection);
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<object?> ScalarAsync(string sql, string parameterValue)
        {
            await using NpgsqlConnection connection = await OpenMaintenanceAsync();
            await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("name", parameterValue);

            object? value = await command.ExecuteScalarAsync();
            return value == DBNull.Value ? null : value;
        }

        private static async Task<NpgsqlConnection> OpenMaintenanceAsync()
        {
            NpgsqlConnection connection = new NpgsqlConnection(
                PostgresConnectionStrings.Maintenance(TestConnection.ConnectionString));
            await connection.OpenAsync();
            return connection;
        }
    }
}
