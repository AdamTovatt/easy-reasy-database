namespace EasyReasy.Database.Logging.Models
{
    /// <summary>
    /// Optional filters applied to operational log reads. A null property means "no constraint on
    /// that column".
    /// </summary>
    public class OperationalLogFilters
    {
        /// <summary>Restrict to a single Serilog level name (exact match).</summary>
        public string? Level { get; set; }

        /// <summary>Restrict to a single <c>SourceContext</c> (exact match).</summary>
        public string? SourceContext { get; set; }

        /// <summary>Lower bound (inclusive, UTC) on <c>created_at</c>.</summary>
        public DateTime? From { get; set; }

        /// <summary>Upper bound (exclusive, UTC) on <c>created_at</c>.</summary>
        public DateTime? To { get; set; }
    }
}
