using System.Text.RegularExpressions;

namespace EasyReasy.Database.Sql
{
    /// <summary>
    /// Guard for identifiers that have to be interpolated into SQL. Database, role, table and column
    /// names cannot be passed as parameters, so every such name should be checked against the
    /// plain-identifier shape before it reaches a statement.
    /// </summary>
    public static class SqlIdentifier
    {
        /// <summary>
        /// PostgreSQL's identifier length limit (<c>NAMEDATALEN - 1</c>), which is the most restrictive
        /// of the common databases. Enforced because PostgreSQL TRUNCATES rather than rejects: a longer
        /// name would create a database under the truncated spelling and then fail to connect to the name
        /// that was asked for, reported as "database does not exist".
        /// </summary>
        public const int MaximumLength = 63;

        // Anchored with \A and \z rather than ^ and $: in .NET, $ also matches immediately BEFORE a
        // trailing newline, so "example_test\n" would pass a ^...$ check and then be interpolated into a
        // statement with the newline still in it.
        private static readonly Regex PlainIdentifier = new Regex(@"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.Compiled);

        /// <summary>Whether <paramref name="value"/> is a plain unquoted SQL identifier the database will keep whole.</summary>
        public static bool IsPlain(string value)
        {
            return value.Length <= MaximumLength && PlainIdentifier.IsMatch(value);
        }

        /// <summary>
        /// Throws unless <paramref name="value"/> is a plain identifier, naming what it was and what the
        /// refusal stopped — the check every caller that interpolates a name into DDL performs, kept in
        /// one place so the refusal reads the same wherever it comes from.
        /// </summary>
        /// <param name="value">The identifier to check.</param>
        /// <param name="kind">What the value is, e.g. <c>Test database name</c>.</param>
        /// <param name="abortedAction">What was abandoned, e.g. <c>provisioning</c>.</param>
        /// <exception cref="InvalidOperationException">If <paramref name="value"/> is not a plain identifier.</exception>
        public static void RequirePlain(string value, string kind, string abortedAction)
        {
            if (!IsPlain(value))
            {
                throw new InvalidOperationException(
                    $"{kind} '{value}' is not a plain SQL identifier; {abortedAction} aborted.");
            }
        }
    }
}
