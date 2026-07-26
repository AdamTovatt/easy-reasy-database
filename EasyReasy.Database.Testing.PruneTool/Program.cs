namespace EasyReasy.Database.Testing.PruneTool
{
    /// <summary>
    /// Entry point: parses the command line into <see cref="PruneOptions"/> and runs one sweep.
    /// </summary>
    public static class Program
    {
        private const string Usage = """
            prune-test-databases — reclaim the per-checkout test database and scratch tree of a gone checkout.

            Test harnesses using EasyReasy.Database.Testing.Npgsql derive a database per checkout and stamp it
            with an ownership marker recording the checkout path. Deleting a worktree leaves its database (and
            any scratch directory) behind; this reclaims both. Nothing is dropped unless BOTH hold:
              1. the name has the exact derived shape — the prefix followed by 8 lowercase hex characters;
              2. the database carries the ownership marker, and the checkout path it records no longer exists.

            Usage:
              prune-test-databases --prefix <database-prefix> --marker <marker-prefix> [options]        # list only
              prune-test-databases --prefix <database-prefix> --marker <marker-prefix> [options] --yes  # apply

            Options:
              --prefix               Derived-database name prefix, e.g. myproject_test_        (required)
              --marker               Ownership-marker prefix, e.g. myproject-test-checkout:    (required)
              --connection-string    PostgreSQL connection string
                                     (default: Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres)
              --scratch-root         Root of per-checkout scratch directories; repeatable
              --scratch-marker-file  Marker file name inside a scratch tree                    (default: checkout)
              --yes                  Perform the drops instead of only listing them
            """;

        /// <summary>Parses <paramref name="arguments"/> and runs the sweep.</summary>
        public static async Task<int> Main(string[] arguments)
        {
            return await RunAsync(arguments, Console.Out, Console.Error);
        }

        /// <summary>
        /// The whole program with its two console streams passed in, so a test can drive the real argument
        /// handling — the half a compiler cannot check — without a process and without a cluster: every
        /// parse failure returns before anything connects.
        /// </summary>
        /// <param name="arguments">The command line, without the executable name.</param>
        /// <param name="output">Where the report goes.</param>
        /// <param name="error">Where refusals go, kept off <paramref name="output"/> so a piped listing stays parseable.</param>
        /// <returns>The process exit code.</returns>
        public static async Task<int> RunAsync(string[] arguments, TextWriter output, TextWriter error)
        {
            (PruneOptions? options, int exitCode) = ParseArguments(arguments, output, error);

            if (options == null)
            {
                return exitCode;
            }

            return await new PruneRunner(options, output, error).RunAsync();
        }

        /// <summary>
        /// Turns the command line into options, or reports why it could not. A null options carries the
        /// exit code to return instead — including 0 for <c>--help</c>, which is a successful run that
        /// simply has no sweep to do.
        /// </summary>
        private static (PruneOptions? Options, int ExitCode) ParseArguments(string[] arguments, TextWriter output, TextWriter error)
        {
            string? connectionString = null;
            string? databasePrefix = null;
            string? markerPrefix = null;
            List<string> scratchRoots = [];
            string? scratchMarkerFileName = null;
            bool confirmed = false;

            // Each value-taking option is spelled exactly once, next to what it sets. A switch that named
            // them a second time to read the value could drift from this list without the compiler
            // noticing, and the two spellings are only ever compared at runtime.
            Dictionary<string, Action<string>> valueOptions = new Dictionary<string, Action<string>>(StringComparer.Ordinal)
            {
                ["--connection-string"] = value => connectionString = value,
                ["--prefix"] = value => databasePrefix = value,
                ["--marker"] = value => markerPrefix = value,
                ["--scratch-root"] = scratchRoots.Add,
                ["--scratch-marker-file"] = value => scratchMarkerFileName = value,
            };

            for (int index = 0; index < arguments.Length; index++)
            {
                string argument = arguments[index];

                if (argument is "-h" or "--help")
                {
                    output.WriteLine(Usage);
                    return (null, 0);
                }

                if (argument == "--yes")
                {
                    confirmed = true;
                    continue;
                }

                if (!valueOptions.TryGetValue(argument, out Action<string>? apply))
                {
                    error.WriteLine($"Unknown option: {argument}");
                    error.WriteLine(Usage);
                    return (null, 1);
                }

                if (index + 1 >= arguments.Length)
                {
                    error.WriteLine($"Error: {argument} requires a value.");
                    return (null, 1);
                }

                apply(arguments[++index]);
            }

            // Blank, not merely absent. An empty --marker would reach the sweep as a prefix that EVERY
            // comment starts with, so every stamped database — including every live one — would be read
            // as carrying this project's marker and judged on whether its comment names a live directory.
            if (string.IsNullOrWhiteSpace(databasePrefix) || string.IsNullOrWhiteSpace(markerPrefix))
            {
                error.WriteLine("Error: --prefix and --marker are required and cannot be blank.");
                error.WriteLine(Usage);
                return (null, 1);
            }

            PruneOptions options = new PruneOptions
            {
                ConnectionString = connectionString ?? PruneOptions.DefaultConnectionString,
                DatabasePrefix = databasePrefix,
                MarkerPrefix = markerPrefix,
                ScratchRoots = scratchRoots,
                ScratchMarkerFileName = scratchMarkerFileName ?? PruneOptions.DefaultScratchMarkerFileName,
                Confirmed = confirmed,
            };

            return (options, 0);
        }
    }
}
