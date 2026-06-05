using System.Text;
using EasyReasy.Database.Logging.Broadcasting;
using EasyReasy.Database.Logging.Models;
using EasyReasy.Database.Logging.Reading;
using EasyReasy.Database.Pagination;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EasyReasy.Database.Logging.AspNetCore.Tests
{
    /// <summary>
    /// Exercises the SSE live-feed endpoint end-to-end over an in-process <see cref="TestServer"/>:
    /// the event-stream headers (including the proxy anti-buffering header), the idle heartbeat
    /// frame, and a published event arriving as a <c>data:</c> frame. The heartbeat interval is
    /// driven short via the <c>MapOperationalLogEndpoints</c> seam so the idle path is reachable.
    /// </summary>
    public class OperationalLogStreamEndpointTests
    {
        private const string StreamPath = "/api/admin/logs/operational/stream";
        private static readonly TimeSpan ShortHeartbeat = TimeSpan.FromMilliseconds(150);

        // The endpoint group also wires the /operational read route, whose repository parameter must
        // be DI-resolvable for the group's delegates to build — even though these tests only hit the
        // stream route. A stub satisfies that without a database.
        private sealed class StubReadRepository : IOperationalLogReadRepository
        {
            public Task<PagedResult<OperationalLogEntry>> GetAsync(OperationalLogFilters filters, int page, int perPage)
            {
                return Task.FromResult(new PagedResult<OperationalLogEntry>(0, new List<OperationalLogEntry>(), page, perPage));
            }
        }

        private static async Task<IHost> CreateHostAsync(IOperationalLogBroadcaster broadcaster)
        {
            return await new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost
                        .UseTestServer()
                        .ConfigureServices(services =>
                        {
                            services.AddRouting();
                            services.AddSingleton(broadcaster);
                            services.AddSingleton<IOperationalLogReadRepository, StubReadRepository>();
                        })
                        .Configure(app =>
                        {
                            app.UseRouting();
                            app.UseEndpoints(endpoints => endpoints.MapOperationalLogEndpoints(heartbeatInterval: ShortHeartbeat));
                        });
                })
                .StartAsync();
        }

        [Fact]
        public async Task Stream_SetsEventStreamHeaders_AndEmitsHeartbeatWhenIdle()
        {
            InMemoryOperationalLogBroadcaster broadcaster = new InMemoryOperationalLogBroadcaster();
            using IHost host = await CreateHostAsync(broadcaster);
            HttpClient client = host.GetTestClient();

            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using HttpResponseMessage response = await client.GetAsync(
                StreamPath, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
            Assert.True(response.Headers.CacheControl?.NoCache);
            Assert.True(response.Headers.TryGetValues("X-Accel-Buffering", out IEnumerable<string>? accel));
            Assert.Equal("no", Assert.Single(accel!));

            // No events are published, so the only thing that can arrive is the idle heartbeat.
            await using Stream stream = await response.Content.ReadAsStreamAsync(cts.Token);
            string received = await ReadUntilAsync(stream, ": heartbeat", cts.Token);
            Assert.Contains(": heartbeat", received);
        }

        [Fact]
        public async Task Stream_PublishedEvent_ArrivesAsCamelCaseDataFrame()
        {
            InMemoryOperationalLogBroadcaster broadcaster = new InMemoryOperationalLogBroadcaster();
            using IHost host = await CreateHostAsync(broadcaster);
            HttpClient client = host.GetTestClient();

            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using HttpResponseMessage response = await client.GetAsync(
                StreamPath, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            await using Stream stream = await response.Content.ReadAsStreamAsync(cts.Token);

            // Reading one heartbeat first proves the handler has subscribed and entered its loop, so
            // the publish below cannot race ahead of the subscription and be dropped.
            await ReadUntilAsync(stream, ": heartbeat", cts.Token);

            broadcaster.Publish(new OperationalLogEvent { Level = "Information", Message = "hello-sse" });

            string frame = await ReadUntilAsync(stream, "hello-sse", cts.Token);
            Assert.Contains("data:", frame);
            Assert.Contains("\"message\":\"hello-sse\"", frame);
            Assert.Contains("\"level\":\"Information\"", frame);
        }

        // Reads from the SSE stream until the accumulated text contains the marker, then returns it.
        private static async Task<string> ReadUntilAsync(Stream stream, string marker, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[512];
            StringBuilder accumulated = new StringBuilder();

            while (!accumulated.ToString().Contains(marker, StringComparison.Ordinal))
            {
                int read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    throw new InvalidOperationException($"Stream ended before '{marker}' appeared. Received: {accumulated}");
                }

                accumulated.Append(Encoding.UTF8.GetString(buffer, 0, read));
            }

            return accumulated.ToString();
        }
    }
}
