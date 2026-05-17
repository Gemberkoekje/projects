using Microsoft.Extensions.DependencyInjection;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.EventHandlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Services;
using Wolverine;
using Wolverine.ErrorHandling;

namespace SpaceTraders.Application;

/// <summary>
/// Registers all application services and event handlers for the SpaceTraders application.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        Action<WolverineOptions> configureWolverine)
    {
        ArgumentNullException.ThrowIfNull(configureWolverine);

        services.AddSingleton<ICreditHistoryService, CreditHistoryService>();
        services.AddSingleton<IShipCapabilityRegistry, ShipCapabilityRegistry>();
        services.AddScoped<INavigationPlanningService, NavigationPlanningService>();
        services.AddScoped<IJumpGateCacheService, JumpGateCacheService>();

        // Navigate subcommands — DI-injected, not bus-routed.
        services.AddScoped<IOrbitSubCommand, OrbitSubCommand>();
        services.AddScoped<INavigateSubCommand, NavigateSubCommand>();
        services.AddScoped<IDockSubCommand, DockSubCommand>();
        services.AddScoped<IRefuelSubCommand, RefuelSubCommand>();

        // Phase 15a/15b: observability read-model (FleetStatusQueryService is the concrete implementation from Phase 15b).
        // Phase 16c: wrap with a short-lived in-memory cache so dashboard polling does not overload the database.
        services.AddMemoryCache();
        services.AddScoped<FleetStatusQueryService>();
        services.AddScoped<IFleetStatusQueryService>(sp =>
            new CachingFleetStatusQueryService(
                sp.GetRequiredService<FleetStatusQueryService>(),
                sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()));

        services.AddScoped<IWaypointVisitService, WaypointVisitService>();
        services.AddScoped<IShipPurchaseService, ShipPurchaseService>();
        services.AddScoped<IScoutShipSelectionService, ScoutShipSelectionService>();
        services.AddScoped<IScoutMarketplaceDiscoveryService, ScoutMarketplaceDiscoveryService>();
        services.AddScoped<IMarketplaceRoutePlanner, MarketplaceRoutePlanner>();
        services.AddScoped<IBudgetPolicy, BudgetPolicy>();
        services.AddScoped<IScoutAllMarketplacesPlanService, ScoutAllMarketplacesPlanService>();
        services.AddScoped<IContractPlanService, ContractPlanService>();
        services.AddScoped<IProbeDeploymentPlanService, ProbeDeploymentPlanService>();
        services.AddScoped<IMiningAutomationService, MiningAutomationService>();
        services.AddScoped<ITradingAutomationService, TradingAutomationService>();

        // Generic command handlers are discovered from this assembly by Wolverine.

        // Baseline goal executors.
        services.AddScoped<IShipGoalExecutor, IdleGoalExecutor>();
        services.AddScoped<IShipGoalExecutor, ScoutWaypointGoalExecutor>();
        services.AddScoped<IShipGoalExecutor, DeployProbeGoalExecutor>();
        services.AddScoped<IShipGoalExecutor, MineAndSellGoalExecutor>();
        services.AddScoped<IShipGoalExecutor, TradeBetweenMarketsGoalExecutor>();
        services.AddScoped<IShipGoalExecutor, SurveyWaypointGoalExecutor>();
        services.AddScoped<IShipGoalExecutorService, ShipGoalExecutorService>();

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
