using System.Net;
using System.Text.Json.Nodes;
using DotNet.Testcontainers.Configurations;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Scoping;
using Testcontainers.PostgreSql;

namespace SpaceTraders.API.Tests;

/// <summary>
/// Slice 2.15: the snapshot list says why each snapshot was taken, and for a discovery what was new, for the WebUI's
/// Snapshots page and Grafana's snapshots dashboard; the download is named after why.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SnapshotEndpointsIntegrationTests : IAsyncLifetime
{
    private const string AgentId = "SPECTER@2026-10-04";

    private PostgreSqlContainer _pg = default!;
    private bool _started;

    public async Task InitializeAsync()
    {
        Skip.IfNot(TestcontainersSettings.OS.DockerEndpointAuthConfig is not null, "Docker is not available – skipping integration tests.");

        _pg = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _pg.StartAsync();
        _started = true;

        await using var db = CreateContext();
        await SpaceTradersDatabaseInitializer.InitializeAsync(db);
        db.StartupSnapshots.Add(new StartupSnapshot
        {
            AgentId = AgentId,
            SnapshotJson = """{"Reason":"Startup"}""",
            CapturedAt = new DateTimeOffset(2026, 10, 04, 13, 06, 41, TimeSpan.Zero),
            IsInitialSnapshot = true,
            Reason = StartupSnapshot.StartupReason,
        });
        db.StartupSnapshots.Add(new StartupSnapshot
        {
            AgentId = AgentId,
            SnapshotJson = """{"Reason":"Discovery"}""",
            CapturedAt = new DateTimeOffset(2026, 10, 04, 14, 02, 11, TimeSpan.Zero),
            Reason = StartupSnapshot.DiscoveryReason,
            Discovered = "Goods: FAB_MATS (X1-FJ91-H59).",
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        if (_started)
        {
            await _pg.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task TheList_SaysWhyEachSnapshotWasTaken_NewestFirst()
    {
        using var factory = new ProductionHostFactory(_pg.GetConnectionString());
        using var client = factory.CreateClient();

        var list = JsonNode.Parse(await client.GetStringAsync("/status/startup-snapshots"))!.AsArray();

        list.Select(item => item!["reason"]!.GetValue<string>()).Should().Equal("Discovery", "Startup");
        list[0]!["discovered"]!.GetValue<string>().Should().Be("Goods: FAB_MATS (X1-FJ91-H59).");
        list[1]!["discovered"].Should().BeNull();
        list[1]!["isInitialSnapshot"]!.GetValue<bool>().Should().BeTrue();
    }

    [SkippableFact]
    public async Task TheDownload_IsNamedAfterWhyTheSnapshotWasTaken()
    {
        using var factory = new ProductionHostFactory(_pg.GetConnectionString());
        using var client = factory.CreateClient();
        var list = JsonNode.Parse(await client.GetStringAsync("/status/startup-snapshots"))!.AsArray();
        var discoveryId = list[0]!["id"]!.GetValue<int>();
        var startupId = list[1]!["id"]!.GetValue<int>();

        using var discovery = await client.GetAsync($"/status/startup-snapshots/{discoveryId}/download");
        using var startup = await client.GetAsync($"/status/startup-snapshots/{startupId}/download");

        discovery.StatusCode.Should().Be(HttpStatusCode.OK);
        discovery.Content.Headers.ContentDisposition!.FileName.Should().Be($"discovery-snapshot-{discoveryId}-20261004-140211.json");
        (await discovery.Content.ReadAsStringAsync()).Should().Be("""{"Reason":"Discovery"}""");
        startup.Content.Headers.ContentDisposition!.FileName.Should().Be($"startup-snapshot-{startupId}-20261004-130641-initial.json");
    }

    private SpaceTradersDbContext CreateContext()
    {
        var scope = new AgentDataScope();
        scope.Set(AgentId);
        return new SpaceTradersDbContext(new DbContextOptionsBuilder<SpaceTradersDbContext>().UseNpgsql(_pg.GetConnectionString()).Options, scope);
    }

    private sealed class ProductionHostFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            builder.UseSetting("Metrics:Port", "0");
            builder.ConfigureTestServices(services =>
            {
                // Only the framework's own hosted services run: no startup chain, no game API calls. The agent is the one
                // agent bootstrap would have picked.
                var appServices = services
                    .Where(d => d.ServiceType == typeof(IHostedService)
                        && d.ImplementationType?.Namespace?.StartsWith("SpaceTraders", StringComparison.Ordinal) == true)
                    .ToList();
                foreach (var service in appServices)
                {
                    services.Remove(service);
                }

                services.AddSingleton<IAgentDataScope>(_ =>
                {
                    var scope = new AgentDataScope();
                    scope.Set(AgentId);
                    return scope;
                });
            });
        }
    }
}
