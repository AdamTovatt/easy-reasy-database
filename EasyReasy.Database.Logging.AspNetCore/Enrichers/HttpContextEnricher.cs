using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;

namespace EasyReasy.Database.Logging.AspNetCore.Enrichers
{
    /// <summary>
    /// Serilog enricher that attaches request-scoped identity to every event raised during a
    /// request: the acting user id as <c>UserId</c> (a typed operational-log column) and the client
    /// IP as <c>IpAddress</c> (which lands in the <c>properties</c> JSON, since there is no IP
    /// column). Events raised outside a request (background services, startup) get nothing.
    /// </summary>
    public sealed class HttpContextEnricher : ILogEventEnricher
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly Func<HttpContext, string?> _resolveUserId;

        /// <summary>Creates the enricher.</summary>
        public HttpContextEnricher(IHttpContextAccessor httpContextAccessor, HttpContextEnricherOptions options)
        {
            _httpContextAccessor = httpContextAccessor;
            _resolveUserId = options.ResolveUserId;
        }

        /// <inheritdoc/>
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            HttpContext? httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return;
            }

            string? userId = _resolveUserId(httpContext);
            if (!string.IsNullOrEmpty(userId))
            {
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("UserId", userId));
            }

            string? ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
            if (ipAddress != null)
            {
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("IpAddress", ipAddress));
            }
        }
    }
}
