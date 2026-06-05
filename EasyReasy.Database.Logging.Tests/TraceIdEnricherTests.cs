using System.Diagnostics;
using EasyReasy.Database.Logging.Enrichers;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace EasyReasy.Database.Logging.Tests
{
    /// <summary>
    /// Covers <see cref="TraceIdEnricher"/>: it attaches the current W3C trace id as
    /// <c>CorrelationId</c> when an <see cref="Activity"/> is in scope, and attaches nothing when
    /// there is none.
    /// </summary>
    public class TraceIdEnricherTests
    {
        private sealed class ScalarPropertyFactory : ILogEventPropertyFactory
        {
            public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
            {
                return new LogEventProperty(name, new ScalarValue(value));
            }
        }

        private static LogEvent EmptyEvent()
        {
            return new LogEvent(
                DateTimeOffset.UtcNow,
                LogEventLevel.Information,
                exception: null,
                new MessageTemplateParser().Parse("test"),
                Array.Empty<LogEventProperty>());
        }

        private static string? Scalar(LogEvent logEvent, string name)
        {
            return logEvent.Properties.TryGetValue(name, out LogEventPropertyValue? value) && value is ScalarValue scalar
                ? scalar.Value?.ToString()
                : null;
        }

        [Fact]
        public void Enrich_WithCurrentActivity_AttachesTraceIdAsCorrelationId()
        {
            using Activity activity = new Activity("test");
            activity.SetIdFormat(ActivityIdFormat.W3C);
            activity.Start();

            LogEvent logEvent = EmptyEvent();
            new TraceIdEnricher().Enrich(logEvent, new ScalarPropertyFactory());

            Assert.Equal(activity.TraceId.ToString(), Scalar(logEvent, "CorrelationId"));
        }

        [Fact]
        public void Enrich_WithNoCurrentActivity_AttachesNothing()
        {
            Assert.Null(Activity.Current);

            LogEvent logEvent = EmptyEvent();
            new TraceIdEnricher().Enrich(logEvent, new ScalarPropertyFactory());

            Assert.Empty(logEvent.Properties);
        }
    }
}
