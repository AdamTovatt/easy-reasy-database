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
        /// directly in <see cref="MaintenanceUnpooled_ForAnyConnectionString_IsUnpooledAndPointsAtTheMaintenanceDatabase"/>,
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
        /// The pooling decision, pinned where it is actually observable — the behaviour it prevents is
        /// Npgsql's, not ours, so it cannot be provoked through our own API once the explicit unlock is in
        /// place. An advisory lock belongs to the SESSION, and a pooled connection's session outlives the
        /// <c>NpgsqlConnection</c> that borrowed it (Npgsql defers its <c>DISCARD ALL</c> reset until the
        /// connection is next used), so a pooled lock holder returned to the pool would leave the lock held
        /// by an idle session.
        /// </summary>
        [Fact]
        public void MaintenanceUnpooled_ForAnyConnectionString_IsUnpooledAndPointsAtTheMaintenanceDatabase()
        {
            NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder(
                PostgresConnectionStrings.MaintenanceUnpooled(TestConnection.ConnectionString));

            Assert.False(builder.Pooling, "a pooled lock holder would leave the lock held by an idle session");
            Assert.Equal(PostgresConnectionStrings.MaintenanceDatabase, builder.Database);
        }

        /// <summary>
        /// The contract of the whole type: only the database changes. Host and credentials have to survive
        /// the rewrite, or a harness configured against a remote cluster would silently provision against
        /// whatever the defaults point at — most likely a local one.
        /// </summary>
        [Fact]
        public void WithDatabase_OverAFullyConfiguredConnectionString_ChangesOnlyTheDatabase()
        {
            const string configured = "Host=db.example.invalid;Port=5433;Database=configured;Username=someone;Password=secret;Timeout=17";

            NpgsqlConnectionStringBuilder rewritten = new NpgsqlConnectionStringBuilder(
                PostgresConnectionStrings.WithDatabase(configured, "example_test_67d7ea4a"));

            Assert.Equal("example_test_67d7ea4a", rewritten.Database);
            Assert.Equal("db.example.invalid", rewritten.Host);
            Assert.Equal(5433, rewritten.Port);
            Assert.Equal("someone", rewritten.Username);
            Assert.Equal("secret", rewritten.Password);
            Assert.Equal(17, rewritten.Timeout);
        }

        /// <inheritdoc cref="WithDatabase_OverAFullyConfiguredConnectionString_ChangesOnlyTheDatabase"/>
        [Fact]
        public void Maintenance_OverAFullyConfiguredConnectionString_KeepsEverythingButTheDatabase()
        {
            const string configured = "Host=db.example.invalid;Port=5433;Database=configured;Username=someone;Password=secret";

            NpgsqlConnectionStringBuilder rewritten = new NpgsqlConnectionStringBuilder(
                PostgresConnectionStrings.Maintenance(configured));

            Assert.Equal(PostgresConnectionStrings.MaintenanceDatabase, rewritten.Database);
            Assert.Equal("db.example.invalid", rewritten.Host);
            Assert.Equal(5433, rewritten.Port);
            Assert.Equal("someone", rewritten.Username);
            Assert.Equal("secret", rewritten.Password);
            Assert.True(rewritten.Pooling, "only MaintenanceUnpooled turns pooling off");
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
