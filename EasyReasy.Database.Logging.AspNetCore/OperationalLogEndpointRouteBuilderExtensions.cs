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

        // How often the SSE feed emits a keepalive comment on an idle connection. Comfortably under
        // the idle-connection timeouts proxies and load balancers typically impose (often 30–60s).
        private static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Maps <c>GET {prefix}/operational</c> (paginated, filterable read) and
        /// <c>GET {prefix}/operational/stream</c> (SSE live feed) under a route group, returned so
        /// the caller can add authorization, e.g.
        /// <c>app.MapOperationalLogEndpoints().RequireAuthorization("Admin")</c>.
        /// </summary>
        /// <param name="endpoints">The route builder to map onto.</param>
        /// <param name="prefix">The route prefix for the two endpoints.</param>
        /// <param name="heartbeatInterval">
        /// How often the SSE feed emits a keepalive comment on an idle connection. Defaults to 30
        /// seconds when <c>null</c>. Lower it when a reverse proxy closes idle connections faster
        /// than that; tests also use it to drive the idle path without waiting the full default.
        /// </param>
        public static RouteGroupBuilder MapOperationalLogEndpoints(
            this IEndpointRouteBuilder endpoints,
            string prefix = "/api/admin/logs",
            TimeSpan? heartbeatInterval = null)
        {
            RouteGroupBuilder group = endpoints.MapGroup(prefix);
            TimeSpan resolvedHeartbeatInterval = heartbeatInterval ?? DefaultHeartbeatInterval;

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

                int resolvedPage = OperationalLogPagination.ResolvePage(page);
                int resolvedPerPage = OperationalLogPagination.ResolvePerPage(perPage);

                return Results.Ok(await repository.GetAsync(filters, resolvedPage, resolvedPerPage));
            });

            group.MapGet("/operational/stream", (
                HttpContext context,
                IOperationalLogBroadcaster broadcaster,
                CancellationToken cancellationToken) =>
                StreamOperationalLogAsync(context, broadcaster, resolvedHeartbeatInterval, cancellationToken));

            return group;
        }

        /// <summary>
        /// Streams operational log events to the SSE response until the client disconnects: sets the
        /// event-stream headers (including the proxy anti-buffering header), flushes them up front,
        /// then writes each event as a <c>data:</c> frame and a <c>: heartbeat</c> comment on idle.
        /// </summary>
        private static async Task StreamOperationalLogAsync(
            HttpContext context,
            IOperationalLogBroadcaster broadcaster,
            TimeSpan heartbeatInterval,
            CancellationToken cancellationToken)
        {
            context.Response.Headers.ContentType = "text/event-stream; charset=utf-8";
            context.Response.Headers.CacheControl = "no-cache";
            // Tell nginx (and compatible reverse proxies) not to buffer the response, otherwise the
            // stream is held back proxy-side and never reaches the client live, heartbeat or not.
            // This is the header that actually makes SSE work behind a proxy.
            context.Response.Headers["X-Accel-Buffering"] = "no";

            ChannelReader<OperationalLogEvent> reader = broadcaster.Subscribe();
            try
            {
                // Flush the headers right away so the client sees an established stream instead of a
                // connection that appears to hang until the first event happens to arrive. Inside the
                // try so a disconnect during the flush is handled like any other.
                await context.Response.Body.FlushAsync(cancellationToken);

                // Race the next event against a heartbeat clock. Only the task that completed is
                // renewed, so at most one read-wait and one timer are ever outstanding.
                Task<bool> waitToRead = reader.WaitToReadAsync(cancellationToken).AsTask();
                Task heartbeat = Task.Delay(heartbeatInterval, cancellationToken);

                while (true)
                {
                    Task completed = await Task.WhenAny(waitToRead, heartbeat);

                    if (completed == heartbeat)
                    {
                        await heartbeat;
                        // An SSE comment line: ignored by the client, but enough traffic to keep
                        // proxies from dropping an idle connection and to surface a dead client.
                        await context.Response.WriteAsync(": heartbeat\n\n", cancellationToken);
                        await context.Response.Body.FlushAsync(cancellationToken);
                        heartbeat = Task.Delay(heartbeatInterval, cancellationToken);
                        continue;
                    }

                    if (!await waitToRead)
                    {
                        // The channel completed — end the stream. Defensive: in normal use only this
                        // handler's own finally completes the reader, so this is reached only if a
                        // future broadcaster were to complete it out from under us.
                        break;
                    }

                    while (reader.TryRead(out OperationalLogEvent? logEvent))
                    {
                        string json = JsonSerializer.Serialize(logEvent, StreamJsonOptions);
                        await context.Response.WriteAsync($"data: {json}\n\n", cancellationToken);
                    }

                    await context.Response.Body.FlushAsync(cancellationToken);
                    waitToRead = reader.WaitToReadAsync(cancellationToken).AsTask();
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected (request aborted) — fall through to unsubscribe.
            }
            catch (IOException)
            {
                // Broken pipe — the client went away mid-write. Routine for a long-lived stream, so
                // swallow it like a disconnect rather than letting it surface as an error.
            }
            finally
            {
                broadcaster.Unsubscribe(reader);
            }
        }
    }
}
