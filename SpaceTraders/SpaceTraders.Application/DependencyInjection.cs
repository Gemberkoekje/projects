using JasperFx.CodeGeneration.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.EventHandlers;
using SpaceTraders.Application.Events.Handlers.Ships;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Goals.Executors;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Roles;
using SpaceTraders.Application.Services;
using SpaceTraders.Application.Trading;
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

        // A purchase that waits for one of our ships at a shipyard, from tick to tick, for the probe plan
        // to answer (slice 6.3, D30).
        services.AddSingleton<ShipyardCalls>();
        services.AddScoped<IScoutShipSelectionService, ScoutShipSelectionService>();
        services.AddScoped<IScoutMarketplaceDiscoveryService, ScoutMarketplaceDiscoveryService>();
        services.AddScoped<IMarketplaceRoutePlanner, MarketplaceRoutePlanner>();
        services.AddScoped<IBudgetPolicy, BudgetPolicy>();
        services.AddScoped<IScoutAllMarketplacesPlanService, ScoutAllMarketplacesPlanService>();
        services.AddScoped<IContractPlanService, ContractPlanService>();
        services.AddScoped<IProbeDeploymentPlanService, ProbeDeploymentPlanService>();
        services.AddScoped<IMiningAutomationService, MiningAutomationService>();
        services.AddScoped<ISiphonAutomationService, SiphonAutomationService>();
        services.AddScoped<ITradingAutomationService, TradingAutomationService>();

        // The role board (slice 6.9): every ship's role by what it and the others can do, and what each role pays
        // per hour; how fast each ship fills its hold, as its extractions showed; and what the board remembers
        // between passes.
        services.AddScoped<IRolePlanService, RolePlanService>();
        services.AddScoped<IRoleAdvisor, RoleAdvisor>();
        services.AddSingleton<IGatheringRates, GatheringRates>();
        services.AddSingleton<RoleBoardMemory>();

        // Cargo nothing will sell or use is sold where that pays, else jettisoned (D42).
        services.AddScoped<ICargoJettison, CargoJettison>();
        services.AddSingleton<JettisonRetries>();

        // Spare time (slice 6.8): the command ship mines or siphons when it has nothing to survey or trade.
        services.AddScoped<ISpareTimePlanService, SpareTimePlanService>();
        services.AddScoped<SpareTimeInterruption>();

        // Surveying and mining (slice 6.4): the survey plan, the surveys' bookkeeping, and what a survey or
        // mining decision reads.
        services.AddScoped<ISurveyPlanService, SurveyPlanService>();
        services.AddScoped<ISurveyKeeper, SurveyKeeper>();
        services.AddScoped<IMiningContextReader, MiningContextReader>();

        // Trading (slice 6.5): the trade arithmetic's inputs, the production chains (fetched once per
        // process, shared with the markets dashboard), and the watch that keeps prices fresh where ships are.
        services.AddSingleton<ISupplyChainCache, SupplyChainCache>();
        services.AddScoped<ITradeContextReader, TradeContextReader>();
        services.AddScoped<IMarketRefresher, MarketRefresher>();
        services.AddScoped<IMarketWatchService, MarketWatchService>();
        services.AddSingleton<MarketWatchAttempts>();

        // Generic command handlers are discovered from this assembly by Wolverine.

        // Baseline goal executors.
        services.AddScoped<IShipGoalExecutor, IdleGoalExecutor>();
        services.AddScoped<IShipGoalExecutor, ScoutWaypointGoalExecutor>();
        services.AddScoped<IShipGoalExecutor, DeployProbeGoalExecutor>();
        services.AddScoped<IShipGoalExecutor, MineAndSellGoalExecutor>();
        services.AddScoped<IShipGoalExecutor, SiphonAndSellGoalExecutor>();
        services.AddScoped<IShipGoalExecutor, GatherAndSellGoalExecutor>();
        services.AddScoped<IShipGoalExecutor, TradeBetweenMarketsGoalExecutor>();
        services.AddScoped<IShipGoalExecutor, SurveyWaypointGoalExecutor>();
        services.AddScoped<IShipGoalExecutorService, ShipGoalExecutorService>();
        services.AddSingleton<IGoalStepCircuitBreaker, GoalStepCircuitBreaker>();
        services.AddSingleton<IShipGoalStepGuard, ShipGoalStepGuard>();

        services.AddWolverine(ExtensionDiscovery.ManualOnly, opts =>
        {
            opts.Discovery.IncludeAssembly(typeof(DependencyInjection).Assembly);

            // Wolverine 6 compiles the handler code at startup only with WolverineFx.RuntimeCompilation,
            // and with ManualOnly discovery the package doesn't register itself.
            opts.UseRuntimeCompilation();

            // Wolverine 6 refuses service location by default. The host allows it for the DbContext,
            // whose options EF Core registers through a factory. Everything else registered through a
            // lambda needs it too: an interface that resolves its concrete type, a typed HttpClient,
            // every test substitute. Allowed without a warning: 5.x's AllowedButWarn logged one per
            // handler and dependency on each handler's first message, which the RepeatingError rule
            // counted as one error repeating (B42), and which this composition can't act on.
            opts.RestoreV5Defaults();
            opts.ServiceLocationPolicy = ServiceLocationPolicy.AlwaysAllowed;

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
