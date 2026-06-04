using System.Net;
using System.Security.Claims;
using EasyReasy.Database.Logging.AspNetCore;
using EasyReasy.Database.Logging.AspNetCore.Enrichers;
using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace EasyReasy.Database.Logging.AspNetCore.Tests
{
    public class HttpContextEnricherTests
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

        private static HttpContextEnricher CreateEnricher(HttpContext? httpContext, HttpContextEnricherOptions? options = null)
        {
            HttpContextAccessor accessor = new HttpContextAccessor { HttpContext = httpContext };
            return new HttpContextEnricher(accessor, options ?? new HttpContextEnricherOptions());
        }

        [Fact]
        public void Enrich_WithAuthenticatedRequest_AttachesUserIdAndIpAddress()
        {
            DefaultHttpContext httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "user-42"),
            }));
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.5");

            LogEvent logEvent = EmptyEvent();
            CreateEnricher(httpContext).Enrich(logEvent, new ScalarPropertyFactory());

            Assert.Equal("user-42", Scalar(logEvent, "UserId"));
            Assert.Equal("203.0.113.5", Scalar(logEvent, "IpAddress"));
        }

        [Fact]
        public void Enrich_WithCustomResolver_UsesResolvedUserId()
        {
            DefaultHttpContext httpContext = new DefaultHttpContext();
            HttpContextEnricherOptions options = new HttpContextEnricherOptions
            {
                ResolveUserId = _ => "from-resolver",
            };

            LogEvent logEvent = EmptyEvent();
            CreateEnricher(httpContext, options).Enrich(logEvent, new ScalarPropertyFactory());

            Assert.Equal("from-resolver", Scalar(logEvent, "UserId"));
        }

        [Fact]
        public void Enrich_WithNoHttpContext_AttachesNothing()
        {
            LogEvent logEvent = EmptyEvent();
            CreateEnricher(httpContext: null).Enrich(logEvent, new ScalarPropertyFactory());

            Assert.Empty(logEvent.Properties);
        }

        [Fact]
        public void Enrich_WithUnauthenticatedRequest_AttachesIpButNoUserId()
        {
            DefaultHttpContext httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.7");

            LogEvent logEvent = EmptyEvent();
            CreateEnricher(httpContext).Enrich(logEvent, new ScalarPropertyFactory());

            Assert.Null(Scalar(logEvent, "UserId"));
            Assert.Equal("198.51.100.7", Scalar(logEvent, "IpAddress"));
        }
    }
}
