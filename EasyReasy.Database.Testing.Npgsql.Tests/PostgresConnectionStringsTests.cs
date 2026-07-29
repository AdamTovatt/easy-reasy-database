using Npgsql;

namespace EasyReasy.Database.Testing.Npgsql.Tests
{
    /// <summary>
    /// The derivations every other part of the harness is pointed by. What they all have in common is the
    /// thing worth asserting: whatever else changes, the HOST and CREDENTIALS survive. A rewrite that lost
    /// them would not fail — it would quietly succeed against whatever the defaults resolve to, which on a
    /// developer's machine is a local cluster and on a runner is nothing at all.
    /// <para>
    /// These lived in <see cref="PostgresClusterLockTests"/> while the lock was their only consumer. They
    /// are here now because it no longer is.
    /// </para>
    /// </summary>
    public class PostgresConnectionStringsTests
    {
        private const string Configured =
            "Host=db.example.invalid;Port=5433;Database=configured;Username=someone;Password=secret;Timeout=17";

        [Fact]
        public void WithDatabase_OverAFullyConfiguredConnectionString_ChangesOnlyTheDatabase()
        {
            NpgsqlConnectionStringBuilder rewritten = new NpgsqlConnectionStringBuilder(
                PostgresConnectionStrings.WithDatabase(Configured, "example_test_67d7ea4a"));

            Assert.Equal("example_test_67d7ea4a", rewritten.Database);
            AssertHostAndCredentialsSurvived(rewritten);
            Assert.Equal(17, rewritten.Timeout);
            Assert.True(rewritten.Pooling, "only the unpooled derivations turn pooling off");
        }

        /// <inheritdoc cref="WithDatabase_OverAFullyConfiguredConnectionString_ChangesOnlyTheDatabase"/>
        [Fact]
        public void Maintenance_OverAFullyConfiguredConnectionString_KeepsEverythingButTheDatabase()
        {
            NpgsqlConnectionStringBuilder rewritten = new NpgsqlConnectionStringBuilder(
                PostgresConnectionStrings.Maintenance(Configured));

            Assert.Equal(PostgresConnectionStrings.MaintenanceDatabase, rewritten.Database);
            AssertHostAndCredentialsSurvived(rewritten);
            Assert.True(rewritten.Pooling, "only the unpooled derivations turn pooling off");
        }

        /// <summary>
        /// The one axis this varies, and nothing else — in particular NOT the database, which is what makes
        /// it composable with the other two rather than a second maintenance derivation.
        /// </summary>
        [Fact]
        public void Unpooled_OverAFullyConfiguredConnectionString_ChangesOnlyPooling()
        {
            NpgsqlConnectionStringBuilder rewritten = new NpgsqlConnectionStringBuilder(
                PostgresConnectionStrings.Unpooled(Configured));

            Assert.False(rewritten.Pooling);
            Assert.Equal("configured", rewritten.Database);
            AssertHostAndCredentialsSurvived(rewritten);
        }

        /// <summary>
        /// The composition of the two above, asserted end to end rather than trusted. Now that this is a
        /// two-hop rewrite, a mistake in either hop — or in the order of them — would show up only here.
        /// <para>
        /// The pooling half is what an advisory lock rests on: a lock belongs to the SESSION, and a pooled
        /// connection's session outlives the <c>NpgsqlConnection</c> that borrowed it, so a lock holder
        /// returned to the pool would leave the lock held by an idle session.
        /// </para>
        /// </summary>
        [Fact]
        public void MaintenanceUnpooled_OverAFullyConfiguredConnectionString_IsBothUnpooledAndMaintenance()
        {
            NpgsqlConnectionStringBuilder rewritten = new NpgsqlConnectionStringBuilder(
                PostgresConnectionStrings.MaintenanceUnpooled(Configured));

            Assert.False(rewritten.Pooling, "a pooled lock holder would leave the lock held by an idle session");
            Assert.Equal(PostgresConnectionStrings.MaintenanceDatabase, rewritten.Database);
            AssertHostAndCredentialsSurvived(rewritten);
        }

        private static void AssertHostAndCredentialsSurvived(NpgsqlConnectionStringBuilder rewritten)
        {
            Assert.Equal("db.example.invalid", rewritten.Host);
            Assert.Equal(5433, rewritten.Port);
            Assert.Equal("someone", rewritten.Username);
            Assert.Equal("secret", rewritten.Password);
        }
    }
}
