using EasyReasy.Database.Logging.Models;
using Serilog.Events;

namespace EasyReasy.Database.Logging.Serialization
{
    /// <summary>
    /// Materializes a Serilog <see cref="LogEvent"/> into the <see cref="OperationalLogEvent"/>
    /// row shape shared by every sink (the portable <c>INSERT</c> sink and the Postgres COPY sink),
    /// so the column-vs-<c>properties</c> mapping and the JSON contract live in exactly one place.
    /// </summary>
    public static class OperationalLogEventFactory
    {
        // Properties promoted to typed columns — excluded from the JSON catch-all so they aren't
        // stored twice.
        private static readonly IReadOnlySet<string> KnownColumnProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SourceContext", "CorrelationId", "UserId",
        };

        /// <summary>
        /// Maps a Serilog event to an <see cref="OperationalLogEvent"/>. The timestamp is normalized
        /// to UTC; <c>SourceContext</c> / <c>CorrelationId</c> / <c>UserId</c> are promoted to typed
        /// columns and every remaining property is serialized into the JSON <c>Properties</c> blob.
        /// </summary>
        public static OperationalLogEvent Materialize(LogEvent logEvent)
        {
            return new OperationalLogEvent
            {
                CreatedAt = logEvent.Timestamp.UtcDateTime,
                Level = logEvent.Level.ToString(),
                SourceContext = logEvent.GetScalarString("SourceContext"),
                Message = logEvent.RenderMessage(),
                MessageTemplate = logEvent.MessageTemplate.Text,
                Exception = logEvent.Exception?.ToString(),
                CorrelationId = logEvent.GetScalarString("CorrelationId"),
                UserId = logEvent.GetScalarString("UserId"),
                Properties = logEvent.SerializeRemainingProperties(KnownColumnProperties),
            };
        }
    }
}
