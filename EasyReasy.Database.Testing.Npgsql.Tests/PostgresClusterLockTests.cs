using System.Globalization;
using Npgsql;

namespace EasyReasy.Database.Testing.Npgsql.Tests
{
    /// <summary>
    /// Guards the cluster-wide mutex a shared role bootstrap runs under. Its whole job is to be held by
    /// exactly one test run at a time, so the property worth asserting is that it excludes a second holder
    /// while held and genuinely frees it on dispose.
    /// </summary>
    public class PostgresClusterLockTests
    {
        /// <summary>
        /// A key of this test's own, so it never contends with a consumer's real bootstrap — and derived
        /// per checkout, because a fixed key would be exactly the cluster-wide coupling between concurrent
        /// runs that this library exists to remove. Negative, which puts it in a range the positive keys
        /// this library recommends to consumers can never occupy.
        /// <para>
        /// A property rather than a static field: computed in a field initializer, a repository root that
        /// could not be found would surface as a <c>TypeInitializationException</c> on every test in the
        /// class, burying the real message one level down.
        /// </para>
        /// </summary>
        private static long TestKey => -(long)uint.Parse(
            CheckoutDatabaseIdentity.CheckoutHash(
                RepositoryRoot.Find(AppContext.BaseDirectory, "EasyReasy.Database.sln")),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);

        /// <summary>
        /// The property the whole mutex rests on: after dispose, another session can take the lock.
        /// <para>
        /// Two independent mechanisms uphold it — the explicit unlock in <c>DisposeAsync</c> and the
        /// unpooled connection, either of which suffices — so this test fails only if BOTH are removed. It
        /// is deliberately not billed as a guard on the pooling fix specifically; that decision is pinned
        /// directly in <see cref="PostgresConnectionStringsTests.MaintenanceUnpooled_OverAFullyConfiguredConnectionString_IsBothUnpooledAndMaintenance"/>,
        /// where it is observable.
        /// </para>
        /// <para>
        /// Asserted from an unpooled observer, because a pooled one could be handed the holder's own
        /// session, where the lock is re-entrant and would look free when it is not.
        /// </para>
        /// </summary>
        [Fact]
        public async Task DisposeAsync_AfterAcquire_ReleasesTheLockForOtherSessions()
        {
            string connectionString = TestConnection.ConnectionString;

            await using (await PostgresClusterLock.AcquireAsync(connectionString, TestKey))
            {
                Assert.False(await TryAcquireFromAnotherSessionAsync(connectionString), "the lock should exclude another session while held");
            }

            Assert.True(await TryAcquireFromAnotherSessionAsync(connectionString), "the lock should be free once disposed");
        }

        /// <summary>
        /// Tries to take the same lock from an independent, unpooled session on the maintenance database —
        /// the same session scope a second test run would use. When it succeeds it holds the lock only
        /// until the session closes, which the unpooled disposal below does immediately.
        /// </summary>
        private static async Task<bool> TryAcquireFromAnotherSessionAsync(string connectionString)
        {
            await using NpgsqlConnection connection = new NpgsqlConnection(
                PostgresConnectionStrings.MaintenanceUnpooled(connectionString));
            await connection.OpenAsync();

            await using NpgsqlCommand command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", TestKey);

            return (bool)(await command.ExecuteScalarAsync())!;
        }
    }
}
