using Npgsql;

namespace EasyReasy.Database.Testing.Npgsql.Tests
{
    /// <summary>
    /// Covers the write half of the decision a pruning sweep DELETES on: which databases carry an
    /// ownership marker. The stakes are asymmetric: failing to stamp a derived database only leaks it,
    /// but stamping one the harness does not own — CI's fixed database, or a developer's hand-made one —
    /// enrols someone else's database in a sweep that later deletes it.
    /// </summary>
    public class TestDatabaseProvisionerTests
    {
        private const string ExampleMarker = "example-test-checkout:/home/dev/code/example";

        [Fact]
        public async Task EnsureDatabaseExistsAsync_WithAMarker_CreatesTheDatabaseAndRecordsIt()
        {
            string databaseName = ThrowawayDatabaseName();

            try
            {
                await TestDatabaseProvisioner.EnsureDatabaseExistsAsync(TestCluster.ConnectionStringFor(databaseName), ExampleMarker);

                Assert.True(await TestCluster.DatabaseExistsAsync(databaseName), "the database must be created");
                Assert.Equal(ExampleMarker, await TestCluster.CommentOnAsync(databaseName));
            }
            finally
            {
                await TestCluster.DropDatabaseAsync(databaseName);
            }
        }

        /// <summary>
        /// The case that matters: a pinned name is still created, but never claimed — even when the name
        /// itself happens to look exactly like a derived one.
        /// </summary>
        [Fact]
        public async Task EnsureDatabaseExistsAsync_WithoutAMarker_CreatesTheDatabaseWithoutClaimingIt()
        {
            string databaseName = ThrowawayDatabaseName();

            try
            {
                await TestDatabaseProvisioner.EnsureDatabaseExistsAsync(TestCluster.ConnectionStringFor(databaseName), ownershipMarker: null);

                Assert.True(await TestCluster.DatabaseExistsAsync(databaseName), "the database must be created");
                Assert.Null(await TestCluster.CommentOnAsync(databaseName));
            }
            finally
            {
                await TestCluster.DropDatabaseAsync(databaseName);
            }
        }

        /// <summary>
        /// Re-running restamps rather than failing, so a database left behind by an interrupted run still
        /// ends up attributable. Note this exercises the ALREADY-EXISTS path, which short-circuits before
        /// the CREATE; the concurrent-create race handler is covered separately by
        /// <see cref="EnsureDatabaseExistsAsync_FromSeveralConcurrentRuns_CreatesItOnceWithoutFailing"/>.
        /// </summary>
        [Fact]
        public async Task EnsureDatabaseExistsAsync_OnADatabaseThatAlreadyExists_SkipsCreationAndRestamps()
        {
            string databaseName = ThrowawayDatabaseName();

            try
            {
                await TestDatabaseProvisioner.EnsureDatabaseExistsAsync(TestCluster.ConnectionStringFor(databaseName), ownershipMarker: null);
                Assert.Null(await TestCluster.CommentOnAsync(databaseName));

                await TestDatabaseProvisioner.EnsureDatabaseExistsAsync(TestCluster.ConnectionStringFor(databaseName), ExampleMarker);

                Assert.Equal(ExampleMarker, await TestCluster.CommentOnAsync(databaseName));
            }
            finally
            {
                await TestCluster.DropDatabaseAsync(databaseName);
            }
        }

        /// <summary>
        /// The race the catch block exists for, provoked rather than described. This path takes no lock —
        /// two checkouts that PIN the same database name reach it for real — so the losers must come back
        /// having got the outcome they wanted rather than throwing.
        /// <para>
        /// Several runs, not two: the check-then-CREATE window is small, so one pair might interleave
        /// harmlessly. Note that a run which happened not to overlap would still pass here — this proves
        /// the handler tolerates the race when it occurs, not that the race occurred on any given run.
        /// </para>
        /// </summary>
        [Fact]
        public async Task EnsureDatabaseExistsAsync_FromSeveralConcurrentRuns_CreatesItOnceWithoutFailing()
        {
            string databaseName = ThrowawayDatabaseName();

            try
            {
                await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
                    TestDatabaseProvisioner.EnsureDatabaseExistsAsync(TestCluster.ConnectionStringFor(databaseName), ExampleMarker)));

                Assert.True(await TestCluster.DatabaseExistsAsync(databaseName), "the concurrent runs must converge on one created database");
                Assert.Equal(ExampleMarker, await TestCluster.CommentOnAsync(databaseName));
            }
            finally
            {
                await TestCluster.DropDatabaseAsync(databaseName);
            }
        }

        /// <summary>
        /// A marker containing a single quote must be escaped into the COMMENT literal, not break out of
        /// it — checkout paths are attacker-ish input in the sense that any path is possible.
        /// </summary>
        [Fact]
        public async Task EnsureDatabaseExistsAsync_WithAQuoteInTheMarker_EscapesItIntoTheComment()
        {
            string databaseName = ThrowawayDatabaseName();
            string marker = "example-test-checkout:/home/dev/code/o'brien";

            try
            {
                await TestDatabaseProvisioner.EnsureDatabaseExistsAsync(TestCluster.ConnectionStringFor(databaseName), marker);

                Assert.Equal(marker, await TestCluster.CommentOnAsync(databaseName));
            }
            finally
            {
                await TestCluster.DropDatabaseAsync(databaseName);
            }
        }

        /// <summary>
        /// The database name is interpolated into <c>CREATE DATABASE</c>, so the provisioner must refuse a
        /// non-plain identifier itself rather than trust every caller to have validated it.
        /// </summary>
        [Fact]
        public async Task EnsureDatabaseExistsAsync_WithANonPlainDatabaseName_ThrowsWithoutConnecting()
        {
            string connectionString = new NpgsqlConnectionStringBuilder(TestConnection.ConnectionString)
            {
                Database = "bad-name; DROP DATABASE postgres",
            }.ConnectionString;

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => TestDatabaseProvisioner.EnsureDatabaseExistsAsync(connectionString, ownershipMarker: null));
        }

        /// <summary>A derived-looking name of this test's own, so it can never touch a real one.</summary>
        private static string ThrowawayDatabaseName()
        {
            return string.Concat("easyreasy_prov_test_", Guid.NewGuid().ToString("n").AsSpan(0, 8));
        }
    }
}
