namespace EasyReasy.Database.Logging.Models
{
    /// <summary>
    /// One operational log fact, materialized from a Serilog event after the row has been written
    /// to the operational log table. Published over the in-process live feed via
    /// <see cref="Broadcasting.IOperationalLogBroadcaster"/>. Carries no <c>Id</c> — the sink does
    /// not read generated ids back; use <see cref="CorrelationId"/> + <see cref="CreatedAt"/> to
    /// locate the corresponding persisted row.
    /// </summary>
    public class OperationalLogEvent
    {
        /// <summary>The UTC timestamp the event was raised.</summary>
        public DateTime CreatedAt { get; set; }

        /// <summary>The Serilog level name (e.g. <c>Information</c>, <c>Warning</c>, <c>Error</c>).</summary>
        public required string Level { get; set; }

        /// <summary>The Serilog <c>SourceContext</c>, usually the logging category / type name.</summary>
        public string? SourceContext { get; set; }

        /// <summary>The rendered log message.</summary>
        public required string Message { get; set; }

        /// <summary>The unrendered Serilog message template.</summary>
        public string? MessageTemplate { get; set; }

        /// <summary>The formatted exception, if the event carried one.</summary>
        public string? Exception { get; set; }

        /// <summary>The W3C trace id correlating the event to a request, if available.</summary>
        public string? CorrelationId { get; set; }

        /// <summary>The acting user id, if the event was raised in an authenticated request scope.</summary>
        public string? UserId { get; set; }

        /// <summary>The remaining structured properties as a JSON document, or <c>null</c> if none.</summary>
        public string? Properties { get; set; }
    }
}
