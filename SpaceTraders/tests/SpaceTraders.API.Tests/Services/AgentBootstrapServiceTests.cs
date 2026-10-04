using System.Net;
using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SpaceTraders.API.Configuration;
using SpaceTraders.API.Services;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Repositories;
using SpaceTraders.Infrastructure.Persistence.Scoping;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Exceptions;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Accounts;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Agents;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Common;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Contracts;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Factions;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Status;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Systems;
using Wolverine;

namespace SpaceTraders.API.Tests.Services;

public sealed class AgentBootstrapServiceTests
{
    [Fact]
    public async Task StartAsync_WhenConfiguredNameDiffersFromActiveTokenAgent_RegistersNewAgent()
    {
        var agentTokenProvider = new AgentTokenProvider();
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        var settings = Substitute.For<ISettingsRepository>();
        var bus = Substitute.For<IMessageBus>();

        var activeAgentCalls = 0;
        apiClient.GetMyAgentAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                activeAgentCalls++;
                return Task.FromResult(activeAgentCalls == 1
                    ? new Agent
                    {
                        Symbol = "OLD-AGENT",
                        StartingFaction = "COSMIC",
                    }
                    : new Agent
                    {
                        Symbol = "NEW-AGENT",
                        StartingFaction = "COSMIC",
                    });
            });

        apiClient.RegisterAsync(Arg.Any<RegisterRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateRegistrationResponse("NEW-AGENT", "new-token")));
        StubServerResetDate(apiClient);

        using var provider = await BuildProvider(
            databaseName: Guid.NewGuid().ToString("N"),
            initialAgentId: null,
            configureDb: db => SeedActiveTokenCredential(db, "OLD-AGENT@2026-09-27", "old-token"),
            configureServices: services =>
            {
                services.AddSingleton<IAgentTokenProvider>(agentTokenProvider);
                services.AddSingleton(apiClient);
                services.AddSingleton(settings);
                services.AddSingleton(bus);
                services.AddSingleton<IOptions<SpaceTradersBootstrapOptions>>(
                    Options.Create(new SpaceTradersBootstrapOptions
                    {
                        AgentName = "NEW-AGENT",
                        AgentFaction = "COSMIC",
                        AccountToken = "account-token",
                    }));
            });

        var service = provider.GetRequiredService<AgentBootstrapService>();

        await service.StartAsync(CancellationToken.None);

        await apiClient.Received(1).RegisterAsync(
            Arg.Is<RegisterRequest>(r => r.Symbol == "NEW-AGENT" && r.Faction == "COSMIC"),
            Arg.Any<CancellationToken>());
        agentTokenProvider.Token.Should().Be("new-token");

        provider.GetRequiredService<IAgentDataScope>().AgentId.Should().Be("NEW-AGENT@2026-09-27");
        await using var verifyScope = provider.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();

        var activeToken = await AgentTokenSelection.GetActiveTokenAsync(db, CancellationToken.None);
        activeToken.Should().Be("new-token");
        (await db.Agents.AsNoTracking().AnyAsync(a => a.AgentId == "NEW-AGENT@2026-09-27" && a.Symbol == "NEW-AGENT")).Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_WhenTokenMatchesConfiguredName_DoesNotRegisterNewAgent()
    {
        var agentTokenProvider = new AgentTokenProvider();
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        var settings = Substitute.For<ISettingsRepository>();
        var bus = Substitute.For<IMessageBus>();

        apiClient.GetMyAgentAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new Agent
            {
                Symbol = "MATCHING-AGENT",
                StartingFaction = "COSMIC",
            }));
        StubServerResetDate(apiClient);

        using var provider = await BuildProvider(
            databaseName: Guid.NewGuid().ToString("N"),
            initialAgentId: null,
            configureDb: db => SeedActiveTokenCredential(db, "MATCHING-AGENT@2026-09-27", "matching-token"),
            configureServices: services =>
            {
                services.AddSingleton<IAgentTokenProvider>(agentTokenProvider);
                services.AddSingleton(apiClient);
                services.AddSingleton(settings);
                services.AddSingleton(bus);
                services.AddSingleton<IOptions<SpaceTradersBootstrapOptions>>(
                    Options.Create(new SpaceTradersBootstrapOptions
                    {
                        AgentName = "MATCHING-AGENT",
                        AgentFaction = "COSMIC",
                        AccountToken = "account-token",
                    }));
            });

        var service = provider.GetRequiredService<AgentBootstrapService>();

        await service.StartAsync(CancellationToken.None);

        await apiClient.DidNotReceive().RegisterAsync(Arg.Any<RegisterRequest>(), Arg.Any<CancellationToken>());
        agentTokenProvider.Token.Should().Be("matching-token");
    }

    [Fact]
    public async Task StartAsync_RecordsWhenTheServerResetsNext()
    {
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        var metrics = Substitute.For<IAutomationMetrics>();
        apiClient.GetMyAgentAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new Agent { Symbol = "MATCHING-AGENT", StartingFaction = "COSMIC" }));
        apiClient.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServerStatus
            {
                Status = "SpaceTraders is currently online",
                Version = "v2.3.0",
                ResetDate = "2026-09-27",
                Description = "SpaceTraders",
                ServerResets = new ServerResetInfo { Next = "2026-10-04T16:00:00.000Z", Frequency = "weekly" },
            }));

        using var provider = await BuildProvider(
            databaseName: Guid.NewGuid().ToString("N"),
            initialAgentId: null,
            configureDb: db => SeedActiveTokenCredential(db, "MATCHING-AGENT@2026-09-27", "matching-token"),
            configureServices: services =>
            {
                services.AddSingleton<IAgentTokenProvider>(new AgentTokenProvider());
                services.AddSingleton(apiClient);
                services.AddSingleton(Substitute.For<ISettingsRepository>());
                services.AddSingleton(Substitute.For<IMessageBus>());
                services.AddSingleton(metrics);
                services.AddSingleton<IOptions<SpaceTradersBootstrapOptions>>(
                    Options.Create(new SpaceTradersBootstrapOptions { AgentName = "MATCHING-AGENT", AgentFaction = "COSMIC" }));
            });

        await provider.GetRequiredService<AgentBootstrapService>().StartAsync(CancellationToken.None);

        metrics.Received(1).NextServerReset(new DateTimeOffset(2026, 10, 04, 16, 00, 00, TimeSpan.Zero));
    }

    [Fact]
    public async Task StartAsync_WhenTokenResetMismatch_PublishesResetEventAndRegistersNewAgent()
    {
        var agentTokenProvider = new AgentTokenProvider();
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        var settings = Substitute.For<ISettingsRepository>();
        var bus = Substitute.For<IMessageBus>();

        apiClient.GetMyAgentAsync(Arg.Any<CancellationToken>())
            .Returns<Task<Agent>>(_ => throw new SpaceTradersApiException(
                "Token reset_date does not match the server",
                HttpStatusCode.Unauthorized,
                "my/agent",
                null));

        apiClient.RegisterAsync(Arg.Any<RegisterRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateRegistrationResponse("RESET-NEW", "reset-new-token")));
        StubServerResetDate(apiClient);

        using var provider = await BuildProvider(
            databaseName: Guid.NewGuid().ToString("N"),
            initialAgentId: null,
            configureDb: db => SeedActiveTokenCredential(db, "RESET-NEW@2026-09-13", "stale-token"),
            configureServices: services =>
            {
                services.AddSingleton<IAgentTokenProvider>(agentTokenProvider);
                services.AddSingleton(apiClient);
                services.AddSingleton(settings);
                services.AddSingleton(bus);
                services.AddSingleton<IOptions<SpaceTradersBootstrapOptions>>(
                    Options.Create(new SpaceTradersBootstrapOptions
                    {
                        AgentName = "RESET-NEW",
                        AgentFaction = "COSMIC",
                        AccountToken = "account-token",
                    }));
            });

        var service = provider.GetRequiredService<AgentBootstrapService>();

        await service.StartAsync(CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<TokenResetMismatchDetectedEvent>(e => e.Source == "active"),
            Arg.Any<DeliveryOptions>());
        await settings.Received().SetAsync("Runtime.TokenResetMismatchDetected", "true", Arg.Any<CancellationToken>());
        await settings.Received().SetAsync("Runtime.Alert.TokenResetMismatch", "true", Arg.Any<CancellationToken>());
        await apiClient.Received(1).RegisterAsync(Arg.Any<RegisterRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_WhenStartupSyncChecksCache_OnlyUsesActiveAgentData()
    {
        var now = TimeProvider.System.GetUtcNow();
        var agentTokenProvider = new AgentTokenProvider();
        agentTokenProvider.Set("active-token");
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        var bus = Substitute.For<IMessageBus>();

        apiClient.GetMyAgentAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new Agent
            {
                Symbol = "ACTIVE-AGENT",
                StartingFaction = "COSMIC",
                Credits = 123,
                ShipCount = 1,
            }));

        apiClient.GetMyShipsAsync(1, 20, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PagedApiResponse<Ship>
            {
                Data = new[]
                {
                    new Ship
                    {
                        Symbol = "SHIP-1",
                        Nav = new ShipNav
                        {
                            SystemSymbol = "X1-NEW",
                            WaypointSymbol = "X1-NEW-A1",
                            Status = "IN_ORBIT",
                            FlightMode = "CRUISE",
                        },
                        Fuel = new ShipFuel
                        {
                            Current = 100,
                            Capacity = 100,
                        },
                        Cargo = new FleetShipCargo
                        {
                            Units = 0,
                            Capacity = 40,
                        },
                    },
                },
                Meta = new Meta
                {
                    Total = 1,
                    Page = 1,
                    Limit = 20,
                },
            }));

        apiClient.GetSystemAsync("X1-NEW", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new SystemInfo
            {
                Symbol = "X1-NEW",
                SectorSymbol = "X1",
                Type = "NEUTRON_STAR",
                X = 10,
                Y = 20,
            }));

        apiClient.GetWaypointsAsync("X1-NEW", 1, 20, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new PagedApiResponse<Waypoint>
                {
                    Data = new[]
                    {
                        new Waypoint
                        {
                            Symbol = "X1-NEW-A1",
                            SystemSymbol = "X1-NEW",
                            Type = "PLANET",
                            X = 1,
                            Y = 2,
                            Traits = Array.Empty<WaypointTrait>(),
                        },
                    },
                    Meta = new Meta
                    {
                        Total = 1,
                        Page = 1,
                        Limit = 20,
                    },
                }),
                Task.FromResult(new PagedApiResponse<Waypoint>
                {
                    Data = Array.Empty<Waypoint>(),
                    Meta = new Meta
                    {
                        Total = 1,
                        Page = 2,
                        Limit = 20,
                    },
                }));

        apiClient.GetMyContractsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PagedApiResponse<Contract>
            {
                Data = Array.Empty<Contract>(),
                Meta = new Meta
                {
                    Total = 0,
                    Page = 1,
                    Limit = 20,
                },
            }));

        bus.InvokeAsync(Arg.Any<object>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        using var provider = await BuildProvider(
            databaseName: Guid.NewGuid().ToString("N"),
            initialAgentId: "ACTIVE-AGENT@2026-09-27",
            configureDb: async db =>
            {
                db.Systems.Add(new CachedSystem
                {
                    AgentId = "OTHER-AGENT@2026-09-13",
                    Symbol = "X1-NEW",
                    SectorSymbol = "X1",
                    Type = "OLD",
                    X = 99,
                    Y = 99,
                    LastObservedAt = now,
                });
                db.Waypoints.Add(new CachedWaypoint
                {
                    AgentId = "OTHER-AGENT@2026-09-13",
                    Symbol = "X1-NEW-OLD",
                    SystemSymbol = "X1-NEW",
                    Type = "PLANET",
                    X = 99,
                    Y = 99,
                    HasMarket = false,
                    HasShipyard = false,
                    LastObservedAt = now,
                });
                await db.SaveChangesAsync();
            },
            configureServices: services =>
            {
                services.AddSingleton<IAgentTokenProvider>(agentTokenProvider);
                services.AddSingleton(apiClient);
                services.AddSingleton(bus);
            });

        var service = new StartupSyncService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StartupSyncService>.Instance);

        await service.StartAsync(CancellationToken.None);

        await apiClient.Received(1).GetSystemAsync("X1-NEW", Arg.Any<CancellationToken>());
        await apiClient.Received(1).GetWaypointsAsync("X1-NEW", 1, 20, Arg.Any<CancellationToken>());

        await using var verifyScope = provider.CreateAsyncScope();
        var dbContext = verifyScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();

        (await dbContext.Systems.AsNoTracking().AnyAsync(s => s.AgentId == "ACTIVE-AGENT@2026-09-27" && s.Symbol == "X1-NEW")).Should().BeTrue();
        (await dbContext.Waypoints.AsNoTracking().AnyAsync(w => w.AgentId == "ACTIVE-AGENT@2026-09-27" && w.SystemSymbol == "X1-NEW")).Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_StoresTheAgentTokenOnlyInTheCredentialsTable()
    {
        // B4: every table had the agent token (a JWT of about 1 KB) in its key.
        const string token = "registered-agent-token";
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        apiClient.RegisterAsync(Arg.Any<RegisterRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateRegistrationResponse("NEW-AGENT", token)));
        StubServerResetDate(apiClient);

        using var provider = await BuildProvider(
            databaseName: Guid.NewGuid().ToString("N"),
            initialAgentId: null,
            configureServices: services => AddRegistrationServices(services, apiClient));

        await provider.GetRequiredService<AgentBootstrapService>().StartAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
        (await TablesHoldingAsync(db, token)).Should().Equal("stored_credentials");
    }

    [Fact]
    public async Task StartAsync_AfterRegisteringANewAgent_ItHasTheDefaultSettings()
    {
        // B26: resolving the real API client also creates the scope's DbContext, because the
        // client records endpoint usage through it. The substitute does the same.
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        apiClient.RegisterAsync(Arg.Any<RegisterRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateRegistrationResponse("NEW-AGENT", "registered-agent-token")));
        StubServerResetDate(apiClient);

        using var provider = await BuildProvider(
            databaseName: Guid.NewGuid().ToString("N"),
            initialAgentId: null,
            configureServices: services =>
            {
                AddRegistrationServices(services, apiClient);
                services.RemoveAll<ISpaceTradersApiClient>();
                services.AddScoped(serviceProvider =>
                {
                    _ = serviceProvider.GetRequiredService<SpaceTradersDbContext>();
                    return apiClient;
                });
            });

        await provider.GetRequiredService<AgentBootstrapService>().StartAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        (await settings.GetRawAsync("Automation.Enabled")).Should().Be("true");
        (await settings.GetRawAsync("Runtime.TokenResetMismatchDetected")).Should().Be("false");
    }

    [Fact]
    public async Task StartAsync_AfterAServerReset_TheNewAgentStartsWithTheValuesChosenForTheNextRuns()
    {
        // D69: the old agent ran with one value; the next run was given another, and the trading plan off.
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        apiClient.RegisterAsync(Arg.Any<RegisterRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateRegistrationResponse("NEW-AGENT", "registered-agent-token")));
        StubServerResetDate(apiClient);

        using var provider = await BuildProvider(
            databaseName: Guid.NewGuid().ToString("N"),
            initialAgentId: null,
            configureDb: async db =>
            {
                db.Settings.Add(new AgentSetting
                {
                    AgentId = "NEW-AGENT@2026-09-20",
                    Key = "Automation.MiningShipPercentage",
                    Value = "0.5",
                    Type = "decimal",
                    Description = string.Empty,
                });
                db.NextRunSettings.Add(new NextRunSetting { Key = "Automation.MiningShipPercentage", Value = "0.6" });
                db.NextRunSettings.Add(new NextRunSetting { Key = "Automation.Plan.Trading.Enabled", Value = "false" });
                await db.SaveChangesAsync();
            },
            configureServices: services => AddRegistrationServices(services, apiClient));

        await provider.GetRequiredService<AgentBootstrapService>().StartAsync(CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        (await settings.GetRawAsync("Automation.MiningShipPercentage")).Should().Be("0.6");
        (await settings.GetRawAsync("Automation.Plan.Trading.Enabled")).Should().Be("false");
        (await settings.GetRawAsync("Automation.Plan.Mining.Enabled")).Should().Be("true");
        (await settings.GetNextRunSettingsAsync()).Where(setting => !setting.IsDefault).Select(setting => setting.Key)
            .Should().BeEquivalentTo("Automation.MiningShipPercentage", "Automation.Plan.Trading.Enabled");
    }

    [Fact]
    public async Task StartAsync_StoresANewAgentUnderItsSymbolAndTheServersResetDate()
    {
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        apiClient.RegisterAsync(Arg.Any<RegisterRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateRegistrationResponse("NEW-AGENT", "registered-agent-token")));
        StubServerResetDate(apiClient);

        using var provider = await BuildProvider(
            databaseName: Guid.NewGuid().ToString("N"),
            initialAgentId: null,
            configureServices: services => AddRegistrationServices(services, apiClient));

        await provider.GetRequiredService<AgentBootstrapService>().StartAsync(CancellationToken.None);

        provider.GetRequiredService<IAgentDataScope>().AgentId.Should().Be("NEW-AGENT@2026-09-27");
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
        (await AgentTokenSelection.FindAgentIdAsync(db, "registered-agent-token")).Should().Be("NEW-AGENT@2026-09-27");
    }

    [Fact]
    public async Task StartAsync_KeepsTheIdATokenWasStoredUnder()
    {
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        apiClient.GetMyAgentAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new Agent { Symbol = "NEW-AGENT", StartingFaction = "COSMIC" }));
        StubServerResetDate(apiClient);

        using var provider = await BuildProvider(
            databaseName: Guid.NewGuid().ToString("N"),
            initialAgentId: null,
            configureDb: db => SeedActiveTokenCredential(db, "NEW-AGENT@2026-09-13", "known-token"),
            configureServices: services => AddRegistrationServices(services, apiClient));

        await provider.GetRequiredService<AgentBootstrapService>().StartAsync(CancellationToken.None);

        provider.GetRequiredService<IAgentDataScope>().AgentId.Should().Be("NEW-AGENT@2026-09-13");
        await apiClient.DidNotReceive().RegisterAsync(Arg.Any<RegisterRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAsync_DoesNotRegisterANewAgentUnderTheIdOfAStoredAgent()
    {
        // The stored agent's token was refused after a reset, but the server's reset date was read
        // before that reset: the new agent would get the stored agent's id.
        var apiClient = Substitute.For<ISpaceTradersApiClient>();
        apiClient.GetMyAgentAsync(Arg.Any<CancellationToken>())
            .Returns<Task<Agent>>(_ => throw new SpaceTradersApiException(
                "Token reset_date does not match the server",
                HttpStatusCode.Unauthorized,
                "my/agent",
                null));
        StubServerResetDate(apiClient);

        using var provider = await BuildProvider(
            databaseName: Guid.NewGuid().ToString("N"),
            initialAgentId: null,
            configureDb: db => SeedActiveTokenCredential(db, "NEW-AGENT@2026-09-27", "stale-token"),
            configureServices: services => AddRegistrationServices(services, apiClient));

        var start = async () => await provider.GetRequiredService<AgentBootstrapService>().StartAsync(CancellationToken.None);

        await start.Should().ThrowAsync<InvalidOperationException>().WithMessage("*NEW-AGENT@2026-09-27*");
        await apiClient.DidNotReceive().RegisterAsync(Arg.Any<RegisterRequest>(), Arg.Any<CancellationToken>());
    }

    private static void StubServerResetDate(ISpaceTradersApiClient apiClient)
        => apiClient.GetStatusAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServerStatus
            {
                Status = "SpaceTraders is currently online",
                Version = "v2.3.0",
                ResetDate = "2026-09-27",
                Description = "SpaceTraders",
            }));

    private static async Task SeedActiveTokenCredential(SpaceTradersDbContext db, string agentId, string token)
    {
        db.Credentials.Add(new StoredCredential
        {
            AgentId = agentId,
            Key = AgentTokenSelection.ActiveAgentTokenKey,
            Value = token,
            StoredAt = TimeProvider.System.GetUtcNow(),
        });

        await db.SaveChangesAsync();
    }

    private static void AddRegistrationServices(IServiceCollection services, ISpaceTradersApiClient apiClient)
    {
        services.AddSingleton<IAgentTokenProvider>(new AgentTokenProvider());
        services.AddSingleton(apiClient);
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddSingleton(Substitute.For<IMessageBus>());
        services.AddSingleton<IOptions<SpaceTradersBootstrapOptions>>(
            Options.Create(new SpaceTradersBootstrapOptions
            {
                AgentName = "NEW-AGENT",
                AgentFaction = "COSMIC",
                AccountToken = "account-token",
            }));
    }

    /// <summary>The tables with a row that holds <paramref name="value"/> in any text column.</summary>
    private static async Task<IReadOnlyList<string>> TablesHoldingAsync(SpaceTradersDbContext db, string value)
    {
        var tables = new List<string>();
        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var load = typeof(AgentBootstrapServiceTests)
                .GetMethod(nameof(LoadAllAsync), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(entityType.ClrType);
            var rows = await (Task<IReadOnlyList<object>>)load.Invoke(null, [db])!;
            var textProperties = entityType.GetProperties()
                .Where(property => property.ClrType == typeof(string) && property.PropertyInfo is not null)
                .Select(property => property.PropertyInfo!)
                .ToList();

            if (rows.Any(row => textProperties.Any(property => Equals(property.GetValue(row), value))))
            {
                tables.Add(entityType.GetTableName()!);
            }
        }

        return tables;
    }

    private static async Task<IReadOnlyList<object>> LoadAllAsync<TEntity>(SpaceTradersDbContext db)
        where TEntity : class
        => await db.Set<TEntity>().IgnoreQueryFilters().AsNoTracking().ToListAsync<object>();

    private static RegisterResponseData CreateRegistrationResponse(string agentSymbol, string token)
        => new()
        {
            Token = token,
            Agent = new Agent
            {
                Symbol = agentSymbol,
                StartingFaction = "COSMIC",
                Headquarters = "X1-HQ-A1",
                Credits = 1000,
                ShipCount = 1,
                AccountId = "account-1",
            },
            Faction = new Faction
            {
                Symbol = "COSMIC",
                Name = "Cosmic",
                Description = "Faction",
                Headquarters = "X1-HQ-A1",
                IsRecruiting = true,
            },
            Contract = new Contract
            {
                Id = Guid.NewGuid().ToString("N"),
                FactionSymbol = "COSMIC",
                Type = "PROCUREMENT",
                Accepted = false,
                Fulfilled = false,
            },
            Ships = new[]
            {
                new Ship
                {
                    Symbol = $"{agentSymbol}-SHIP-1",
                    Nav = new ShipNav
                    {
                        SystemSymbol = "X1-HQ",
                        WaypointSymbol = "X1-HQ-A1",
                        Status = "DOCKED",
                        FlightMode = "CRUISE",
                    },
                    Fuel = new ShipFuel
                    {
                        Current = 100,
                        Capacity = 100,
                    },
                },
            },
        };

    private static async Task<ServiceProvider> BuildProvider(
        string databaseName,
        string? initialAgentId,
        Action<IServiceCollection> configureServices,
        Func<SpaceTradersDbContext, Task>? configureDb = null)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IAgentDataScope>(_ =>
        {
            var scope = new AgentDataScope();
            if (initialAgentId is not null)
            {
                scope.Set(initialAgentId);
            }

            return scope;
        });
        services.AddDbContext<SpaceTradersDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddSingleton(Substitute.For<IAutomationMetrics>());
        services.AddSingleton<AgentBootstrapService>();

        configureServices(services);

        var provider = services.BuildServiceProvider();

        if (configureDb is not null)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();
            await configureDb(db);
        }

        return provider;
    }
}
