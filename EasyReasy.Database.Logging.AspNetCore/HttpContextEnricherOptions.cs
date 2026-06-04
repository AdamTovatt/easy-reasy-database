using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace EasyReasy.Database.Logging.AspNetCore
{
    /// <summary>
    /// Options for the <see cref="Enrichers.HttpContextEnricher"/>.
    /// </summary>
    public sealed class HttpContextEnricherOptions
    {
        /// <summary>
        /// Resolves the acting user id from the current request. Defaults to the standard
        /// <see cref="ClaimTypes.NameIdentifier"/> claim. Override it if your identity uses a
        /// different claim (e.g. delegate to an auth library's helper).
        /// </summary>
        public Func<HttpContext, string?> ResolveUserId { get; set; } = DefaultResolveUserId;

        private static string? DefaultResolveUserId(HttpContext context)
        {
            return context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
    }
}
