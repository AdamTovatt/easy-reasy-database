using EasyReasy.Database.Testing.PruneTool;

namespace EasyReasy.Database.Testing.Npgsql.Tests
{
    /// <summary>
    /// End-to-end sweep against the real cluster, using a prefix and marker of this test's own so it can
    /// never touch any real project's databases. Covers the read half of the pruning decision (which
    /// stamped databases the sweep selects) — the write half (who gets stamped at all) is covered by
    /// <see cref="TestDatabaseProvisionerTests"/>.
    /// </summary>
    public class PruneRunnerTests : IAsyncLifetime
    {
        private const string Marker = "easyreasy-prune-test-checkout:";

        // Per run, not fixed. A sweep is defined by its prefix, so two runs sharing one would each see the
        // other's fixtures as candidates — and a confirmed run would drop the very databases the other
        // run's dry-run test is about to assert are still there.
        private readonly string _prefix = "easyreasy_prune_test_" + Guid.NewGuid().ToString("n")[..8] + "_";

        private readonly string _liveDatabase;
        private readonly string _orphanDatabase;
        private readonly string _pinnedLookalikeDatabase;

        private string _liveCheckout = null!;
        private string _goneCheckout = null!;
        private string _scratchRoot = null!;

        public PruneRunnerTests()
        {
            _liveDatabase = _prefix + "aaaa1111";
            _orphanDatabase = _prefix + "bbbb2222";
            _pinnedLookalikeDatabase = _prefix + "cccc3333";
        }

        public async Task InitializeAsync()
        {
            _liveCheckout = Directory.CreateTempSubdirectory("easyreasy-prune-live-").FullName;
            _goneCheckout = Path.Combine(Path.GetTempPath(), "easyreasy-prune-gone-" + Guid.NewGuid().ToString("n"));
            _scratchRoot = Directory.CreateTempSubdirectory("easyreasy-prune-scratch-").FullName;

            // A live checkout's database, an orphan's database, and an unstamped database whose name
            // merely looks derived — the sweep must drop exactly the middle one.
            await TestDatabaseProvisioner.EnsureDatabaseExistsAsync(TestCluster.ConnectionStringFor(_liveDatabase), Marker + _liveCheckout);
            await TestDatabaseProvisioner.EnsureDatabaseExistsAsync(TestCluster.ConnectionStringFor(_orphanDatabase), Marker + _goneCheckout);
            await TestDatabaseProvisioner.EnsureDatabaseExistsAsync(TestCluster.ConnectionStringFor(_pinnedLookalikeDatabase), ownershipMarker: null);

            // Four scratch trees, one per way the sweep can meet one: the orphan's own tree (under its
            // hash, carrying no marker — the database drop is what reaches it), a marker-carrying orphan
            // tree no database row points at, a live checkout's tree, and an unmarked tree no database
            // drop reaches. The last must survive: with nothing to attribute it to, the sweep has no
            // grounds to delete it.
            Directory.CreateDirectory(Path.Combine(_scratchRoot, "bbbb2222"));
            WriteScratchTree("dddd4444", Marker + _goneCheckout);
            WriteScratchTree("eeee5555", Marker + _liveCheckout);
            Directory.CreateDirectory(Path.Combine(_scratchRoot, "ffff6666"));
        }

        public async Task DisposeAsync()
        {
            try
            {
                await TestCluster.DropDatabaseAsync(_liveDatabase);
                await TestCluster.DropDatabaseAsync(_orphanDatabase);
                await TestCluster.DropDatabaseAsync(_pinnedLookalikeDatabase);
            }
            finally
            {
                // In a finally, so a cluster that refuses a drop cannot also leak the temp trees.
                DeleteIfPresent(_liveCheckout);
                DeleteIfPresent(_scratchRoot);
            }
        }

        [Fact]
        public async Task RunAsync_WithoutYes_ListsTheOrphanButDropsNothing()
        {
            StringWriter output = new StringWriter();

            int exitCode = await new PruneRunner(Options(confirmed: false), output, new StringWriter()).RunAsync();

            Assert.Equal(0, exitCode);
            Assert.Contains($"drop  {_orphanDatabase}", output.ToString());
            Assert.Contains($"keep  {_liveDatabase}", output.ToString());
            Assert.True(await TestCluster.DatabaseExistsAsync(_orphanDatabase), "a dry run must not drop anything");
            Assert.True(Directory.Exists(Path.Combine(_scratchRoot, "dddd4444")), "a dry run must not remove scratch trees");
        }

        [Fact]
        public async Task RunAsync_WithYes_DropsExactlyTheOrphanAndItsScratch()
        {
            StringWriter output = new StringWriter();

            int exitCode = await new PruneRunner(Options(confirmed: true), output, new StringWriter()).RunAsync();

            Assert.Equal(0, exitCode);

            // The orphan's database and both orphaned scratch trees are gone.
            Assert.False(await TestCluster.DatabaseExistsAsync(_orphanDatabase), "the orphan's checkout is gone, so its database must be dropped");
            Assert.False(Directory.Exists(Path.Combine(_scratchRoot, "bbbb2222")), "the orphan's own tree goes with its database");
            Assert.False(Directory.Exists(Path.Combine(_scratchRoot, "dddd4444")), "a marked tree whose checkout is gone must be swept");

            // The live checkout's database and tree, the unstamped lookalike, and the unattributable tree
            // all survive.
            Assert.True(await TestCluster.DatabaseExistsAsync(_liveDatabase), "the live checkout's database must survive");
            Assert.True(await TestCluster.DatabaseExistsAsync(_pinnedLookalikeDatabase), "an unstamped database is someone else's, whatever its name looks like");
            Assert.True(Directory.Exists(Path.Combine(_scratchRoot, "eeee5555")), "the live checkout's tree must survive");
            Assert.True(Directory.Exists(Path.Combine(_scratchRoot, "ffff6666")), "an unmarked tree with no database to attribute it to must survive");
        }

        /// <summary>
        /// The one branch that exits non-zero on a bad prefix. Checked with the hash appended, because
        /// that is the shape the sweep matches; the refusal goes to the ERROR stream so a listing piped
        /// somewhere is not polluted by it.
        /// </summary>
        [Fact]
        public async Task RunAsync_WithAPrefixThatCannotFormAnIdentifier_ExitsNonZeroWithoutSweeping()
        {
            StringWriter output = new StringWriter();
            StringWriter error = new StringWriter();

            PruneOptions options = new PruneOptions
            {
                // Unreachable on purpose: a run that got as far as connecting would fail this test loudly
                // rather than quietly proving nothing.
                ConnectionString = "Host=nowhere.invalid;Username=postgres;Password=none",
                DatabasePrefix = "bad prefix; DROP DATABASE postgres",
                MarkerPrefix = Marker,
            };

            int exitCode = await new PruneRunner(options, output, error).RunAsync();

            Assert.Equal(1, exitCode);
            Assert.Contains("bad prefix", error.ToString());
            Assert.Equal("", output.ToString());
        }

        /// <summary>
        /// An empty marker prefix is a prefix of EVERY database comment, so a sweep that accepted one
        /// would read every stamped database in the cluster as this project's. The CLI rejects it too;
        /// this pins the guard that protects any other caller.
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task RunAsync_WithABlankMarkerPrefix_ExitsNonZeroWithoutSweeping(string markerPrefix)
        {
            StringWriter output = new StringWriter();
            StringWriter error = new StringWriter();

            PruneOptions options = new PruneOptions
            {
                ConnectionString = "Host=nowhere.invalid;Username=postgres;Password=none",
                DatabasePrefix = _prefix,
                MarkerPrefix = markerPrefix,
            };

            int exitCode = await new PruneRunner(options, output, error).RunAsync();

            Assert.Equal(1, exitCode);
            Assert.Contains("marker", error.ToString());
            Assert.Equal("", output.ToString());
        }

        /// <summary>
        /// A scratch root that does not exist is the normal state of a project that keeps no scratch
        /// trees, or of one whose e2e suite has not run yet — it is skipped, not an error.
        /// </summary>
        [Fact]
        public async Task RunAsync_WithAScratchRootThatDoesNotExist_SkipsItWithoutFailing()
        {
            StringWriter output = new StringWriter();
            string missingRoot = Path.Combine(Path.GetTempPath(), "easyreasy-prune-absent-" + Guid.NewGuid().ToString("n"));

            PruneOptions options = new PruneOptions
            {
                ConnectionString = TestConnection.ConnectionString,
                DatabasePrefix = _prefix,
                MarkerPrefix = Marker,
                ScratchRoots = [missingRoot],
                Confirmed = false,
            };

            int exitCode = await new PruneRunner(options, output, new StringWriter()).RunAsync();

            Assert.Equal(0, exitCode);
            Assert.Contains("No orphaned scratch directories.", output.ToString());
        }

        /// <summary>
        /// A tree whose marker names a checkout that is still there survives even a CONFIRMED sweep — the
        /// marker is what attributes a tree, not what condemns it.
        /// </summary>
        [Fact]
        public async Task RunAsync_WithAScratchTreeWhoseCheckoutStillExists_LeavesItAlone()
        {
            StringWriter output = new StringWriter();

            int exitCode = await new PruneRunner(Options(confirmed: true), output, new StringWriter()).RunAsync();

            Assert.Equal(0, exitCode);
            Assert.True(Directory.Exists(Path.Combine(_scratchRoot, "eeee5555")), "a tree whose checkout is live must survive a confirmed sweep");
            Assert.DoesNotContain("eeee5555", output.ToString());
        }

        private PruneOptions Options(bool confirmed)
        {
            return new PruneOptions
            {
                ConnectionString = TestConnection.ConnectionString,
                DatabasePrefix = _prefix,
                MarkerPrefix = Marker,
                ScratchRoots = [_scratchRoot],
                Confirmed = confirmed,
            };
        }

        private void WriteScratchTree(string hash, string markerContent)
        {
            string tree = Path.Combine(_scratchRoot, hash);
            Directory.CreateDirectory(tree);
            File.WriteAllText(Path.Combine(tree, PruneOptions.DefaultScratchMarkerFileName), markerContent);
        }

        private static void DeleteIfPresent(string directory)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
