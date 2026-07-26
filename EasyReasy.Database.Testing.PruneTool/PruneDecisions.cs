using System.Text.RegularExpressions;

namespace EasyReasy.Database.Testing.PruneTool
{
    /// <summary>
    /// The decision rules of the sweep, kept pure so the destructive path can be tested without a
    /// database. Two conditions must BOTH hold before a database qualifies:
    /// <list type="number">
    /// <item>its name has the exact derived shape — the prefix followed by 8 lowercase hex characters. A
    /// hand-made database like <c>myproject_test_1418</c> therefore never qualifies, whatever it
    /// contains;</item>
    /// <item>it carries the ownership marker the harness stamps on a database it DERIVED — never on one a
    /// pin named — so a database someone else named cannot qualify even if its name happens to look
    /// derived.</item>
    /// </list>
    /// Whether the recorded checkout still exists is then what separates keeping from dropping.
    /// </summary>
    public static class PruneDecisions
    {
        /// <summary>
        /// Whether <paramref name="databaseName"/> has the exact shape the per-checkout derivation
        /// produces for <paramref name="databasePrefix"/>.
        /// </summary>
        public static bool HasDerivedShape(string databaseName, string databasePrefix)
        {
            return Regex.IsMatch(databaseName, "^" + Regex.Escape(databasePrefix) + "[0-9a-f]{8}$");
        }

        /// <summary>
        /// Extracts the checkout path recorded in <paramref name="comment"/>, or null when the comment is
        /// missing, carries a different marker, or records no path.
        /// </summary>
        public static string? TryGetCheckoutPath(string? comment, string markerPrefix)
        {
            if (comment == null || !comment.StartsWith(markerPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            string checkoutPath = comment.Substring(markerPrefix.Length);
            return checkoutPath.Length == 0 ? null : checkoutPath;
        }

        /// <summary>
        /// The scratch directory belonging to <paramref name="databaseName"/> under
        /// <paramref name="scratchRoot"/>: the tree hangs off the same checkout hash the database name
        /// ends in, so the orphan's scratch can be removed precisely — no whole-tree delete that would
        /// also take out the checkouts still in use.
        /// </summary>
        public static string ScratchDirectoryFor(string databaseName, string databasePrefix, string scratchRoot)
        {
            return Path.Combine(scratchRoot, databaseName.Substring(databasePrefix.Length));
        }
    }
}
