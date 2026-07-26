using Npgsql;

namespace EasyReasy.Database.Testing.Npgsql
{
    /// <summary>
    /// A mutex shared by every test process on one PostgreSQL cluster, used to serialise the parts of a
    /// test harness bootstrap that touch cluster-wide state (roles and role memberships live in the
    /// cluster, not in a database, so per-checkout databases do not isolate them).
    /// <para>
    /// PostgreSQL advisory locks are scoped to the database the session is connected to, so a lock taken
    /// in one checkout's database would not exclude a session in another's. This holds its lock on the
    /// <c>postgres</c> maintenance database, which every checkout's run connects to, making it genuinely
    /// cluster-wide. The lock is session-scoped: closing the connection releases it, so a killed test run
    /// cannot wedge the cluster.
    /// </para>
    /// <para>
    /// Callers in one repository must agree on the key they lock under — two bootstraps under different
    /// keys would not exclude each other — so pin the key in a constant every harness (including any
    /// non-.NET mirror, such as a Playwright global setup) shares and asserts on.
    /// </para>
    /// </summary>
    public sealed class PostgresClusterLock : IAsyncDisposable
    {
        /// <summary>
        /// How long to wait for another run's bootstrap before giving up. The work under the lock is
        /// expected to be a handful of catalog statements, so a holder that has not released within a
        /// minute is stuck rather than busy — fail loudly instead of hanging the run.
        /// </summary>
        private const int LockTimeoutSeconds = 60;

        private readonly NpgsqlConnection _connection;
        private readonly long _key;

        private PostgresClusterLock(NpgsqlConnection connection, long key)
        {
            _connection = connection;
            _key = key;
        }

        /// <summary>
        /// Acquires the cluster-wide lock identified by <paramref name="key"/>, waiting for any other test
        /// run that holds it. Uses the host/credentials of <paramref name="connectionString"/> but connects
        /// to the maintenance database.
        /// </summary>
        public static async Task<PostgresClusterLock> AcquireAsync(string connectionString, long key)
        {
            NpgsqlConnection connection = new NpgsqlConnection(PostgresConnectionStrings.MaintenanceUnpooled(connectionString));
            await connection.OpenAsync();

            try
            {
                await using (NpgsqlCommand timeout = new NpgsqlCommand($"SET lock_timeout = '{LockTimeoutSeconds}s'", connection))
                {
                    await timeout.ExecuteNonQueryAsync();
                }

                await using (NpgsqlCommand acquire = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection))
                {
                    // Longer than the server-side lock timeout, so a genuinely stuck holder surfaces as
                    // PostgreSQL's "canceling statement due to lock timeout" rather than as an opaque
                    // client-side read timeout.
                    acquire.CommandTimeout = LockTimeoutSeconds + 30;
                    acquire.Parameters.AddWithValue("key", key);
                    await acquire.ExecuteNonQueryAsync();
                }
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }

            return new PostgresClusterLock(connection, key);
        }

        /// <summary>
        /// Releases the lock explicitly, then closes the session. Closing alone would be enough — which is
        /// what makes a killed test run safe — but unlocking first keeps the intent visible.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using NpgsqlCommand release = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", _connection);
                release.Parameters.AddWithValue("key", _key);
                await release.ExecuteNonQueryAsync();
            }
            catch
            {
                // Any failure to unlock means the session is unusable, which released the lock anyway.
                // Swallowed regardless of type so a broken session can neither skip the close below nor
                // mask an exception thrown by the work this lock was guarding.
            }
            finally
            {
                await _connection.DisposeAsync();
            }
        }
    }
}
