namespace EasyReasy.Database.Logging.Models
{
    /// <summary>
    /// One persisted row of the operational log table, returned from
    /// <see cref="Reading.IOperationalLogReadRepository"/> as a paginated read DTO. The in-flight
    /// broadcast counterpart is <see cref="OperationalLogEvent"/>.
    /// </summary>
    public class OperationalLogEntry
    {
        /// <summary>The generated row id.</summary>
        public long Id { get; set; }

        /// <summary>The UTC timestamp the event was raised.</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>The Serilog level name.</summary>
        public required string Level { get; set; }

        /// <summary>The Serilog <c>SourceContext</c>.</summary>
        public string? SourceContext { get; set; }

        /// <summary>The rendered log message.</summary>
        public required string Message { get; set; }

        /// <summary>The unrendered Serilog message template.</summary>
        public string? MessageTemplate { get; set; }

        /// <summary>The formatted exception, if any.</summary>
        public string? Exception { get; set; }

        /// <summary>The W3C trace id, if available.</summary>
        public string? CorrelationId { get; set; }

        /// <summary>The acting user id, if available.</summary>
        public string? UserId { get; set; }

        /// <summary>The remaining structured properties as a JSON document, or <c>null</c>.</summary>
        public string? Properties { get; set; }
    }
}
