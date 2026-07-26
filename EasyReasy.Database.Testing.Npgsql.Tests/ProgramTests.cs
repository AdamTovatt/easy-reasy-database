using EasyReasy.Database.Testing.PruneTool;

namespace EasyReasy.Database.Testing.Npgsql.Tests
{
    /// <summary>
    /// The command line itself. Option spellings are strings the compiler never checks, so a typo in one
    /// would ship as an option that silently does nothing — and for a tool whose whole job is destructive,
    /// an ignored <c>--prefix</c> or a misread <c>--yes</c> is the worst kind of quiet.
    /// <para>
    /// Every case here fails before the tool connects, so none of them needs a cluster; the connection
    /// string they would have used is deliberately unreachable, so a run that got further would fail
    /// loudly rather than pass while proving nothing.
    /// </para>
    /// </summary>
    public class ProgramTests
    {
        private const string Unreachable = "Host=nowhere.invalid;Username=postgres;Password=none";

        [Fact]
        public async Task RunAsync_WithHelp_PrintsUsageToOutputAndSucceeds()
        {
            StringWriter output = new StringWriter();
            StringWriter error = new StringWriter();

            int exitCode = await Program.RunAsync(["--help"], output, error);

            Assert.Equal(0, exitCode);
            Assert.Contains("prune-test-databases", output.ToString());
            Assert.Equal("", error.ToString());
        }

        [Fact]
        public async Task RunAsync_WithNoArguments_ReportsTheRequiredOptionsToError()
        {
            StringWriter output = new StringWriter();
            StringWriter error = new StringWriter();

            int exitCode = await Program.RunAsync([], output, error);

            Assert.Equal(1, exitCode);
            Assert.Contains("--prefix", error.ToString());
            Assert.Contains("--marker", error.ToString());
            Assert.Equal("", output.ToString());
        }

        /// <summary>
        /// The finding this test exists for: <c>--marker ""</c> parses as a supplied value, so a
        /// required-options check that only tested for ABSENCE would let an empty marker through — and an
        /// empty marker prefixes every database comment in the cluster.
        /// </summary>
        [Theory]
        [InlineData("--marker", "")]
        [InlineData("--marker", "   ")]
        [InlineData("--prefix", "")]
        [InlineData("--prefix", "   ")]
        public async Task RunAsync_WithARequiredOptionSuppliedBlank_IsRefusedLikeAMissingOne(string option, string blankValue)
        {
            StringWriter output = new StringWriter();
            StringWriter error = new StringWriter();

            string[] arguments = option == "--marker"
                ? ["--prefix", "example_test_", "--marker", blankValue, "--connection-string", Unreachable, "--yes"]
                : ["--prefix", blankValue, "--marker", "example-test-checkout:", "--connection-string", Unreachable, "--yes"];

            int exitCode = await Program.RunAsync(arguments, output, error);

            Assert.Equal(1, exitCode);
            Assert.Contains("cannot be blank", error.ToString());
            Assert.Equal("", output.ToString());
        }

        [Theory]
        [InlineData("--prefix")]
        [InlineData("--marker")]
        [InlineData("--connection-string")]
        [InlineData("--scratch-root")]
        [InlineData("--scratch-marker-file")]
        public async Task RunAsync_WithAValueOptionAtTheEndOfTheCommandLine_ReportsTheMissingValue(string option)
        {
            StringWriter output = new StringWriter();
            StringWriter error = new StringWriter();

            int exitCode = await Program.RunAsync([option], output, error);

            Assert.Equal(1, exitCode);
            Assert.Contains($"{option} requires a value", error.ToString());
        }

        /// <summary>
        /// An unrecognised option is refused rather than ignored: silently skipping a mistyped
        /// <c>--scratch-root</c> would run the sweep with less scope than the operator asked for, and
        /// skipping a mistyped <c>--marker</c> would be worse.
        /// </summary>
        [Fact]
        public async Task RunAsync_WithAnUnknownOption_RefusesInsteadOfIgnoringIt()
        {
            StringWriter output = new StringWriter();
            StringWriter error = new StringWriter();

            int exitCode = await Program.RunAsync(
                ["--prefix", "example_test_", "--marker", "example-test-checkout:", "--dry-run"], output, error);

            Assert.Equal(1, exitCode);
            Assert.Contains("Unknown option: --dry-run", error.ToString());
        }

        /// <summary>
        /// Every option reaches the field it names. Driven through a deliberately non-plain prefix so the
        /// run stops at the runner's own guard — which proves the parse completed and handed over real
        /// options, without needing a cluster to prove it against.
        /// </summary>
        [Fact]
        public async Task RunAsync_WithEveryOptionSupplied_ParsesThemAllBeforeTheSweep()
        {
            StringWriter output = new StringWriter();
            StringWriter error = new StringWriter();

            int exitCode = await Program.RunAsync(
                [
                    "--prefix", "not a valid prefix",
                    "--marker", "example-test-checkout:",
                    "--connection-string", Unreachable,
                    "--scratch-root", "/tmp/example-e2e",
                    "--scratch-root", "/tmp/example-other",
                    "--scratch-marker-file", "owner",
                    "--yes",
                ],
                output,
                error);

            Assert.Equal(1, exitCode);
            Assert.Contains("not a valid prefix", error.ToString());
        }
    }
}
