using Microsoft.Extensions.DependencyInjection;
using SpaceTraders.Application.EventHandlers;
using SpaceTraders.Application.Events.Dispatching;
using SpaceTraders.Application.Events.Handlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Sync;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;
using Wolverine.ErrorHandling;

namespace SpaceTraders.Application;

/// <summary>
/// Registers all application services and event handlers for the SpaceTraders application.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mining flow:</b><br/>
/// SetShipAssignmentTypeCommand("Mine")
/// → ShipAssignmentTypeSetEvent → <see cref="ShipDockedEventHandler"/> (emit ShipIdleDockedEvent)
/// → <see cref="ShipDockedMineEventHandler"/> (Orbit/Undock)
/// → ShipUndockedEvent → ShipInOrbitMineEventHandler (Navigate to mine site)
/// → ShipArrivedEvent → ShipInOrbitMineEventHandler (Extract, navigate to sell)
/// → ShipArrivedEvent → ShipInOrbitMineEventHandler (Dock, sell, SetAssignmentType("Mine") → loop)
/// </para>
/// <para>
/// <b>Scouting flow:</b><br/>
/// SetShipAssignmentTypeCommand("Scout")
/// → ShipAssignmentTypeSetEvent → <see cref="ShipDockedEventHandler"/> (emit ShipIdleDockedEvent)
/// → <see cref="ShipDockedScoutEventHandler"/> (Orbit/Undock)
/// → ShipUndockedEvent → ShipInOrbitScoutEventHandler (Navigate to target)
/// → ShipArrivedEvent → ShipInOrbitScoutEventHandler (cache refresh, SetAssignmentType("Scout") → loop)
/// </para>
/// <para>
/// <b>Trading flow:</b><br/>
/// SetShipAssignmentTypeCommand("Trade")
/// → ShipAssignmentTypeSetEvent → <see cref="ShipDockedEventHandler"/> (emit ShipIdleDockedEvent)
/// → <see cref="ShipDockedTraderEventHandler"/> (Orbit/Undock or buy)
/// → ShipUndockedEvent → ShipInOrbitTraderEventHandler (Navigate to buy/sell waypoint)
/// → ShipArrivedEvent → ShipInOrbitTraderEventHandler (Dock, buy/sell, SetAssignmentType("Trade") → loop)
/// </para>
/// </remarks>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        Action<WolverineOptions> configureWolverine)
    {
        ArgumentNullException.ThrowIfNull(configureWolverine);

        services.AddSingleton<ITradeAnalyser, TradeAnalyser>();
        services.AddSingleton<ICreditHistoryService, CreditHistoryService>();
        services.AddScoped<INavigationPlanningService, NavigationPlanningService>();
        services.AddScoped<IJumpGateCacheService, JumpGateCacheService>();
        services.AddScoped<IContractObjectivePlanner, ContractObjectivePlanner>();
        services.AddScoped<IFleetMaintenancePlanner, FleetMaintenancePlanner>();
        services.AddScoped<IShipAssignmentPlanner, ShipAssignmentPlanner>();
        services.AddScoped<IChainOfCommandDispatcher, ChainOfCommandDispatcher>();

        services.AddScoped<IDockedCommandAcceptor, DockedCommandAcceptor>();
        services.AddScoped<IInOrbitCommandAcceptor, InOrbitCommandAcceptor>();
        services.AddScoped<IInTransitCommandAcceptor, InTransitCommandAcceptor>();

        // Phase 3: ship planner boundary. Planners are pure decision components; the
        // ShipPlannerService loads context and dispatches a single command per decision via
        // the existing command acceptors so it can coexist with the chain-of-command handlers.
        services.AddScoped<Planning.IShipPlanner, Planning.MiningShipPlanner>();
        services.AddScoped<Planning.IShipPlanner, Planning.TradingShipPlanner>();
        services.AddScoped<Planning.IShipPlanner, Planning.ContractShipPlanner>();
        services.AddScoped<Planning.IShipPlanner, Planning.ScoutingShipPlanner>();
        services.AddScoped<Planning.IShipPlannerService, Planning.ShipPlannerService>();

        services.AddScoped<IWaypointVisitService, WaypointVisitService>();
        services.AddScoped<IMarketRefreshService, MarketRefreshService>();
        services.AddScoped<IShipyardRefreshService, ShipyardRefreshService>();

        // In-orbit event handlers (consolidated)
        services.AddScoped<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitFuelRecoveryEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitContractEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitScoutEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitTraderEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitMineEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipInOrbitEvent>, ShipInOrbitEventHandler>();

        // Undocked event handlers (ShipUndockedEvent is a state transition to in-orbit)
        services.AddScoped<IChainOfCommandEventHandler<ShipUndockedEvent>, ShipInOrbitEventHandler>();

        // Transit event handler (consolidated)
        services.AddScoped<IChainOfCommandEventHandler<ShipInTransitEvent>, ShipInTransitEventHandler>();

        // State mismatch handler
        services.AddScoped<IChainOfCommandEventHandler<ShipStateMismatchEvent>, ShipStateMismatchEventHandler>();

        // Docked event handlers (consolidated)
        services.AddScoped<IChainOfCommandEventHandler<ShipDockedEvent>, ShipDockedFuelEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipDockedEvent>, ShipDockedBuilderEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipDockedEvent>, ShipDockedMineEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipDockedEvent>, ShipDockedContractEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipDockedEvent>, ShipDockedTraderEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipDockedEvent>, ShipDockedScoutEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipDockedEvent>, ShipDockedEventHandler>();

        // Derived docked events (routed to docked handlers)
        services.AddScoped<IChainOfCommandEventHandler<ShipDockedEvent>, ShipDockedIdleEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipIdleDockedEvent>, ShipDockedFuelEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipIdleDockedEvent>, ShipDockedBuilderEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipIdleDockedEvent>, ShipDockedMineEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipIdleDockedEvent>, ShipDockedTraderEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipIdleDockedEvent>, ShipDockedScoutEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipIdleDockedEvent>, ShipDockedEventHandler>();

        services.AddScoped<IChainOfCommandEventHandler<ShipContractDockedEvent>, ShipDockedFuelEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipContractDockedEvent>, ShipDockedBuilderEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipContractDockedEvent>, ShipDockedMineEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipContractDockedEvent>, ShipDockedTraderEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipContractDockedEvent>, ShipDockedScoutEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipContractDockedEvent>, ShipDockedEventHandler>();

        // ShipAssignmentTypeSetEvent is derived from ShipDockedEvent (re-enters docked chain after assignment type change)
        services.AddScoped<IChainOfCommandEventHandler<ShipAssignmentTypeSetEvent>, ShipDockedFuelEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipAssignmentTypeSetEvent>, ShipDockedBuilderEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipAssignmentTypeSetEvent>, ShipDockedMineEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipAssignmentTypeSetEvent>, ShipDockedTraderEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipAssignmentTypeSetEvent>, ShipDockedScoutEventHandler>();
        services.AddScoped<IChainOfCommandEventHandler<ShipAssignmentTypeSetEvent>, ShipDockedEventHandler>();

        services.AddScoped<SyncAllShipsHandler>();

        services.AddWolverine(opts =>
        {
            opts.Discovery.IncludeAssembly(typeof(DependencyInjection).Assembly);
            configureWolverine(opts);

            // Add retry logging middleware to all message handlers
            opts.Policies.AddMiddleware(typeof(WolverineRetryLoggingMiddleware));

            opts.OnException<Exception>()
                .RetryWithCooldown(
                    TimeSpan.FromMilliseconds(250),
                    TimeSpan.FromMilliseconds(500),
                    TimeSpan.FromSeconds(1))
                .Then.Discard();
        });

        return services;
    }

    public static IServiceCollection AddApplication(this IServiceCollection services) =>
        services.AddApplication(_ => { });
}
