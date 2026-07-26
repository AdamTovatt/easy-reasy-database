using EasyReasy.Database.Testing;

namespace EasyReasy.Database.Tests.Testing
{
    /// <summary>
    /// Guards the per-checkout database naming, which is what keeps two worktrees' suites from destroying
    /// each other's schema. These run without a database.
    /// </summary>
    public class CheckoutDatabaseIdentityTests
    {
        private const string ExampleCheckout = "/home/dev/code/example";
        private const string ExampleHash = "67d7ea4a";
        private const string OtherCheckout = "/home/dev/code/example-worktree-copy";

        private static readonly CheckoutDatabaseIdentity Identity = new CheckoutDatabaseIdentity
        {
            DatabasePrefix = "example_test_",
            PinEnvironmentVariableName = "EXAMPLE_TEST_DATABASE_NAME",
            CheckoutCommentPrefix = "example-test-checkout:",
            RepositoryRootFileName = "EasyReasy.Database.sln",
        };

        /// <summary>
        /// Pins the hash for a fixed path. A consuming repository's non-.NET harness (a Playwright global
        /// setup, for example) mirrors the derivation with the same formula, so pinning the value here
        /// keeps the formula itself from drifting under such mirrors.
        /// </summary>
        [Fact]
        public void CheckoutHash_KnownPath_MatchesThePinnedValue()
        {
            Assert.Equal(ExampleHash, CheckoutDatabaseIdentity.CheckoutHash(ExampleCheckout));
        }

        [Fact]
        public void CheckoutHash_DifferentCheckoutPaths_Differ()
        {
            Assert.NotEqual(
                CheckoutDatabaseIdentity.CheckoutHash(ExampleCheckout),
                CheckoutDatabaseIdentity.CheckoutHash(OtherCheckout));
        }

        [Fact]
        public void CheckoutHash_TrailingSeparator_DoesNotChangeTheHash()
        {
            Assert.Equal(ExampleHash, CheckoutDatabaseIdentity.CheckoutHash(ExampleCheckout + "/"));
        }

        /// <summary>
        /// The case is part of the contract, not an incidental of the formatter: a pruning sweep only
        /// recognises lowercase hex, so a mirror emitting uppercase would create databases nothing can
        /// ever reclaim.
        /// </summary>
        [Fact]
        public void CheckoutHash_ForAnyCheckout_IsLowercaseHex()
        {
            string hash = CheckoutDatabaseIdentity.CheckoutHash(ExampleCheckout);

            Assert.Equal(CheckoutDatabaseIdentity.HashLength, hash.Length);
            Assert.All(hash, character => Assert.Contains(character, "0123456789abcdef"));
        }

        [Fact]
        public void ForCheckout_ForAKnownCheckout_ProducesThePrefixedHash()
        {
            Assert.Equal("example_test_" + ExampleHash, Identity.ForCheckout(ExampleCheckout));
        }

        [Fact]
        public void MarkerFor_ForAKnownCheckout_ProducesThePrefixedCheckoutPath()
        {
            Assert.Equal("example-test-checkout:" + ExampleCheckout, Identity.MarkerFor(ExampleCheckout));
        }

        [Fact]
        public void CurrentCheckoutRoot_FromTheTestBinaries_FindsTheDirectoryContainingTheRootFile()
        {
            Assert.True(File.Exists(Path.Combine(Identity.CurrentCheckoutRoot(), "EasyReasy.Database.sln")));
        }

        /// <summary>
        /// The decision behind the ownership stamp: only a derived database is claimed. Keyed on
        /// pinned-ness, NOT on the resulting name's shape — a pin that happens to look derived is still
        /// someone else's database, and stamping it would enrol it in a sweep that later deletes it.
        /// </summary>
        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData("example_test", true)]
        [InlineData(" example_test ", true)]
        [InlineData("example_test_67d7ea4a", true)]
        public void IsPinned_OverAssortedPinValues_KeysOnWhetherANameWasSuppliedNotOnItsShape(string? pinnedName, bool expected)
        {
            Assert.Equal(expected, CheckoutDatabaseIdentity.IsPinned(pinnedName));
        }

        [Fact]
        public void FindRepositoryRoot_OutsideAnyCheckout_ThrowsNamingTheOverride()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => Identity.FindRepositoryRoot(Path.GetTempPath()));

            Assert.Contains(Identity.PinEnvironmentVariableName, exception.Message);
        }

        /// <summary>The production path: nothing pinned, so the environment lookup hands in null.</summary>
        [Fact]
        public void ResolveDatabaseName_WithNothingPinned_DerivesFromTheCheckout()
        {
            Assert.Equal(
                Identity.ForCheckout(Identity.CurrentCheckoutRoot()),
                Identity.ResolveDatabaseName(null, AppContext.BaseDirectory));
        }

        [Fact]
        public void ResolveDatabaseName_WithABlankPin_DerivesFromTheCheckout()
        {
            Assert.Equal(
                Identity.ForCheckout(Identity.CurrentCheckoutRoot()),
                Identity.ResolveDatabaseName("   ", AppContext.BaseDirectory));
        }

        [Fact]
        public void ResolveDatabaseName_WithAPin_UsesItInsteadOfDeriving()
        {
            Assert.Equal("example_test", Identity.ResolveDatabaseName(" example_test ", AppContext.BaseDirectory));
        }

        /// <summary>
        /// A database name is interpolated into DDL, so a pin that is not a plain identifier has to fail
        /// at resolution rather than reach a statement. The message names the pin variable because that
        /// is the knob whoever hits this has to change.
        /// </summary>
        [Theory]
        [InlineData("example test")]
        [InlineData("example_test; DROP DATABASE example_dev")]
        [InlineData("example-test")]
        [InlineData("1_example_test")]
        [InlineData("\"example_test\"")]
        // PostgreSQL truncates rather than rejects an over-long identifier, so this must fail here.
        [InlineData("example_test_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        public void ResolveDatabaseName_WithAPinThatIsNotAPlainIdentifier_ThrowsNamingThePinVariable(string pinnedName)
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => Identity.ResolveDatabaseName(pinnedName, AppContext.BaseDirectory));

            Assert.Contains(Identity.PinEnvironmentVariableName, exception.Message);
        }

        /// <summary>
        /// The other way resolution can fail, which must not be reported as the first. Nothing is pinned
        /// here, so naming the pin variable would send whoever hits this to a knob that is not set and is
        /// not the cause — the prefix is.
        /// </summary>
        [Fact]
        public void ResolveDatabaseName_WithADerivedNameFromABadPrefix_ThrowsNamingThePrefixNotThePin()
        {
            CheckoutDatabaseIdentity badPrefix = new CheckoutDatabaseIdentity
            {
                DatabasePrefix = "example test ",
                PinEnvironmentVariableName = "EXAMPLE_TEST_DATABASE_NAME",
                CheckoutCommentPrefix = "example-test-checkout:",
                RepositoryRootFileName = "EasyReasy.Database.sln",
            };

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => badPrefix.ResolveDatabaseName(null, AppContext.BaseDirectory));

            Assert.Contains(nameof(CheckoutDatabaseIdentity.DatabasePrefix), exception.Message);
            Assert.DoesNotContain(badPrefix.PinEnvironmentVariableName, exception.Message);
        }

        /// <summary>
        /// The parameterless overloads, which are what production code actually calls. They differ from
        /// the testable ones by exactly one thing — the environment variable name they read — and that
        /// name is a string no compiler checks, so a wrong one would compile and pass everything else.
        /// <para>
        /// Uses a variable of this test's own, set and cleared around the assertion, so it cannot collide
        /// with a real harness's or with another test's.
        /// </para>
        /// </summary>
        [Fact]
        public void ResolveDatabaseNameAndIsPinned_WithThePinVariableSet_ReadTheVariableTheyName()
        {
            CheckoutDatabaseIdentity identity = new CheckoutDatabaseIdentity
            {
                DatabasePrefix = "example_test_",
                PinEnvironmentVariableName = "EASYREASY_PIN_PROBE_" + Guid.NewGuid().ToString("n")[..8],
                CheckoutCommentPrefix = "example-test-checkout:",
                RepositoryRootFileName = "EasyReasy.Database.sln",
            };

            Assert.False(identity.IsPinned(), "nothing is set yet, so the run is not pinned");
            Assert.Equal(identity.ForCheckout(identity.CurrentCheckoutRoot()), identity.ResolveDatabaseName());

            try
            {
                Environment.SetEnvironmentVariable(identity.PinEnvironmentVariableName, "example_pinned");

                Assert.True(identity.IsPinned(), "the pin variable is set, so the run is pinned");
                Assert.Equal("example_pinned", identity.ResolveDatabaseName());
            }
            finally
            {
                Environment.SetEnvironmentVariable(identity.PinEnvironmentVariableName, null);
            }

            Assert.False(identity.IsPinned(), "clearing the variable un-pins the run");
        }
    }
}
