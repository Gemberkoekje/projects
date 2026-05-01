using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SpaceTraders.API.Configuration;
using SpaceTraders.API.Services;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Scoping;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Exceptions;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Accounts;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Agents;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Common;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Contracts;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Factions;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;
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

        using var provider = await BuildProvider(
            databaseName: Guid.NewGuid().ToString("N"),
            initialScopedToken: "initial-token",
            configureDb: db => SeedActiveTokenCredential(db, "old-token"),
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

        await using var verifyScope = provider.CreateAsyncScope();
        var dataScope = verifyScope.ServiceProvider.GetRequiredService<IAgentDataScope>();
        dataScope.Set("new-token");
        var db = verifyScope.ServiceProvider.GetRequiredService<SpaceTradersDbContext>();

        var activeToken = await AgentTokenSelection.GetActiveTokenAsync(db, CancellationToken.None);
        activeToken.Should().Be("new-token");
        (await db.Agents.AsNoTracking().AnyAsync(a => a.AgentToken == "new-token" && a.Symbol == "NEW-AGENT")).Should().BeTrue();
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

        using var provider = await BuildProvider(
            databaseName: Guid.NewGuid().ToString("N"),
            initialScopedToken: "initial-token",
            configureDb: db => SeedActiveTokenCredential(db, "matching-token"),
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

        using var provider = await BuildProvider(
            databaseName: Guid.NewGuid().ToString("N"),
            initialScopedToken: "initial-token",
            configureDb: db => SeedActiveTokenCredential(db, "stale-token"),
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
    public async Task StartAsync_WhenStartupSyncChecksCache_OnlyUsesActiveAgentTokenData()
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
            initialScopedToken: "active-token",
            configureDb: async db =>
            {
                db.Systems.Add(new CachedSystem
                {
                    AgentToken = "other-token",
                    Symbol = "X1-NEW",
                    SectorSymbol = "X1",
                    Type = "OLD",
                    X = 99,
                    Y = 99,
                    LastObservedAt = now,
                });
                db.Waypoints.Add(new CachedWaypoint
                {
                    AgentToken = "other-token",
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

        (await dbContext.Systems.AsNoTracking().AnyAsync(s => s.AgentToken == "active-token" && s.Symbol == "X1-NEW")).Should().BeTrue();
        (await dbContext.Waypoints.AsNoTracking().AnyAsync(w => w.AgentToken == "active-token" && w.SystemSymbol == "X1-NEW")).Should().BeTrue();
    }

    private static async Task SeedActiveTokenCredential(SpaceTradersDbContext db, string token)
    {
        db.Credentials.Add(new StoredCredential
        {
            AgentToken = token,
            Key = AgentTokenSelection.ActiveAgentTokenKey,
            Value = token,
            StoredAt = TimeProvider.System.GetUtcNow(),
        });

        await db.SaveChangesAsync();
    }

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
        string initialScopedToken,
        Action<IServiceCollection> configureServices,
        Func<SpaceTradersDbContext, Task>? configureDb = null)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton<IAgentDataScope>(_ =>
        {
            var scope = new AgentDataScope();
            scope.Set(initialScopedToken);
            return scope;
        });
        services.AddDbContext<SpaceTradersDbContext>(options => options.UseInMemoryDatabase(databaseName));
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
