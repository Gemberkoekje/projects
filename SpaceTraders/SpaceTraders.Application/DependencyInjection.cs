using Microsoft.Extensions.DependencyInjection;
using SpaceTraders.Application.EventHandlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
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
/// → <see cref="Goals.IShipGoalExecutorService"/>.
/// Phase 7d: <see cref="Events.Handlers.Ships.ShipInTransitEventHandler"/> converted to a plain Wolverine
/// handler; chain DI registration removed.
/// Phase 7f: Chain-of-command infrastructure deleted entirely.
/// Phase 12a: Deprecated planner infrastructure removed.
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
        services.AddSingleton<IShipCapabilityRegistry, ShipCapabilityRegistry>();
        services.AddScoped<IAssignmentResolver, AssignmentResolver>();
        services.AddScoped<INavigationPlanningService, NavigationPlanningService>();
        services.AddScoped<IJumpGateCacheService, JumpGateCacheService>();
        services.AddScoped<IContractObjectivePlanner, ContractObjectivePlanner>();
        services.AddScoped<IFleetMaintenancePlanner, FleetMaintenancePlanner>();
        services.AddScoped<IShipAssignmentPlanner, ShipAssignmentPlanner>();

        services.AddScoped<IDockedCommandAcceptor, DockedCommandAcceptor>();
        services.AddScoped<IInOrbitCommandAcceptor, InOrbitCommandAcceptor>();

        // Phase 6: orchestrator goal evaluation. The orchestrator owns strategic goals,
        // capacity estimation, and budget policy. It assigns work to ships via
        // AssignShipCommand and never issues low-level ship commands directly.
        services.AddScoped<Orchestration.IFleetCapacityEstimator, Orchestration.FleetCapacityEstimator>();
        services.AddScoped<Orchestration.IBudgetPolicy, Orchestration.BudgetPolicy>();
        services.AddScoped<Orchestration.IFleetGoalEvaluator, Orchestration.ContractGoalEvaluator>();
        services.AddScoped<Orchestration.IFleetGoalEvaluator, Orchestration.ConstructionGoalEvaluator>();
        services.AddScoped<Orchestration.IFleetGoalEvaluator, Orchestration.MarketCoverageGoalEvaluator>();
        services.AddScoped<Orchestration.IFleetGoalEvaluator, Orchestration.FleetExpansionGoalEvaluator>();
        services.AddScoped<Orchestration.IFleetGoalEvaluator, Orchestration.MarketScoutingGoalEvaluator>();
        services.AddScoped<Orchestration.IFleetOrchestrator, Orchestration.FleetOrchestrator>();

        // Phase 15a/15b: observability read-model (FleetStatusQueryService is the concrete implementation from Phase 15b).
        services.AddScoped<IFleetStatusQueryService, FleetStatusQueryService>();

        services.AddScoped<IWaypointVisitService, WaypointVisitService>();
        services.AddScoped<IMarketRefreshService, MarketRefreshService>();
        services.AddScoped<IShipyardRefreshService, ShipyardRefreshService>();

        // Phase 10: goal-driven executors. Registered before ShipGoalExecutorService so DI
        // injects them as IEnumerable<IShipGoalExecutor> in registration order.
        services.AddScoped<IShipGoalExecutor, IdleGoalExecutor>();
        services.AddScoped<IShipGoalExecutor, MoveToWaypointGoalExecutor>();
        services.AddScoped<IShipGoalExecutor, MineResourceGoalExecutor>();
        services.AddScoped<IShipGoalExecutor, SiphonResourceGoalExecutor>();
        services.AddScoped<IShipGoalExecutor, SellCargoGoalExecutor>();
        services.AddScoped<IShipGoalExecutor, DeliverCargoGoalExecutor>();
        services.AddScoped<IShipGoalExecutor, SupplyConstructionGoalExecutor>();
        services.AddScoped<IShipGoalExecutor, ScoutWaypointGoalExecutor>();
        services.AddScoped<IShipGoalExecutor, PatrolMarketGoalExecutor>();
        services.AddScoped<IShipGoalExecutorService, ShipGoalExecutorService>();

        // Phase 7f: Chain-of-command infrastructure deleted entirely.
        // Phase 12a: Deprecated planner infrastructure deleted.

        services.AddScoped<SyncAllShipsHandler>();

        services.AddWolverine(ExtensionDiscovery.ManualOnly, opts =>
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
