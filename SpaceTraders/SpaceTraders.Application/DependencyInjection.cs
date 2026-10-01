using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        services.AddSingleton<IGoalStepCircuitBreaker, GoalStepCircuitBreaker>();

        services.AddWolverine(ExtensionDiscovery.ManualOnly, opts =>
        {
            opts.Discovery.IncludeAssembly(typeof(DependencyInjection).Assembly);

            // Wolverine 6 compiles the handler code at startup only with WolverineFx.RuntimeCompilation,
            // and with ManualOnly discovery the package doesn't register itself.
            opts.UseRuntimeCompilation();

            // Wolverine 6 refuses service location by default. The host allows it for the DbContext,
            // whose options EF Core registers through a factory. 5.x's AllowedButWarn covers anything
            // else registered through a lambda (and every test substitute): a handler that needs it
            // logs a warning instead of failing on its first message.
            opts.RestoreV5Defaults();

            configureWolverine(opts);

            // Add retry logging middleware to all message handlers
            opts.Policies.AddMiddleware(typeof(WolverineRetryLoggingMiddleware));
            opts.Policies.AddMiddleware(typeof(MessageMetricsMiddleware));

            // Wolverine logs each handled message under the message type's name, which the
            // "Wolverine" level override doesn't reach; at its default (Information) that is one line
            // per message. The handlers log what happened themselves.
            opts.Policies.MessageSuccessLogLevel(LogLevel.Debug);

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
