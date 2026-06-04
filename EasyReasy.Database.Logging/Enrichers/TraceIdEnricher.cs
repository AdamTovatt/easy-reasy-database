using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace EasyReasy.Database.Logging.Enrichers
{
    /// <summary>
    /// Serilog enricher that attaches the current W3C trace id as the <c>CorrelationId</c> property.
    /// ASP.NET Core (and any code that starts an <see cref="Activity"/>) sets
    /// <see cref="Activity.Current"/>, so events emitted within that activity carry the same
    /// correlation id. Events raised with no current activity get no property. Web-free — depends
    /// only on <see cref="System.Diagnostics"/>.
    /// </summary>
    public sealed class TraceIdEnricher : ILogEventEnricher
    {
        /// <inheritdoc/>
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            Activity? activity = Activity.Current;
            if (activity == null)
            {
                return;
            }

            string traceId = activity.TraceId.ToString();
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationId", traceId));
        }
    }
}
