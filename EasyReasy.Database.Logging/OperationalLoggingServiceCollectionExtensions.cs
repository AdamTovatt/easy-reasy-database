using System.Data.Common;
using EasyReasy.Database.Logging.Broadcasting;
using EasyReasy.Database.Logging.Enrichers;
using EasyReasy.Database.Logging.Reading;
using EasyReasy.Database.Logging.Sinks;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace EasyReasy.Database.Logging
{
    /// <summary>
    /// Registration entry point for the operational logging pipeline.
    /// </summary>
    public static class OperationalLoggingServiceCollectionExtensions
    {
        /// <summary>
        /// Registers the operational logging services: the configured
        /// <see cref="OperationalLoggingOptions"/>, the in-memory broadcaster, the trace-id
        /// enricher, the provider-agnostic batched sink (as <see cref="IBatchedLogEventSink"/>),
        /// and the paginated read repository.
        /// <para>
        /// A registered <see cref="DbDataSource"/> is required (provided by the
        /// <c>EasyReasy.Database</c> setup). Call
        /// <see cref="OperationalLoggingHostExtensions.UseOperationalLogging"/> on the host builder
        /// to install Serilog as the logging provider. The Postgres package re-registers
        /// <see cref="IBatchedLogEventSink"/> with the faster COPY sink when added; the ASP.NET Core
        /// package adds the <c>HttpContext</c> enricher.
        /// </para>
        /// </summary>
        public static IServiceCollection AddOperationalLogging(
            this IServiceCollection services,
            Action<OperationalLoggingOptions>? configure = null)
        {
            OperationalLoggingOptions options = new OperationalLoggingOptions();
            configure?.Invoke(options);
            options.Validate();

            services.AddSingleton(options);
            services.AddSingleton<IOperationalLogBroadcaster, InMemoryOperationalLogBroadcaster>();
            services.AddSingleton<ILogEventEnricher, TraceIdEnricher>();

            services.AddSingleton<IBatchedLogEventSink>(serviceProvider => new OperationalLogSink(
                serviceProvider.GetRequiredService<DbDataSource>(),
                serviceProvider.GetRequiredService<OperationalLoggingOptions>(),
                serviceProvider.GetService<IOperationalLogBroadcaster>()));

            services.AddSingleton<IOperationalLogReadRepository>(serviceProvider => new OperationalLogReadRepository(
                serviceProvider.GetRequiredService<DbDataSource>(),
                serviceProvider.GetRequiredService<OperationalLoggingOptions>()));

            return services;
        }
    }
}
