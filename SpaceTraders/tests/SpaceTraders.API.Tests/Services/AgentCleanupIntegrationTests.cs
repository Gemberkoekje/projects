using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;
using SpaceTraders.API.Configuration;
using SpaceTraders.API.Services;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Repositories;
using SpaceTraders.Infrastructure.Persistence.Scoping;
using SpaceTraders.Infrastructure.Persistence.Seed;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Exceptions;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Accounts;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Agents;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Contracts;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Factions;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Status;
using Testcontainers.PostgreSql;
using Wolverine;

namespace SpaceTraders.API.Tests.Services;

/// <summary>
/// B3: every server reset left the previous agent's rows behind for good, because everything is
/// scoped to the current agent. The bootstrap now deletes them, except the run summaries.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AgentCleanupIntegrationTests : IAsyncLifetime
{
    private const string OldAgent = "AGENT@2026-09-13";
    private const string NewAgent = "AGENT@2026-09-27";

    private PostgreSqlContainer _pg = default!;
    private bool _started;

    public async Task InitializeAsync()
    {
        Skip.IfNot(System.IO.File.Exists("/var/run/docker.sock") || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DOCKER_HOST")), "Docker is not available – skipping integration tests.");

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
    public async Task StartAsync_AfterAReset_DeletesThePreviousAgentsRows_ButKeepsItsRuns()
    {
        await SeedOldAgentAsync();
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        apiClient.GetMyAgentAsync(Arg.Any<CancellationToken>())
            .Returns<Task<Agent>>(_ => throw new SpaceTradersApiException(
                "Token reset_date does not match the server",
                HttpStatusCode.Unauthorized,
                "my/agent",
                null));
        apiClient.RegisterAsync(Arg.Any<RegisterRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateRegistration()));
        apiClient.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServerStatus { Status = "online", Version = "v2.3.0", ResetDate = "2026-09-27", Description = "SpaceTraders" }));
        await using var provider = BuildProvider(apiClient);

        await provider.GetRequiredService<AgentBootstrapService>().StartAsync(CancellationToken.None);

        var otherAgentsRows = await RowsOfOtherAgentsAsync(NewAgent);
        otherAgentsRows.Where(table => table.Value > 0).Should().BeEquivalentTo(new Dictionary<string, long> { ["runs"] = 1 });
        await using var db = CreateContext(NewAgent);
        (await db.Agents.SingleAsync()).Symbol.Should().Be("AGENT");
        (await db.Ships.SingleAsync()).Symbol.Should().Be("AGENT-1");
    }

    private async Task SeedOldAgentAsync()
    {
        await using var db = CreateContext(OldAgent);
        await SpaceTradersDatabaseInitializer.InitializeAsync(db);
        await DefaultSettingsSeed.SeedAsync(db);

        var now = TimeProvider.System.GetUtcNow();
        db.Credentials.Add(new StoredCredential { AgentId = OldAgent, Key = AgentTokenSelection.AgentTokenCredentialKey, Value = "old-token", StoredAt = now });
        db.Credentials.Add(new StoredCredential { AgentId = OldAgent, Key = AgentTokenSelection.ActiveAgentTokenKey, Value = "old-token", StoredAt = now });
        db.Agents.Add(new CachedAgent { AgentId = OldAgent, Symbol = "AGENT", StartingFaction = "COSMIC", Credits = 1_000_000 });
        db.Ships.Add(new CachedShip { AgentId = OldAgent, Symbol = "AGENT-1", ShipType = "SHIP_COMMAND" });
        db.MarketPriceSamples.Add(new MarketPriceSample { AgentId = OldAgent, WaypointSymbol = "X1-AB-1", GoodSymbol = "FOOD", ObservedAt = now });
        db.LedgerEntries.Add(new LedgerEntry { AgentId = OldAgent, ShipSymbol = "AGENT-1", OccurredAt = now, Category = LedgerCategory.FuelPurchase, Amount = -100 });
        db.ActivityLogs.Add(new ActivityLog { AgentId = OldAgent, ShipSymbol = "AGENT-1", EventType = "Arrived", Message = "Arrived", Timestamp = now });
        db.Runs.Add(new Run { Id = Guid.NewGuid(), AgentId = OldAgent, Name = "Run 1", StrategyLabel = "default", StartedAt = now, StartingCredits = 175_000 });
        await db.SaveChangesAsync();
    }

    private ServiceProvider BuildProvider(ISpaceTradersApiClient apiClient)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAgentDataScope, AgentDataScope>();
        services.AddDbContext<SpaceTradersDbContext>(options => options.UseNpgsql(_pg.GetConnectionString()));
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddSingleton(apiClient);
        services.AddSingleton<IAgentTokenProvider>(new AgentTokenProvider());
        services.AddSingleton(Substitute.For<IMessageBus>());
        services.AddSingleton<IOptions<SpaceTradersBootstrapOptions>>(Options.Create(new SpaceTradersBootstrapOptions
        {
            AgentName = "AGENT",
            AgentFaction = "COSMIC",
            AccountToken = "account-token",
        }));
        services.AddSingleton<AgentBootstrapService>();
        return services.BuildServiceProvider();
    }

    private SpaceTradersDbContext CreateContext(string agent)
    {
        var scope = new AgentDataScope();
        scope.Set(agent);
        var options = new DbContextOptionsBuilder<SpaceTradersDbContext>().UseNpgsql(_pg.GetConnectionString()).Options;
        return new SpaceTradersDbContext(options, scope);
    }

    /// <summary>Per table with an agent column: the number of rows that belong to another agent.</summary>
    private async Task<IReadOnlyDictionary<string, long>> RowsOfOtherAgentsAsync(string agent)
    {
        await using var db = CreateContext(agent);
        var tables = db.Model.GetEntityTypes()
            .Where(entity => entity.FindProperty(nameof(Run.AgentId)) is not null)
            .Select(entity => entity.GetTableName()!)
            .ToList();

        await using var connection = new NpgsqlConnection(_pg.GetConnectionString());
        await connection.OpenAsync();
        var counts = new Dictionary<string, long>();
        foreach (var table in tables)
        {
            await using var command = new NpgsqlCommand($"SELECT count(*) FROM \"{table}\" WHERE \"AgentId\" <> @agent", connection);
            command.Parameters.AddWithValue("agent", agent);
            counts[table] = (long)(await command.ExecuteScalarAsync())!;
        }

        return counts;
    }

    private static RegisterResponseData CreateRegistration() => new()
    {
        Token = "new-token",
        Agent = new Agent { Symbol = "AGENT", StartingFaction = "COSMIC", Headquarters = "X1-HQ-A1", Credits = 175_000, ShipCount = 1 },
        Faction = new Faction { Symbol = "COSMIC", Name = "Cosmic", Description = "Faction", Headquarters = "X1-HQ-A1", IsRecruiting = true },
        Contract = new Contract { Id = "contract-1", FactionSymbol = "COSMIC", Type = "PROCUREMENT" },
        Ships =
        [
            new Ship
            {
                Symbol = "AGENT-1",
                Nav = new ShipNav { SystemSymbol = "X1-HQ", WaypointSymbol = "X1-HQ-A1", Status = "DOCKED", FlightMode = "CRUISE" },
                Fuel = new ShipFuel { Current = 400, Capacity = 400 },
            },
        ],
    };
}
