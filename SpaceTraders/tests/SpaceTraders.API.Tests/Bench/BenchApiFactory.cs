using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;

namespace SpaceTraders.API.Tests.Bench;

/// <summary>
/// The API host as the cluster runs it, on a local copy of the cluster's database (<c>tools/bench/copy_world.py</c>), for the
/// tick bench (slice 6.60). Nothing reaches the game: the port is a stub that only serves the production chains saved beside
/// the copy, and the API client and token provider are stubs. No background service starts, the game loop included: the
/// bench runs what it times itself.
/// </summary>
/// <param name="connectionString">The local copy.</param>
/// <param name="chains">The game's production chains, as <c>GET market/supply-chain</c> answers them.</param>
internal sealed class BenchApiFactory(string connectionString, IReadOnlyDictionary<string, IReadOnlyList<string>> chains)
    : WebApplicationFactory<Program>
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Never Development: it reads the API project's user secrets, whose connection string is the cluster's shared
        // Postgres (seen on 2026-11-11: the first run reached for its database). And the settings go in last, so they win.
        builder.UseEnvironment("Bench");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connectionString,
            ["SPACETRADERS_INTERNAL_API_KEY"] = "bench",
            ["Metrics:Port"] = "0",
            ["Serilog:MinimumLevel:Default"] = "Warning",
        }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISpaceTradersApiClient>();
            services.AddSingleton(Substitute.For<ISpaceTradersApiClient>());

            services.RemoveAll<ISpaceTradersPort>();
            services.AddScoped(_ => StubPort());

            services.RemoveAll<IAgentTokenProvider>();
            services.AddSingleton(Substitute.For<IAgentTokenProvider>());

            // The bot's own background services, the deferred startup that starts the game loop among them. Wolverine's
            // stays, so the bus works.
            foreach (var hosted in services
                .Where(service => service.ServiceType == typeof(IHostedService)
                    && service.ImplementationType?.Namespace?.StartsWith("SpaceTraders", StringComparison.Ordinal) == true)
                .ToList())
            {
                services.Remove(hosted);
            }
        });
    }

    private ISpaceTradersPort StubPort()
    {
        var port = Substitute.For<ISpaceTradersPort>();
        port.GetSupplyChainAsync(Arg.Any<CancellationToken>()).Returns(chains);
        return port;
    }
}
