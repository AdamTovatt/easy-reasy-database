using Npgsql;

namespace EasyReasy.Database.Testing.Npgsql
{
    /// <summary>
    /// Derives the connection strings a test harness needs from the one it was configured with, keeping
    /// the host, port and credentials and varying only the database.
    /// </summary>
    public static class PostgresConnectionStrings
    {
        /// <summary>
        /// The always-present database used to take cluster-wide locks and to run <c>CREATE DATABASE</c>,
        /// neither of which can run from inside the database being created.
        /// </summary>
        public const string MaintenanceDatabase = "postgres";

        /// <summary>Returns <paramref name="connectionString"/> pointed at <paramref name="database"/>.</summary>
        public static string WithDatabase(string connectionString, string database)
        {
            return new NpgsqlConnectionStringBuilder(connectionString)
            {
                Database = database,
            }.ConnectionString;
        }

        /// <summary>Returns <paramref name="connectionString"/> pointed at the maintenance database.</summary>
        public static string Maintenance(string connectionString)
        {
            return WithDatabase(connectionString, MaintenanceDatabase);
        }

        /// <summary>
        /// The maintenance connection a cluster-wide advisory lock is held on, which must be UNPOOLED.
        /// An advisory lock belongs to the session, and a pooled connection's session outlives the
        /// <c>NpgsqlConnection</c> that borrowed it: Npgsql defers its <c>DISCARD ALL</c> reset until the
        /// connection is next used — verified against Npgsql 10 — so returning a lock holder to the pool
        /// would leave the lock held by an idle session and deadlock the next run.
        /// </summary>
        public static string MaintenanceUnpooled(string connectionString)
        {
            return new NpgsqlConnectionStringBuilder(connectionString)
            {
                Database = MaintenanceDatabase,
                Pooling = false,
            }.ConnectionString;
        }
    }
}
