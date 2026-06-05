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
    /// Exercises the <c>/operational</c> read route's HTTP glue end-to-end over a <see cref="TestServer"/>:
    /// query string → <see cref="OperationalLogFilters"/> → page/perPage resolution → repository call.
    /// A capturing repository records exactly what the endpoint resolved and forwarded.
    /// </summary>
    public class OperationalLogReadEndpointTests
    {
        private sealed class CapturingReadRepository : IOperationalLogReadRepository
        {
            public OperationalLogFilters? LastFilters { get; private set; }
            public int LastPage { get; private set; }
            public int LastPerPage { get; private set; }

            public Task<PagedResult<OperationalLogEntry>> GetAsync(OperationalLogFilters filters, int page, int perPage)
            {
                LastFilters = filters;
                LastPage = page;
                LastPerPage = perPage;
                return Task.FromResult(new PagedResult<OperationalLogEntry>(0, new List<OperationalLogEntry>(), page, perPage));
            }
        }

        private static async Task<IHost> CreateHostAsync(IOperationalLogReadRepository repository)
        {
            return await new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost
                        .UseTestServer()
                        .ConfigureServices(services =>
                        {
                            services.AddRouting();
                            services.AddSingleton(repository);
                            // The group also wires the stream route, whose broadcaster parameter must be
                            // DI-resolvable for the group's delegates to build.
                            services.AddSingleton<IOperationalLogBroadcaster>(new InMemoryOperationalLogBroadcaster());
                        })
                        .Configure(app =>
                        {
                            app.UseRouting();
                            app.UseEndpoints(endpoints => endpoints.MapOperationalLogEndpoints());
                        });
                })
                .StartAsync();
        }

        [Fact]
        public async Task Read_ClampsOversizedPerPage_AndPassesFiltersThrough()
        {
            CapturingReadRepository repository = new CapturingReadRepository();
            using IHost host = await CreateHostAsync(repository);
            HttpClient client = host.GetTestClient();

            using HttpResponseMessage response = await client.GetAsync(
                "/api/admin/logs/operational?perPage=1000&level=Error&sourceContext=App.Accounts&from=2024-01-01T00:00:00Z");

            response.EnsureSuccessStatusCode();
            // perPage clamped to the maximum, page defaulted.
            Assert.Equal(OperationalLogPagination.MaxPerPage, repository.LastPerPage);
            Assert.Equal(1, repository.LastPage);
            // Query string mapped onto the filter object.
            Assert.Equal("Error", repository.LastFilters?.Level);
            Assert.Equal("App.Accounts", repository.LastFilters?.SourceContext);
            Assert.True(repository.LastFilters?.From.HasValue);
        }

        [Fact]
        public async Task Read_HonorsExplicitInBoundsPageAndPerPage()
        {
            CapturingReadRepository repository = new CapturingReadRepository();
            using IHost host = await CreateHostAsync(repository);
            HttpClient client = host.GetTestClient();

            using HttpResponseMessage response = await client.GetAsync(
                "/api/admin/logs/operational?page=3&perPage=25");

            response.EnsureSuccessStatusCode();
            Assert.Equal(3, repository.LastPage);
            Assert.Equal(25, repository.LastPerPage);
            Assert.Null(repository.LastFilters?.Level);
        }
    }
}
