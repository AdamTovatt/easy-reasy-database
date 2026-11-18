using Npgsql;

namespace EasyReasy.Database.Testing.Npgsql
{
    /// <summary>
    /// A mutex shared by every test process on one PostgreSQL cluster, used to serialise the parts of a
    /// test harness bootstrap that must not run concurrently across checkouts: cluster-wide state such as
    /// roles and role memberships (which live in the cluster, not in a database, so per-checkout databases
    /// do not isolate them), and any other work a harness chooses to hold it across, such as creating a
    /// database and running its first migration.
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
        /// How long <see cref="AcquireAsync"/> waits for another holder when the caller does not say. A
        /// minute suits a key whose holders all do quick work, such as
        /// <see cref="TestClusterRoles.EnsureAsync"/>'s handful of catalog statements, where a holder that
        /// has not released by then is stuck rather than busy. The wait is the waiter's, not the holder's:
        /// when any holder of a key does slower work (creating a database and migrating it, say), every
        /// caller that takes that key passes a wait long enough to outlast it.
        /// </summary>
        private static readonly TimeSpan DefaultLockWait = TimeSpan.FromSeconds(60);

        /// <summary>
        /// How far the acquire command's client-side timeout exceeds the server-side lock timeout, so a
        /// genuinely stuck holder surfaces as PostgreSQL's "canceling statement due to lock timeout"
        /// rather than as an opaque client-side read timeout.
        /// </summary>
        private const int CommandTimeoutMarginSeconds = 30;

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
        /// <param name="connectionString">Any connection string for the cluster; only its host and credentials are used.</param>
        /// <param name="key">The advisory-lock key every cooperating harness agrees on.</param>
        /// <param name="lockWait">
        /// How long to wait for another holder before failing with PostgreSQL's lock-timeout error
        /// (SQLSTATE 55P03). Null means 60 seconds. Rounded up to whole seconds, so 1.5 seconds waits 2.
        /// Must be positive, because PostgreSQL reads a zero <c>lock_timeout</c> as waiting forever, and at
        /// most about 24.8 days, PostgreSQL's limit. This bounds only this caller's own wait: every caller
        /// that may queue behind slow work under the same key needs a wait long enough to outlast it.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="lockWait"/> is zero, negative, or longer than PostgreSQL allows.</exception>
        public static async Task<PostgresClusterLock> AcquireAsync(string connectionString, long key, TimeSpan? lockWait = null)
        {
            LockWaitTimeouts timeouts = LockWaitTimeouts.For(lockWait);

            NpgsqlConnection connection = new NpgsqlConnection(PostgresConnectionStrings.MaintenanceUnpooled(connectionString));
            await connection.OpenAsync();

            try
            {
                await using (NpgsqlCommand timeout = new NpgsqlCommand($"SET lock_timeout = '{timeouts.LockTimeoutSeconds}s'", connection))
                {
                    await timeout.ExecuteNonQueryAsync();
                }

                await using (NpgsqlCommand acquire = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection))
                {
                    acquire.CommandTimeout = timeouts.CommandTimeoutSeconds;
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

        /// <summary>
        /// The two timeouts one lock wait sets, both in whole seconds: PostgreSQL's <c>lock_timeout</c> for
        /// the session, and the acquire command's client-side <see cref="NpgsqlCommand.CommandTimeout"/>,
        /// which stays <see cref="CommandTimeoutMarginSeconds"/> longer.
        /// </summary>
        internal readonly record struct LockWaitTimeouts(int LockTimeoutSeconds, int CommandTimeoutSeconds)
        {
            /// <summary>
            /// The longest wait PostgreSQL accepts: <c>lock_timeout</c> is an int of milliseconds, about
            /// 24.8 days.
            /// </summary>
            internal const int MaxLockWaitSeconds = int.MaxValue / 1000;

            /// <summary>
            /// Derives both timeouts from <paramref name="lockWait"/> (null meaning
            /// <see cref="DefaultLockWait"/>), rounded UP to whole seconds: <c>CommandTimeout</c> is whole
            /// seconds, and truncating a sub-second wait would give a <c>lock_timeout</c> of zero, which
            /// PostgreSQL reads as no limit at all. Refuses an out-of-range wait here, before
            /// <see cref="AcquireAsync"/> opens a connection.
            /// </summary>
            internal static LockWaitTimeouts For(TimeSpan? lockWait)
            {
                TimeSpan wait = lockWait ?? DefaultLockWait;

                if (wait <= TimeSpan.Zero || wait.TotalSeconds > MaxLockWaitSeconds)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(lockWait),
                        wait,
                        $"The lock wait must be positive (PostgreSQL reads a lock_timeout of zero as waiting forever) and at most {MaxLockWaitSeconds} seconds (PostgreSQL's limit).");
                }

                int seconds = (int)Math.Ceiling(wait.TotalSeconds);
                return new LockWaitTimeouts(seconds, seconds + CommandTimeoutMarginSeconds);
            }
        }
    }
}
