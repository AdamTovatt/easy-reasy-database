using System.Text.RegularExpressions;
using Serilog.Events;

namespace EasyReasy.Database.Logging
{
    /// <summary>
    /// Configuration for the operational logging pipeline: where rows are written, how the
    /// batched sink buffers them, and the Serilog minimum-level policy applied when
    /// <see cref="OperationalLoggingHostExtensions.UseOperationalLogging"/> installs Serilog as
    /// the logging provider.
    /// </summary>
    public sealed class OperationalLoggingOptions
    {
        private static readonly Regex TableNamePattern = new Regex(
            @"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)?$",
            RegexOptions.Compiled);

        /// <summary>
        /// The table operational log rows are written to and read from. May be schema-qualified
        /// (<c>schema.table</c>). Validated as a SQL identifier — it is interpolated into SQL, so
        /// it must never come from untrusted input. Defaults to <c>operational_log</c>.
        /// </summary>
        public string TableName { get; set; } = "operational_log";

        /// <summary>
        /// The Serilog minimum level for the whole pipeline. Defaults to
        /// <see cref="LogEventLevel.Information"/>.
        /// </summary>
        public LogEventLevel MinimumLevel { get; set; } = LogEventLevel.Information;

        /// <summary>
        /// Per-source minimum-level overrides (Serilog <c>SourceContext</c> prefixes). Empty by
        /// default. A web app typically adds <c>{ "Microsoft.AspNetCore", LogEventLevel.Warning }</c>.
        /// </summary>
        public IDictionary<string, LogEventLevel> MinimumLevelOverrides { get; } = new Dictionary<string, LogEventLevel>();

        /// <summary>
        /// Maximum number of events the batched sink flushes per batch. Defaults to 100.
        /// </summary>
        public int BatchSizeLimit { get; set; } = 100;

        /// <summary>
        /// Flush interval for the batched sink. Defaults to 2 seconds.
        /// </summary>
        public TimeSpan BatchPeriod { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>
        /// Upper bound on events buffered before the sink starts dropping under sustained back
        /// pressure. Defaults to 10,000.
        /// </summary>
        public int QueueLimit { get; set; } = 10_000;

        /// <summary>
        /// Whether to also write to the console sink in parallel as a database-outage safety net.
        /// Defaults to <c>true</c>.
        /// </summary>
        public bool WriteToConsole { get; set; } = true;

        /// <summary>
        /// Validates the options. Throws <see cref="ArgumentException"/> when <see cref="TableName"/>
        /// is not a safe SQL identifier.
        /// </summary>
        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(TableName) || !TableNamePattern.IsMatch(TableName))
            {
                throw new ArgumentException(
                    $"'{TableName}' is not a valid table name. Use a SQL identifier, optionally schema-qualified (e.g. 'operational_log' or 'logs.operational_log').",
                    nameof(TableName));
            }
        }
    }
}
