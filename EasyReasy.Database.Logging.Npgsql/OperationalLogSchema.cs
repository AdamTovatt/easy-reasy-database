using System.Text.RegularExpressions;

namespace EasyReasy.Database.Logging.Npgsql
{
    /// <summary>
    /// Builds the PostgreSQL DDL for the operational log table: a monthly range-partitioned table
    /// with a <c>jsonb</c> properties column, the supporting indexes, the current + next two
    /// monthly partitions, and a <c>DEFAULT</c> partition so writes never fail before the
    /// background maintenance service has run. Run the returned SQL through your own migration
    /// runner (e.g. dbup) — the library does not own your migration sequence.
    /// </summary>
    public static class OperationalLogSchema
    {
        private static readonly Regex UnqualifiedIdentifier = new Regex(
            @"^[A-Za-z_][A-Za-z0-9_]*$",
            RegexOptions.Compiled);

        /// <summary>
        /// Returns the <c>CREATE TABLE</c> + index + partition DDL for an operational log table
        /// named <paramref name="tableName"/>. The name must be an unqualified SQL identifier (no
        /// schema prefix) because it is also used to derive partition and index names.
        /// </summary>
        /// <exception cref="ArgumentException">If <paramref name="tableName"/> is not an unqualified identifier.</exception>
        public static string CreateTableSql(string tableName = "operational_log")
        {
            if (string.IsNullOrWhiteSpace(tableName) || !UnqualifiedIdentifier.IsMatch(tableName))
            {
                throw new ArgumentException(
                    $"'{tableName}' must be an unqualified SQL identifier (no schema prefix) for the partitioned DDL helper.",
                    nameof(tableName));
            }

            return $@"
CREATE TABLE IF NOT EXISTS {tableName} (
    id               BIGSERIAL,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    level            TEXT        NOT NULL,
    source_context   TEXT,
    message          TEXT        NOT NULL,
    message_template TEXT,
    exception        TEXT,
    correlation_id   TEXT,
    user_id          TEXT,
    properties       JSONB,
    PRIMARY KEY (id, created_at)
) PARTITION BY RANGE (created_at);

CREATE INDEX IF NOT EXISTS idx_{tableName}_created_at  ON {tableName} (created_at DESC);
CREATE INDEX IF NOT EXISTS idx_{tableName}_user_id     ON {tableName} (user_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_{tableName}_correlation ON {tableName} (correlation_id);

DO $$
DECLARE
    month_start    DATE;
    month_end      DATE;
    offset_idx     INT;
    base_month     DATE := date_trunc('month', (now() AT TIME ZONE 'UTC')::date)::DATE;
    partition_name TEXT;
BEGIN
    FOR offset_idx IN 0..2 LOOP
        month_start := (base_month + (offset_idx || ' months')::INTERVAL)::DATE;
        month_end   := (month_start + INTERVAL '1 month')::DATE;
        partition_name := '{tableName}_' || to_char(month_start, 'YYYY_MM');
        EXECUTE format(
            'CREATE TABLE IF NOT EXISTS %I PARTITION OF {tableName} FOR VALUES FROM (%L) TO (%L)',
            partition_name, month_start, month_end
        );
    END LOOP;
    EXECUTE 'CREATE TABLE IF NOT EXISTS {tableName}_default PARTITION OF {tableName} DEFAULT';
END $$;
";
        }
    }
}
