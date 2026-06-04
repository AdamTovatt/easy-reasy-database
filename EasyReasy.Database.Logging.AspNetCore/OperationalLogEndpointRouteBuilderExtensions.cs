using System.Text.Json;
using System.Threading.Channels;
using EasyReasy.Database.Logging.Broadcasting;
using EasyReasy.Database.Logging.Models;
using EasyReasy.Database.Logging.Reading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EasyReasy.Database.Logging.AspNetCore
{
    /// <summary>
    /// Opt-in admin HTTP endpoints over the operational log: a paginated read and a Server-Sent
    /// Events live feed. Map them if you want the ready-made surface; otherwise consume
    /// <see cref="IOperationalLogReadRepository"/> and <see cref="IOperationalLogBroadcaster"/>
    /// directly and wire your own.
    /// </summary>
    public static class OperationalLogEndpointRouteBuilderExtensions
    {
        private static readonly JsonSerializerOptions StreamJsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        /// <summary>
        /// Maps <c>GET {prefix}/operational</c> (paginated, filterable read) and
        /// <c>GET {prefix}/operational/stream</c> (SSE live feed) under a route group, returned so
        /// the caller can add authorization, e.g.
        /// <c>app.MapOperationalLogEndpoints().RequireAuthorization("Admin")</c>.
        /// </summary>
        public static RouteGroupBuilder MapOperationalLogEndpoints(
            this IEndpointRouteBuilder endpoints,
            string prefix = "/api/admin/logs")
        {
            RouteGroupBuilder group = endpoints.MapGroup(prefix);

            group.MapGet("/operational", async (
                IOperationalLogReadRepository repository,
                string? level,
                string? sourceContext,
                DateTime? from,
                DateTime? to,
                int? page,
                int? perPage) =>
            {
                OperationalLogFilters filters = new OperationalLogFilters
                {
                    Level = level,
                    SourceContext = sourceContext,
                    From = from,
                    To = to,
                };

                return Results.Ok(await repository.GetAsync(filters, page is > 0 ? page.Value : 1, perPage is > 0 ? perPage.Value : 50));
            });

            group.MapGet("/operational/stream", async (
                HttpContext context,
                IOperationalLogBroadcaster broadcaster,
                CancellationToken cancellationToken) =>
            {
                context.Response.Headers.ContentType = "text/event-stream";
                context.Response.Headers.CacheControl = "no-cache";

                ChannelReader<OperationalLogEvent> reader = broadcaster.Subscribe();
                try
                {
                    await foreach (OperationalLogEvent logEvent in reader.ReadAllAsync(cancellationToken))
                    {
                        string json = JsonSerializer.Serialize(logEvent, StreamJsonOptions);
                        await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                        await context.Response.Body.FlushAsync(cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Client disconnected — fall through to unsubscribe.
                }
                finally
                {
                    broadcaster.Unsubscribe(reader);
                }
            });

            return group;
        }
    }
}
