using EasyReasy.Database.Sql;
using EasyReasy.Database.Testing.Npgsql;
using Npgsql;

namespace EasyReasy.Database.Testing.PruneTool
{
    /// <summary>
    /// One sweep: reclaim the per-checkout test database, and the per-checkout scratch tree, of every
    /// checkout that no longer exists. Lists by default; drops only when
    /// <see cref="PruneOptions.Confirmed"/> is set.
    /// <para>
    /// Scratch trees are reclaimed by two passes, because there are two ways for one to be orphaned. A
    /// tree belonging to a database being dropped is removed alongside it, identified by the checkout
    /// hash the database name ends in — the database's own marker has already proved that checkout gone,
    /// so the tree needs no marker of its own. A tree whose checkout PINNED its database name has no
    /// database row pointing at it at all, so it is swept separately, on a marker file it writes itself.
    /// </para>
    /// </summary>
    public sealed class PruneRunner
    {
        private readonly PruneOptions _options;
        private readonly TextWriter _output;
        private readonly TextWriter _error;

        /// <summary>
        /// Creates a runner writing its report to <paramref name="output"/> and any refusal to
        /// <paramref name="error"/>, so a listing piped somewhere is not polluted by a failure.
        /// </summary>
        public PruneRunner(PruneOptions options, TextWriter output, TextWriter error)
        {
            _options = options;
            _output = output;
            _error = error;
        }

        /// <summary>Runs the sweep. Returns the process exit code.</summary>
        public async Task<int> RunAsync()
        {
            string? refusal = ValidateOptions();

            if (refusal != null)
            {
                await _error.WriteLineAsync(refusal);
                return 1;
            }

            int orphanedDatabases = await PruneDatabasesAsync();
            int orphanedScratchTrees = PruneOrphanedScratchTrees();

            if (!_options.Confirmed && orphanedDatabases + orphanedScratchTrees > 0)
            {
                await _output.WriteLineAsync("");
                await _output.WriteLineAsync("Re-run with --yes to apply.");
            }

            return 0;
        }

        /// <summary>
        /// Why this run must not proceed, or null. Both checks guard the destructive rule against being
        /// widened by its own configuration, so they run before anything connects.
        /// </summary>
        private string? ValidateOptions()
        {
            // An empty marker prefix is a prefix of EVERY comment, so every stamped database in the
            // cluster would be read as this project's and judged only on whether the comment happens to
            // name a live directory — which, for a comment that is not a path at all, it does not. The
            // CLI rejects it too; this is the guard for any other caller.
            if (string.IsNullOrWhiteSpace(_options.MarkerPrefix))
            {
                return "Error: the ownership-marker prefix cannot be blank; it is what limits the sweep to "
                    + "this project's databases.";
            }

            // Checked with the hash appended, since that is the shape the sweep actually matches: a prefix
            // that only overflows the identifier limit once the 8 hash characters are on it is still one
            // no derived database could ever have been created under.
            if (!SqlIdentifier.IsPlain(_options.DatabasePrefix + new string('0', 8)))
            {
                return $"Error: database prefix '{_options.DatabasePrefix}' cannot form a plain SQL identifier.";
            }

            return null;
        }

        /// <summary>
        /// The database pass. Returns how many orphans were FOUND — the same number whether or not they
        /// were then dropped, so the caller's "would have done something" check reads the same in both
        /// modes.
        /// </summary>
        private async Task<int> PruneDatabasesAsync()
        {
            await using NpgsqlConnection connection = new NpgsqlConnection(
                PostgresConnectionStrings.Maintenance(_options.ConnectionString));
            await connection.OpenAsync();

            List<StampedDatabase> stamped = await ListStampedDatabasesAsync(connection);

            if (stamped.Count == 0)
            {
                await _output.WriteLineAsync("No harness-created test databases found.");
                return 0;
            }

            List<StampedDatabase> orphans = await ReportAndSelectOrphansAsync(stamped);

            if (orphans.Count == 0)
            {
                await _output.WriteLineAsync("");
                await _output.WriteLineAsync("No databases to prune.");
                return 0;
            }

            if (!_options.Confirmed)
            {
                await _output.WriteLineAsync("");
                await _output.WriteLineAsync($"{orphans.Count} database(s) would be dropped.");
                return orphans.Count;
            }

            await DropOrphansAsync(connection, orphans);

            await _output.WriteLineAsync("");
            await _output.WriteLineAsync($"Pruned {orphans.Count} database(s).");
            return orphans.Count;
        }

        /// <summary>
        /// Every database in the cluster that this project's harness DERIVED and stamped, with the
        /// checkout each one records.
        /// </summary>
        private async Task<List<StampedDatabase>> ListStampedDatabasesAsync(NpgsqlConnection connection)
        {
            List<StampedDatabase> stamped = [];

            await using NpgsqlCommand listCommand = new NpgsqlCommand(
                "SELECT datname, shobj_description(oid, 'pg_database') FROM pg_database WHERE datname LIKE @pattern ORDER BY datname",
                connection);

            // LIKE narrows the scan; the exact shape and marker conditions are applied below, in the
            // pure decision rules, so nothing outside the convention is ever even considered.
            listCommand.Parameters.AddWithValue("pattern", _options.DatabasePrefix.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");

            await using NpgsqlDataReader reader = await listCommand.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                string name = reader.GetString(0);
                string? comment = await reader.IsDBNullAsync(1) ? null : reader.GetString(1);

                if (!PruneDecisions.HasDerivedShape(name, _options.DatabasePrefix))
                {
                    continue;
                }

                string? checkoutPath = PruneDecisions.TryGetCheckoutPath(comment, _options.MarkerPrefix);

                if (checkoutPath != null)
                {
                    stamped.Add(new StampedDatabase(name, checkoutPath));
                }
            }

            return stamped;
        }

        /// <summary>
        /// Prints the verdict on every stamped database and returns the ones whose checkout is gone. The
        /// full listing is printed in both modes on purpose: it is what lets an operator notice a wrong
        /// verdict BEFORE re-running with <c>--yes</c>.
        /// </summary>
        private async Task<List<StampedDatabase>> ReportAndSelectOrphansAsync(List<StampedDatabase> stamped)
        {
            List<StampedDatabase> orphans = [];

            foreach (StampedDatabase database in stamped)
            {
                if (Directory.Exists(database.CheckoutPath))
                {
                    await _output.WriteLineAsync($"keep  {database.Name}  <- {database.CheckoutPath}");
                }
                else
                {
                    await _output.WriteLineAsync($"drop  {database.Name}  <- {database.CheckoutPath} (gone)");
                    orphans.Add(database);
                }
            }

            return orphans;
        }

        /// <summary>Drops each orphan and removes the scratch tree hanging off its checkout hash.</summary>
        private async Task DropOrphansAsync(NpgsqlConnection connection, List<StampedDatabase> orphans)
        {
            foreach (StampedDatabase orphan in orphans)
            {
                // WITH (FORCE) — PostgreSQL 13 and later — so a leftover connection from a killed run does
                // not block the drop. The name passed HasDerivedShape above, so it is a plain identifier,
                // safe to interpolate.
                await using NpgsqlCommand dropCommand = new NpgsqlCommand($"DROP DATABASE IF EXISTS {orphan.Name} WITH (FORCE)", connection);
                await dropCommand.ExecuteNonQueryAsync();

                List<string> removedScratch = [];

                foreach (string scratchRoot in _options.ScratchRoots)
                {
                    string scratch = PruneDecisions.ScratchDirectoryFor(orphan.Name, _options.DatabasePrefix, scratchRoot);

                    if (Directory.Exists(scratch))
                    {
                        Directory.Delete(scratch, recursive: true);
                        removedScratch.Add(scratch);
                    }
                }

                await _output.WriteLineAsync(removedScratch.Count == 0
                    ? $"Dropped {orphan.Name}."
                    : $"Dropped {orphan.Name} and removed {string.Join(", ", removedScratch)}.");
            }
        }

        /// <summary>
        /// The scratch pass, for trees no database drop would reach: a checkout that pinned its database
        /// name still writes a tree under its derived hash. Returns how many orphaned trees were FOUND.
        /// </summary>
        private int PruneOrphanedScratchTrees()
        {
            int found = 0;

            foreach (string scratchRoot in _options.ScratchRoots)
            {
                if (!Directory.Exists(scratchRoot))
                {
                    continue;
                }

                foreach (string tree in Directory.GetDirectories(scratchRoot))
                {
                    string markerFile = Path.Combine(tree, _options.ScratchMarkerFileName);

                    if (!File.Exists(markerFile))
                    {
                        continue;
                    }

                    string? checkoutPath = PruneDecisions.TryGetCheckoutPath(
                        File.ReadAllText(markerFile).Trim(), _options.MarkerPrefix);

                    if (checkoutPath == null || Directory.Exists(checkoutPath))
                    {
                        continue;
                    }

                    found++;

                    if (_options.Confirmed)
                    {
                        Directory.Delete(tree, recursive: true);
                        _output.WriteLine($"Removed orphaned scratch {tree} (checkout {checkoutPath} is gone).");
                    }
                    else
                    {
                        _output.WriteLine($"Would remove orphaned scratch {tree} (checkout {checkoutPath} is gone).");
                    }
                }
            }

            if (found == 0)
            {
                _output.WriteLine("No orphaned scratch directories.");
            }

            return found;
        }

        /// <summary>A database this project's harness derived, and the checkout its marker records.</summary>
        private sealed record StampedDatabase(string Name, string CheckoutPath);
    }
}
