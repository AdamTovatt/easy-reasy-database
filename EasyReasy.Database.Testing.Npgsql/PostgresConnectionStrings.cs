using Npgsql;

namespace EasyReasy.Database.Testing.Npgsql
{
    /// <summary>
    /// Derives the connection strings a test harness needs from the one it was configured with. Each
    /// varies one axis — the database, or pooling — and every one of them keeps the host, port and
    /// credentials, which is the property that matters: a derivation that dropped them would not fail, it
    /// would quietly connect somewhere else.
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
        /// Returns <paramref name="connectionString"/> with pooling turned off, for work whose SESSION
        /// state matters. A pooled connection's session outlives the <c>NpgsqlConnection</c> that borrowed
        /// it — Npgsql defers its <c>DISCARD ALL</c> reset until the connection is next used, verified
        /// against Npgsql 10 — so anything that must end when the object is disposed needs this.
        /// </summary>
        public static string Unpooled(string connectionString)
        {
            return new NpgsqlConnectionStringBuilder(connectionString)
            {
                Pooling = false,
            }.ConnectionString;
        }

        /// <summary>
        /// The maintenance connection a cluster-wide advisory lock is held on, which must be UNPOOLED.
        /// An advisory lock belongs to the session, so returning a lock holder to the pool would leave the
        /// lock held by an idle session and deadlock the next run. See <see cref="Unpooled"/> for why a
        /// pooled session outlives its <c>NpgsqlConnection</c>.
        /// </summary>
        public static string MaintenanceUnpooled(string connectionString)
        {
            return Unpooled(Maintenance(connectionString));
        }
    }
}
