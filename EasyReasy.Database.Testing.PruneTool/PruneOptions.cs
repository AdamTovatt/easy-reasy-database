namespace EasyReasy.Database.Testing.PruneTool
{
    /// <summary>
    /// What one prune run operates on. The prefix and marker are per-project conventions (the same values
    /// the project's test harness derives and stamps with), so the tool takes them explicitly rather than
    /// assuming any one project's spelling.
    /// </summary>
    public sealed class PruneOptions
    {
        /// <summary>A local development cluster, used when no connection string is given.</summary>
        public const string DefaultConnectionString = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

        /// <summary>The marker file name used when none is given.</summary>
        public const string DefaultScratchMarkerFileName = "checkout";

        /// <summary>
        /// Connection string to the cluster, pointed at any database (the maintenance database is used
        /// regardless).
        /// </summary>
        public string ConnectionString { get; init; } = DefaultConnectionString;

        /// <summary>The derived-database name prefix, e.g. <c>myproject_test_</c>.</summary>
        public required string DatabasePrefix { get; init; }

        /// <summary>The ownership-marker prefix the harness stamps, e.g. <c>myproject-test-checkout:</c>.</summary>
        public required string MarkerPrefix { get; init; }

        /// <summary>
        /// Roots under which per-checkout scratch directories live (each named by the checkout hash), or
        /// empty when the project keeps no scratch trees.
        /// </summary>
        public IReadOnlyList<string> ScratchRoots { get; init; } = [];

        /// <summary>
        /// Name of the marker file inside a scratch tree recording the owning checkout, with the same
        /// prefixed form as the database marker.
        /// </summary>
        public string ScratchMarkerFileName { get; init; } = DefaultScratchMarkerFileName;

        /// <summary>Whether to actually drop and delete. Without it the run only lists what it would do.</summary>
        public bool Confirmed { get; init; }
    }
}
