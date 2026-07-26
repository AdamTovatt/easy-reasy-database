using EasyReasy.Database.Sql;
using Npgsql;

namespace EasyReasy.Database.Testing.Npgsql
{
    /// <summary>
    /// Creates the database a test run uses, and optionally records which checkout owns it.
    /// <para>
    /// A DERIVED database should be stamped with its owning checkout (<c>CheckoutDatabaseIdentity</c>'s
    /// <c>MarkerFor</c>, in EasyReasy.Database.Testing), which is what lets a pruning sweep tell an
    /// abandoned worktree's database from a live one. A PINNED database (<c>IsPinned</c>) must never be
    /// stamped: the name then belongs to whoever chose it — CI's fixed database, or a hand-made database
    /// named after an issue — and writing an ownership marker onto someone else's database would enrol it
    /// in a sweep that later deletes it.
    /// </para>
    /// </summary>
    public static class TestDatabaseProvisioner
    {
        /// <summary>
        /// Creates <paramref name="connectionString"/>'s database if it does not exist yet, and — when
        /// <paramref name="ownershipMarker"/> is non-null — records which checkout owns it. Runs from the
        /// maintenance database, since <c>CREATE DATABASE</c> cannot run from inside the database being
        /// created.
        /// </summary>
        /// <param name="connectionString">The connection string naming the database to create.</param>
        /// <param name="ownershipMarker">
        /// The ownership marker a pruning sweep deletes on, or null to leave the database unclaimed.
        /// Passed in rather than decided here so both outcomes are testable, and so this package needs no
        /// dependency on the one that derives the name; the caller supplies
        /// <c>identity.IsPinned() ? null : identity.MarkerFor(identity.CurrentCheckoutRoot())</c>.
        /// </param>
        public static async Task EnsureDatabaseExistsAsync(string connectionString, string? ownershipMarker)
        {
            string targetDatabase = new NpgsqlConnectionStringBuilder(connectionString).Database
                ?? throw new InvalidOperationException("The test connection string has no database name.");

            // A database name cannot be parameterised, so it is interpolated below — refuse anything that
            // is not a plain identifier rather than trust every caller to have validated it.
            SqlIdentifier.RequirePlain(targetDatabase, "Test database name", "provisioning");

            await using NpgsqlConnection connection = new NpgsqlConnection(PostgresConnectionStrings.Maintenance(connectionString));
            await connection.OpenAsync();

            bool exists;

            await using (NpgsqlCommand existsCommand = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", connection))
            {
                existsCommand.Parameters.AddWithValue("name", targetDatabase);
                exists = await existsCommand.ExecuteScalarAsync() != null;
            }

            if (!exists)
            {
                try
                {
                    await using NpgsqlCommand createCommand = new NpgsqlCommand($"CREATE DATABASE {targetDatabase}", connection);
                    await createCommand.ExecuteNonQueryAsync();
                }
                catch (PostgresException exception) when (
                    exception.SqlState == PostgresErrorCodes.UniqueViolation
                    || exception.SqlState == PostgresErrorCodes.DuplicateDatabase)
                {
                    // Another run created it between the check and the CREATE — the outcome we wanted
                    // anyway. That loser gets unique_violation from pg_database_datname_index, NOT
                    // duplicate_database (which is what CREATE raises when the database was already
                    // visible), so both are handled — verified against PostgreSQL 16. This path takes no
                    // lock, so the race is reachable whenever two checkouts pin the same database name.
                }
            }

            if (ownershipMarker == null)
            {
                return;
            }

            // Rewritten on every run, not only on creation, so a database left behind by an interrupted run
            // still ends up attributable. COMMENT takes a literal rather than a parameter, so the marker
            // is escaped by doubling any single quote — the only escaping a standard-conforming string
            // needs.
            await using NpgsqlCommand commentCommand = new NpgsqlCommand(
                $"COMMENT ON DATABASE {targetDatabase} IS '{ownershipMarker.Replace("'", "''")}'", connection);
            await commentCommand.ExecuteNonQueryAsync();
        }
    }
}
