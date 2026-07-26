using EasyReasy.Database.Sql;
using Npgsql;

namespace EasyReasy.Database.Testing.Npgsql
{
    /// <summary>
    /// Idempotent bootstrap for the cluster roles a test suite needs before its migrations run.
    /// <para>
    /// Roles and role memberships are CLUSTER objects, so per-checkout databases give no isolation here —
    /// this is the one piece of state two concurrent runs still share, and the reason the bootstrap runs
    /// under <see cref="PostgresClusterLock"/>. Every harness in a repository (including any non-.NET
    /// mirror, such as a Playwright global setup) must bootstrap the same roles under the same lock key.
    /// </para>
    /// </summary>
    public static class TestClusterRoles
    {
        /// <summary>
        /// Creates <paramref name="roles"/> if missing and grants <paramref name="migrationRole"/>
        /// membership in the ones marked <see cref="TestClusterRole.GrantToMigrationRole"/>. Takes the
        /// cluster-wide lock identified by <paramref name="lockKey"/> for the duration.
        /// </summary>
        public static async Task EnsureAsync(string connectionString, string migrationRole, IReadOnlyList<TestClusterRole> roles, long lockKey)
        {
            // Role names cannot be parameterised, so they are interpolated into the DDL below — refuse
            // anything that is not a plain identifier before it reaches a statement. Note that
            // TestClusterRole.Options is deliberately NOT checked: it is raw SQL by design.
            SqlIdentifier.RequirePlain(migrationRole, "Migration role name", "role bootstrap");

            foreach (TestClusterRole role in roles)
            {
                SqlIdentifier.RequirePlain(role.Name, "Cluster role name", "role bootstrap");
            }

            string sql = string.Concat(roles.Select(role => CreateRoleIfMissingSql(role.Name, role.Options)))
                + string.Concat(roles
                    .Where(role => role.GrantToMigrationRole)
                    .Select(role => $"\nGRANT {role.Name} TO {migrationRole};"));

            await using PostgresClusterLock clusterLock = await PostgresClusterLock.AcquireAsync(connectionString, lockKey);

            await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Emits an idempotent <c>CREATE ROLE</c>. The existence check keeps the common case quiet; the
        /// handler makes the statement safe when another session wins the race between the check and the
        /// create. That loser gets <c>unique_violation</c> from <c>pg_authid_rolname_index</c> — NOT
        /// <c>duplicate_object</c>, which is what the same statement raises when the role was already
        /// visible — so both are handled.
        /// </summary>
        public static string CreateRoleIfMissingSql(string roleName, string options)
        {
            return $@"
                DO $$
                BEGIN
                   IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{roleName}') THEN
                      CREATE ROLE {roleName} {options};
                   END IF;
                EXCEPTION WHEN duplicate_object OR unique_violation THEN
                   NULL;
                END
                $$;";
        }
    }
}
