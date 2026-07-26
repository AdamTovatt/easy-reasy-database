using EasyReasy.Database.Sql;

namespace EasyReasy.Database.Tests.Sql
{
    /// <summary>
    /// Guards the shared guard: every identifier that gets interpolated into SQL — database, role and
    /// table names — is checked here first, because none of them can be passed as a parameter.
    /// </summary>
    public class SqlIdentifierTests
    {
        [Theory]
        [InlineData("example_test")]
        [InlineData("example_test_67d7ea4a")]
        [InlineData("_leading_underscore")]
        [InlineData("Mixed_Case_9")]
        public void IsPlain_PlainIdentifiers_AreAccepted(string value)
        {
            Assert.True(SqlIdentifier.IsPlain(value));
        }

        [Theory]
        [InlineData("")]
        [InlineData("example test")]
        [InlineData("example-test")]
        [InlineData("1_leading_digit")]
        [InlineData("\"quoted\"")]
        [InlineData("example_test; DROP DATABASE example_dev")]
        [InlineData("example_test--comment")]
        [InlineData("example_test'")]
        public void IsPlain_AnythingNeedingQuotingOrCarryingSql_IsRejected(string value)
        {
            Assert.False(SqlIdentifier.IsPlain(value));
        }

        /// <summary>
        /// The anchoring, pinned where it is easy to get wrong: in .NET <c>$</c> also matches immediately
        /// before a TRAILING newline, so a <c>^…$</c> check would accept a name with a newline still on
        /// the end and interpolate it into a statement. Only <c>\z</c> rejects it.
        /// </summary>
        [Theory]
        [InlineData("example_test\n")]
        [InlineData("example_test\r\n")]
        [InlineData("\nexample_test")]
        [InlineData("example_test\nDROP DATABASE example_dev")]
        public void IsPlain_AnythingCarryingANewline_IsRejected(string value)
        {
            Assert.False(SqlIdentifier.IsPlain(value));
        }

        /// <summary>
        /// PostgreSQL truncates an over-long identifier rather than rejecting it, so a name one character
        /// too long would create a database under a different spelling and then fail to connect to the one
        /// that was asked for.
        /// </summary>
        [Fact]
        public void IsPlain_AtAndOverThePostgresLengthLimit_AcceptsOnlyWhatSurvivesWhole()
        {
            string atLimit = new string('a', SqlIdentifier.MaximumLength);
            string overLimit = new string('a', SqlIdentifier.MaximumLength + 1);

            Assert.True(SqlIdentifier.IsPlain(atLimit), "a name at the limit survives whole");
            Assert.False(SqlIdentifier.IsPlain(overLimit), "a name over the limit would be silently truncated");
        }

        [Fact]
        public void RequirePlain_WithAPlainIdentifier_DoesNotThrow()
        {
            SqlIdentifier.RequirePlain("example_test", "Test database name", "provisioning");
        }

        /// <summary>
        /// The refusal has to say which value was rejected and what it stopped, since it surfaces to
        /// whoever configured the name rather than to a caller that can recover from it.
        /// </summary>
        [Fact]
        public void RequirePlain_WithANonPlainIdentifier_ThrowsNamingTheValueAndTheAbortedAction()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => SqlIdentifier.RequirePlain("bad name", "Test database name", "provisioning"));

            Assert.Contains("bad name", exception.Message);
            Assert.Contains("Test database name", exception.Message);
            Assert.Contains("provisioning aborted", exception.Message);
        }
    }
}
