using EasyReasy.Database.Logging.AspNetCore.Enrichers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace EasyReasy.Database.Logging.AspNetCore
{
    /// <summary>
    /// ASP.NET Core registration entry point for operational logging.
    /// </summary>
    public static class AspNetCoreOperationalLoggingServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the <see cref="HttpContextEnricher"/> (and <see cref="IHttpContextAccessor"/>)
        /// so operational log events raised during a request carry the acting user id and client IP.
        /// Call alongside <c>AddOperationalLogging</c>; the enricher is picked up automatically by
        /// <c>UseOperationalLogging</c>.
        /// </summary>
        public static IServiceCollection AddOperationalLoggingHttpContext(
            this IServiceCollection services,
            Action<HttpContextEnricherOptions>? configure = null)
        {
            HttpContextEnricherOptions options = new HttpContextEnricherOptions();
            configure?.Invoke(options);

            services.AddHttpContextAccessor();
            services.AddSingleton(options);
            services.AddSingleton<ILogEventEnricher>(serviceProvider => new HttpContextEnricher(
                serviceProvider.GetRequiredService<IHttpContextAccessor>(),
                serviceProvider.GetRequiredService<HttpContextEnricherOptions>()));

            return services;
        }
    }
}
