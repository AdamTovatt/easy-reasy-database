using System.Diagnostics;
using System.Globalization;
using Npgsql;

namespace EasyReasy.Database.Testing.Npgsql.Tests
{
    /// <summary>
    /// Guards the cluster-wide mutex a shared role bootstrap runs under. Its whole job is to be held by
    /// exactly one test run at a time, so the properties worth asserting are that it excludes a second
    /// holder while held and genuinely frees it on dispose, and that a waiter gives up after the wait it
    /// asked for.
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
        /// One helper derives both timeouts from the caller's wait, so they cannot drift apart: the
        /// client-side command timeout stays 30 seconds past the server-side lock timeout.
        /// </summary>
        [Fact]
        public void LockWaitTimeouts_ForAGivenWait_SetsLockTimeoutAndACommandTimeoutThirtySecondsLonger()
        {
            Assert.Equal(new PostgresClusterLock.LockWaitTimeouts(300, 330), PostgresClusterLock.LockWaitTimeouts.For(TimeSpan.FromSeconds(300)));
        }

        /// <summary>
        /// With no wait given, the default is 60 seconds.
        /// </summary>
        [Fact]
        public void LockWaitTimeouts_ForNoWait_DefaultsToSixtySeconds()
        {
            Assert.Equal(new PostgresClusterLock.LockWaitTimeouts(60, 90), PostgresClusterLock.LockWaitTimeouts.For(null));
        }

        /// <summary>
        /// A sub-second wait rounds up rather than truncating to a <c>lock_timeout</c> of zero, which
        /// PostgreSQL reads as no limit.
        /// </summary>
        [Fact]
        public void LockWaitTimeouts_ForASubSecondWait_RoundsUpRatherThanToNoLimit()
        {
            Assert.Equal(new PostgresClusterLock.LockWaitTimeouts(1, 31), PostgresClusterLock.LockWaitTimeouts.For(TimeSpan.FromMilliseconds(200)));
        }

        /// <summary>
        /// The longest wait PostgreSQL accepts is still accepted, so the bound below is not off by one.
        /// </summary>
        [Fact]
        public void LockWaitTimeouts_ForTheLongestWaitPostgresAccepts_IsAccepted()
        {
            int max = PostgresClusterLock.LockWaitTimeouts.MaxLockWaitSeconds;
            Assert.Equal(new PostgresClusterLock.LockWaitTimeouts(max, max + 30), PostgresClusterLock.LockWaitTimeouts.For(TimeSpan.FromSeconds(max)));
        }

        /// <summary>
        /// A wait PostgreSQL cannot represent is refused before any connection opens: zero or negative
        /// (a zero <c>lock_timeout</c> means wait forever), or longer than <c>lock_timeout</c>'s int of
        /// milliseconds. An unreachable host proves the refusal came first, since connecting would fail
        /// with a different exception.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(PostgresClusterLock.LockWaitTimeouts.MaxLockWaitSeconds + 1)]
        public async Task AcquireAsync_WithAnOutOfRangeLockWait_ThrowsWithoutConnecting(int seconds)
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => PostgresClusterLock.AcquireAsync(UnreachableConnectionString, TestKey, TimeSpan.FromSeconds(seconds)));
        }

        /// <inheritdoc cref="AcquireAsync_WithAnOutOfRangeLockWait_ThrowsWithoutConnecting"/>
        [Fact]
        public async Task AcquireAsync_WithTimeSpanMaxValue_ThrowsWithoutConnecting()
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => PostgresClusterLock.AcquireAsync(UnreachableConnectionString, TestKey, TimeSpan.MaxValue));
        }

        private const string UnreachableConnectionString = "Host=nowhere.invalid;Username=postgres;Password=none";

        /// <summary>
        /// The configured wait reaches the server: with the key held elsewhere, a one-second wait fails
        /// with PostgreSQL's own lock-timeout error long before the 60 second default would.
        /// </summary>
        [Fact]
        public async Task AcquireAsync_WhileAnotherSessionHoldsTheKey_FailsWithLockTimeoutAfterTheGivenWait()
        {
            string connectionString = TestConnection.ConnectionString;
            await using PostgresClusterLock holder = await PostgresClusterLock.AcquireAsync(connectionString, TestKey);

            Stopwatch stopwatch = Stopwatch.StartNew();
            PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
                () => PostgresClusterLock.AcquireAsync(connectionString, TestKey, TimeSpan.FromSeconds(1)));
            stopwatch.Stop();

            Assert.Equal(PostgresErrorCodes.LockNotAvailable, exception.SqlState);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"a one-second wait took {stopwatch.Elapsed}");
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
