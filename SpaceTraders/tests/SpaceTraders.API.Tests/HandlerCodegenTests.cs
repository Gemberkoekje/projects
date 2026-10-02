using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpaceTraders.Application.Health;
using Wolverine.Runtime;

namespace SpaceTraders.API.Tests;

/// <summary>
/// B42: on the cluster the <c>RepeatingError</c> rule raised an anomaly in the bot's second minute.
/// Wolverine compiles a message handler when its first message comes, and logged a warning for every
/// dependency it resolves from the container ("Utilizing service location for ..."): eleven warnings
/// with one template in the first minute, over the rule's five. Service location is how this
/// composition works (factory registrations, typed HttpClients), so the warnings never led anywhere.
/// </summary>
public sealed class HandlerCodegenTests(ProductionCompositionFactory factory) : IClassFixture<ProductionCompositionFactory>
{
    [Fact]
    public void CompilingEveryHandler_LogsNoWarning()
    {
        var runtime = (WolverineRuntime)factory.Services.GetRequiredService<IWolverineRuntime>();
        var errors = factory.Services.GetRequiredService<ErrorLog>();
        var since = TimeProvider.System.GetUtcNow();

        foreach (var chain in runtime.Handlers.Chains)
        {
            // What the first message of each type does on the cluster.
            runtime.Handlers.HandlerFor(chain.MessageType).Should().NotBeNull();
        }

        errors.Since(since).Select(statement => $"{statement.Count}x {statement.LastMessage}")
            .Should().BeEmpty("a handler's first message is no reason for a warning, and the RepeatingError rule counts every one");
    }
}

/// <summary>
/// The API host as production composes it: no substitutes, so Wolverine sees the real registrations.
/// Nothing of the bot's own is started, and nothing connects to the database or the game.
/// </summary>
public sealed class ProductionCompositionFactory : WebApplicationFactory<Program>
{
    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=localhost;Database=test;Username=test;Password=test");
        builder.UseSetting("SPACETRADERS_INTERNAL_API_KEY", "test-key-codegen");
        builder.UseSetting("Metrics:Port", "0");

        builder.ConfigureTestServices(services =>
        {
            var appServices = services
                .Where(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService) &&
                    descriptor.ImplementationType?.Namespace?.StartsWith("SpaceTraders", StringComparison.Ordinal) == true)
                .ToList();

            foreach (var descriptor in appServices)
            {
                services.Remove(descriptor);
            }
        });
    }
}
