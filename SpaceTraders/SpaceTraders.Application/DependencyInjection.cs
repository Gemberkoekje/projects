using Microsoft.Extensions.DependencyInjection;
using SpaceTraders.Application.EventHandlers;
using SpaceTraders.Application.Events.Dispatching;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Sync;
using Wolverine;
using Wolverine.ErrorHandling;

namespace SpaceTraders.Application;

/// <summary>
/// Registers all application services and event handlers for the SpaceTraders application.
/// </summary>
/// <remarks>
/// Phase 6.5d: All ship automation is now driven by <see cref="EventHandlers.ShipAutomationTickEventHandler"/>
/// → <see cref="Planning.IShipPlannerService"/>. Chain-of-command business-effect handler registrations
/// have been removed.
/// Phase 7d: <see cref="Events.Handlers.Ships.ShipInTransitEventHandler"/> converted to a plain Wolverine
/// handler; chain DI registration removed. The chain infrastructure types are retained for Phase 7f cleanup.
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
        // the command acceptors. Phase 6.5d: chain-of-command business handlers removed;
        // planners are now the sole source of ship automation decisions.
        // Phase 4b: cross-cutting planners (fuel recovery, maintenance) are registered first
        // so they can preempt the role planner when applicable; if they return None the
        // planner service falls through to the next matching planner.
        services.AddScoped<Planning.IShipPlanner, Planning.FuelRecoveryShipPlanner>();
        services.AddScoped<Planning.IShipPlanner, Planning.MaintenanceShipPlanner>();
        services.AddScoped<Planning.IShipPlanner, Planning.MiningShipPlanner>();
        services.AddScoped<Planning.IShipPlanner, Planning.TradingShipPlanner>();
        services.AddScoped<Planning.IShipPlanner, Planning.ContractShipPlanner>();
        services.AddScoped<Planning.IShipPlanner, Planning.ScoutingShipPlanner>();
        services.AddScoped<Planning.IShipPlanner, Planning.BuilderShipPlanner>();
        services.AddScoped<Planning.IShipPlannerService, Planning.ShipPlannerService>();

        // Phase 6: orchestrator goal evaluation. The orchestrator owns strategic goals,
        // capacity estimation, and budget policy. It assigns work to ships via
        // AssignShipCommand and never issues low-level ship commands directly.
        services.AddScoped<Orchestration.IFleetCapacityEstimator, Orchestration.FleetCapacityEstimator>();
        services.AddScoped<Orchestration.IBudgetPolicy, Orchestration.BudgetPolicy>();
        services.AddScoped<Orchestration.IFleetGoalEvaluator, Orchestration.ContractGoalEvaluator>();
        services.AddScoped<Orchestration.IFleetGoalEvaluator, Orchestration.ConstructionGoalEvaluator>();
        services.AddScoped<Orchestration.IFleetGoalEvaluator, Orchestration.MarketCoverageGoalEvaluator>();
        services.AddScoped<Orchestration.IFleetGoalEvaluator, Orchestration.FleetExpansionGoalEvaluator>();
        services.AddScoped<Orchestration.IFleetOrchestrator, Orchestration.FleetOrchestrator>();

        services.AddScoped<IWaypointVisitService, WaypointVisitService>();
        services.AddScoped<IMarketRefreshService, MarketRefreshService>();
        services.AddScoped<IShipyardRefreshService, ShipyardRefreshService>();

        // Phase 6.5d: Business-effect chain handler registrations have been removed.
        // ShipAutomationTickEventHandler → ShipPlannerService now covers all docked, in-orbit,
        // and in-transit decision points.
        // Phase 7d: ShipInTransitEventHandler converted to plain Wolverine handler; chain DI
        // registration removed. Chain infrastructure retained for Phase 7f cleanup.

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
