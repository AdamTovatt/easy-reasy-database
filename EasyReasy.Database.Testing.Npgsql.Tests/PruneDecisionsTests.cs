using EasyReasy.Database.Testing.PruneTool;

namespace EasyReasy.Database.Testing.Npgsql.Tests
{
    /// <summary>
    /// Guards the rules the destructive sweep decides on. The stakes are asymmetric: missing an orphan
    /// only leaks a database, but a rule that matches too widely drops a database someone still owns.
    /// </summary>
    public class PruneDecisionsTests
    {
        [Theory]
        [InlineData("example_test_67d7ea4a", true)]
        [InlineData("example_test_00000000", true)]
        // Hand-made names never qualify, whatever the database contains.
        [InlineData("example_test_1418", false)]
        [InlineData("example_test", false)]
        [InlineData("example_test_", false)]
        // Uppercase hex is not what the derivation emits.
        [InlineData("example_test_67D7EA4A", false)]
        // Nothing may trail the hash.
        [InlineData("example_test_67d7ea4a_copy", false)]
        // A different prefix is a different project's database.
        [InlineData("other_test_67d7ea4a", false)]
        public void HasDerivedShape_OverNamesAroundTheConvention_AcceptsExactlyThePrefixPlusEightLowercaseHex(string name, bool expected)
        {
            Assert.Equal(expected, PruneDecisions.HasDerivedShape(name, "example_test_"));
        }

        /// <summary>
        /// The prefix is caller input, so regex metacharacters in it must be matched literally, not
        /// interpreted — a dot that matched any character would widen the destructive rule.
        /// </summary>
        [Fact]
        public void HasDerivedShape_WithRegexMetacharactersInThePrefix_MatchesThemLiterally()
        {
            Assert.True(PruneDecisions.HasDerivedShape("a.b_67d7ea4a", "a.b_"));
            Assert.False(PruneDecisions.HasDerivedShape("axb_67d7ea4a", "a.b_"));
        }

        [Theory]
        [InlineData("example-test-checkout:/home/dev/code/example", "/home/dev/code/example")]
        // No comment, a foreign comment, or a marker with no path all mean: not ours, keep out.
        [InlineData(null, null)]
        [InlineData("some other comment", null)]
        [InlineData("other-project-checkout:/home/dev/code/example", null)]
        [InlineData("example-test-checkout:", null)]
        public void TryGetCheckoutPath_OverAssortedComments_ExtractsThePathOnlyFromThisProjectsMarker(string? comment, string? expected)
        {
            Assert.Equal(expected, PruneDecisions.TryGetCheckoutPath(comment, "example-test-checkout:"));
        }

        [Fact]
        public void ScratchDirectoryFor_ForADerivedDatabase_HangsOffTheCheckoutHashItsNameEndsIn()
        {
            Assert.Equal(
                Path.Combine("/tmp/example-e2e", "67d7ea4a"),
                PruneDecisions.ScratchDirectoryFor("example_test_67d7ea4a", "example_test_", "/tmp/example-e2e"));
        }
    }
}
