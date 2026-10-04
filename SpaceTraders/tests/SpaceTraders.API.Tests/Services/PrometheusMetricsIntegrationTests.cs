using DotNet.Testcontainers.Configurations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.API.Services;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Naming;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Scoping;
using Testcontainers.PostgreSql;

namespace SpaceTraders.API.Tests.Services;

/// <summary>
/// Slice 2.16: each ship's ledger, summed by category in one query, against PostgreSQL 16 as the cluster runs it: the
/// in-memory database doesn't show that the grouping translates, nor that another agent's rows count for no ship.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PrometheusMetricsIntegrationTests : IAsyncLifetime
{
    private const string OldAgent = "AGENT@2026-09-13";
    private const string ActiveAgent = "AGENT@2026-09-27";

    private PostgreSqlContainer _pg = default!;
    private bool _started;

    public async Task InitializeAsync()
    {
        // Testcontainers finds Docker the way it will start the container: DOCKER_HOST, the Unix socket, or
        // Docker Desktop's named pipe on Windows (B36).
        Skip.IfNot(TestcontainersSettings.OS.DockerEndpointAuthConfig is not null, "Docker is not available – skipping integration tests.");

        _pg = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _pg.StartAsync();
        _started = true;
    }

    public async Task DisposeAsync()
    {
        if (_started)
        {
            await _pg.DisposeAsync();
        }
    }

    [SkippableFact]
    public async Task SampleAsync_SumsEachShipsLedger_ByCategory_ForTheActiveAgentOnly()
    {
        var now = TimeProvider.System.GetUtcNow();
        await using (var db = CreateContext(ActiveAgent))
        {
            await SpaceTradersDatabaseInitializer.InitializeAsync(db);
            db.Ships.Add(new CachedShip { AgentId = ActiveAgent, Symbol = "AGENT-1", ShipType = "COMMAND", Status = "DOCKED" });
            db.Ships.Add(new CachedShip { AgentId = ActiveAgent, Symbol = "AGENT-3", ShipType = "SHIP_LIGHT_HAULER", Status = "DOCKED" });
            db.LedgerEntries.Add(Ledger(ActiveAgent, "AGENT-1", LedgerCategory.TradeBuy, -9_000, now));
            db.LedgerEntries.Add(Ledger(ActiveAgent, "AGENT-1", LedgerCategory.TradeSell, 7_000, now));
            db.LedgerEntries.Add(Ledger(ActiveAgent, "AGENT-1", LedgerCategory.TradeSell, 6_500, now));
            db.LedgerEntries.Add(Ledger(ActiveAgent, "AGENT-1", LedgerCategory.FuelPurchase, -120, now));
            db.LedgerEntries.Add(Ledger(ActiveAgent, "AGENT-3", LedgerCategory.ShipPurchase, -70_000, now));
            db.LedgerEntries.Add(Ledger(ActiveAgent, "AGENT-3", LedgerCategory.MountPurchase, -3_000, now));
            db.LedgerEntries.Add(Ledger(ActiveAgent, "AGENT", LedgerCategory.ContractPayout, 23_000, now));

            // The agent of the reset before had ships of the same symbols.
            db.LedgerEntries.Add(Ledger(OldAgent, "AGENT-1", LedgerCategory.TradeSell, 1_000_000, now));
            db.LedgerEntries.Add(Ledger(OldAgent, "AGENT-3", LedgerCategory.ShipPurchase, -45_000, now));
            await db.SaveChangesAsync();
        }

        var metrics = Substitute.For<IAutomationMetrics>();
        IReadOnlyCollection<ShipMetricsSample> ships = [];
        metrics.When(m => m.Fleet(Arg.Any<IReadOnlyCollection<ShipMetricsSample>>(), Arg.Any<DateTimeOffset>()))
            .Do(call => ships = call.Arg<IReadOnlyCollection<ShipMetricsSample>>());
        await using var provider = BuildProvider();

        using var service = new PrometheusMetricsService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            metrics,
            new ShipStateJournal(NullLogger<ShipStateJournal>.Instance),
            new PurchaseNeeds(),
            new FullHoldSavings(),
            new ShipNameBook(new ActiveReset(Scope(ActiveAgent))),
            NullLogger<PrometheusMetricsService>.Instance);
        await service.SampleAsync(CancellationToken.None);

        ships.Single(s => s.Ship == "AGENT-1").Ledger.Should().BeEquivalentTo(new Dictionary<string, long>
        {
            ["TradeBuy"] = -9_000,
            ["TradeSell"] = 13_500,
            ["FuelPurchase"] = -120,
        });
        ships.Single(s => s.Ship == "AGENT-3").Ledger.Should().BeEquivalentTo(new Dictionary<string, long>
        {
            ["ShipPurchase"] = -70_000,
            ["MountPurchase"] = -3_000,
        });
        ships.Select(s => (s.Ship, s.Value)).Should().BeEquivalentTo(new[]
        {
            ("AGENT-1", 0L),
            ("AGENT-3", 73_000L),
        });
    }

    private static LedgerEntry Ledger(string agentId, string ship, LedgerCategory category, long amount, DateTimeOffset at)
        => new() { AgentId = agentId, ShipSymbol = ship, Category = category, Amount = amount, OccurredAt = at };

    private static AgentDataScope Scope(string agentId)
    {
        var scope = new AgentDataScope();
        scope.Set(agentId);
        return scope;
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAgentDataScope>(_ => Scope(ActiveAgent));
        services.AddDbContext<SpaceTradersDbContext>(options => options.UseNpgsql(_pg.GetConnectionString()));
        return services.BuildServiceProvider();
    }

    private SpaceTradersDbContext CreateContext(string agent)
    {
        var options = new DbContextOptionsBuilder<SpaceTradersDbContext>().UseNpgsql(_pg.GetConnectionString()).Options;
        return new SpaceTradersDbContext(options, Scope(agent));
    }
}
