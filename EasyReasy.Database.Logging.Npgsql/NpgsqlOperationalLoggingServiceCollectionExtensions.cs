using System.Data.Common;
using EasyReasy.Database.Logging;
using EasyReasy.Database.Logging.Broadcasting;
using EasyReasy.Database.Logging.Npgsql.Partitions;
using EasyReasy.Database.Logging.Npgsql.Sinks;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;

namespace EasyReasy.Database.Logging.Npgsql
{
    /// <summary>
    /// PostgreSQL registration entry point. Call after
    /// <c>AddOperationalLogging(...)</c> to upgrade the operational logging pipeline to the
    /// PostgreSQL fast path.
    /// </summary>
    public static class NpgsqlOperationalLoggingServiceCollectionExtensions
    {
        /// <summary>
        /// Replaces the agnostic <c>INSERT</c> sink with the binary <c>COPY</c> sink and, unless
        /// disabled via <see cref="NpgsqlOperationalLoggingOptions.MaintainPartitions"/>, registers
        /// the monthly partition-maintenance background service. The registered
        /// <see cref="DbDataSource"/> must be an <see cref="NpgsqlDataSource"/>.
        /// </summary>
        public static IServiceCollection AddNpgsqlOperationalLogging(
            this IServiceCollection services,
            Action<NpgsqlOperationalLoggingOptions>? configure = null)
        {
            NpgsqlOperationalLoggingOptions options = new NpgsqlOperationalLoggingOptions();
            configure?.Invoke(options);
            services.AddSingleton(options);

            // Last single-service registration wins on resolve, so this overrides the core's
            // portable sink registered by AddOperationalLogging.
            services.AddSingleton<IBatchedLogEventSink>(serviceProvider =>
            {
                DbDataSource dataSource = serviceProvider.GetRequiredService<DbDataSource>();
                if (dataSource is not NpgsqlDataSource npgsqlDataSource)
                {
                    throw new InvalidOperationException(
                        "AddNpgsqlOperationalLogging requires the registered DbDataSource to be an NpgsqlDataSource.");
                }

                return new NpgsqlOperationalLogSink(
                    npgsqlDataSource,
                    serviceProvider.GetRequiredService<OperationalLoggingOptions>(),
                    serviceProvider.GetService<IOperationalLogBroadcaster>());
            });

            if (options.MaintainPartitions)
            {
                services.TryAddSingleton(TimeProvider.System);
                services.AddHostedService<OperationalLogPartitionMaintenanceService>();
            }

            return services;
        }
    }
}
