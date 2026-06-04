using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace EasyReasy.Database.Logging
{
    /// <summary>
    /// Host-builder entry point that installs Serilog as the logging provider for the operational
    /// logging pipeline.
    /// </summary>
    public static class OperationalLoggingHostExtensions
    {
        /// <summary>
        /// Installs Serilog as the <c>ILogger</c> provider, wiring every registered
        /// <see cref="ILogEventEnricher"/> and the registered <see cref="IBatchedLogEventSink"/>
        /// (driven by Serilog's native batching), plus an optional parallel console sink. Must be
        /// called after <see cref="OperationalLoggingServiceCollectionExtensions.AddOperationalLogging"/>
        /// on the service collection; enrichers and the sink are resolved from the built provider.
        /// Existing <c>ILogger&lt;T&gt;</c> call sites are unchanged — Serilog sits behind them.
        /// </summary>
        public static IHostBuilder UseOperationalLogging(this IHostBuilder hostBuilder)
        {
            return hostBuilder.UseSerilog((context, services, loggerConfiguration) =>
            {
                OperationalLoggingOptions options = services.GetRequiredService<OperationalLoggingOptions>();
                IBatchedLogEventSink sink = services.GetRequiredService<IBatchedLogEventSink>();
                IEnumerable<ILogEventEnricher> enrichers = services.GetServices<ILogEventEnricher>();

                BatchingOptions batchOptions = new BatchingOptions
                {
                    BatchSizeLimit = options.BatchSizeLimit,
                    BufferingTimeLimit = options.BatchPeriod,
                    QueueLimit = options.QueueLimit,
                };

                loggerConfiguration.MinimumLevel.Is(options.MinimumLevel);

                foreach (KeyValuePair<string, LogEventLevel> levelOverride in options.MinimumLevelOverrides)
                {
                    loggerConfiguration.MinimumLevel.Override(levelOverride.Key, levelOverride.Value);
                }

                loggerConfiguration.Enrich.FromLogContext();

                foreach (ILogEventEnricher enricher in enrichers)
                {
                    loggerConfiguration.Enrich.With(enricher);
                }

                if (options.WriteToConsole)
                {
                    loggerConfiguration.WriteTo.Console();
                }

                loggerConfiguration.WriteTo.Sink(sink, batchOptions);
            });
        }
    }
}
