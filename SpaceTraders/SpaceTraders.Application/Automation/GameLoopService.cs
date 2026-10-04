using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Contracts;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Exploring;
using SpaceTraders.Application.Goals;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// Background service.
/// Every 5 s:
///  - Skips processing if this instance is not the leader (see <see cref="ILeaderElection"/>).
///  - With automation switched on (<see cref="AutomationSwitches"/>): bootstraps each plan that is
///    switched on, runs one goal step per ship, runs the contract plan's assignments, and last
///    refreshes one market where a ship is, when one is due (<see cref="IMarketWatchService"/>).
///  - Detects API availability transitions and publishes ApiUnavailableEvent / ApiAvailableEvent.
/// </summary>
public sealed class GameLoopService(
    IServiceScopeFactory serviceScopeFactory,
    IApiAvailabilityState apiAvailability,
    ILeaderElection leaderElection,
    ILogger<GameLoopService> logger) : BackgroundService
{
    private static readonly TimeSpan DeadReckoningInterval = TimeSpan.FromSeconds(5);

    private long _tick;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in GameLoopService tick.");
            }

            await Task.Delay(DeadReckoningInterval, stoppingToken);
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        if (!leaderElection.IsLeader)
        {
            logger.LogTrace("GameLoopService: not the leader; skipping tick.");
            return;
        }

        // Every line logged during the tick carries its number; each step adds its plan or ship.
        using var tickContext = logger.BeginScope(new Dictionary<string, object> { ["Tick"] = Interlocked.Increment(ref _tick) });
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var bus = services.GetRequiredService<Wolverine.IMessageBus>();
        var settings = services.GetRequiredService<ISettingsRepository>();

        if (TimeProvider.System.GetUtcNow() < apiAvailability.PausedUntil)
        {
            logger.LogDebug(
                "GameLoopService: API calls are paused until {PausedUntil} after a 502; no plans, goal steps or contract work this tick.",
                apiAvailability.PausedUntil);
        }
        else if (await settings.IsAutomationEnabledAsync(cancellationToken))
        {
            await RunAutomationAsync(settings, cancellationToken);
        }
        else
        {
            logger.LogDebug("GameLoopService: automation is switched off; no plans, goal steps or contract work this tick.");
        }

        await PublishApiAvailabilityEventsAsync(bus, cancellationToken);
    }

    private async Task RunAutomationAsync(ISettingsRepository settings, CancellationToken cancellationToken)
    {
        // A plan that is switched off is not bootstrapped, so it doesn't buy anything either.
        foreach (var plan in Enum.GetValues<AutomationPlan>())
        {
            if (await settings.IsPlanEnabledAsync(plan, cancellationToken))
            {
                await RunStepAsync(
                    new Dictionary<string, object> { ["Plan"] = plan },
                    services => BootstrapAsync(plan, services, cancellationToken),
                    exception => logger.LogError(exception, "GameLoopService: bootstrapping the {Plan} plan failed; the rest of the tick carries on.", plan),
                    cancellationToken);
            }
        }

        await using (var scope = serviceScopeFactory.CreateAsyncScope())
        {
            var ships = await scope.ServiceProvider.GetRequiredService<IShipRepository>().GetAllAsync(cancellationToken);
            foreach (var ship in ships)
            {
                await RunStepAsync(
                    new Dictionary<string, object> { ["ShipSymbol"] = ship.Symbol },
                    async services =>
                    {
                        await services.GetRequiredService<IShipGoalExecutorService>().ExecuteAsync(ship.Symbol, cancellationToken);
                    },
                    exception => logger.LogError(exception, "GameLoopService: the goal step for ship {ShipSymbol} failed; the rest of the tick carries on.", ship.Symbol),
                    cancellationToken);
            }
        }

        if (await settings.IsPlanEnabledAsync(AutomationPlan.Contract, cancellationToken))
        {
            await RunContractAssignmentsAsync(cancellationToken);
        }

        // Last, and one market a tick: a refresh is a read, which can go later without loss, while a
        // move or a trade can't (D19, 6.5).
        await RunStepAsync(
            [],
            services => services.GetRequiredService<IMarketWatchService>().RefreshDueMarketAsync(cancellationToken),
            exception => logger.LogError(exception, "GameLoopService: refreshing a market where a ship is failed; the next tick tries again."),
            cancellationToken);
    }

    /// <summary>
    /// Runs one step of the tick in its own scope and try/catch, so that a step that throws, or
    /// leaves its DbContext unusable, doesn't stop the steps after it. Everything logged during the
    /// step carries <paramref name="logContext"/>.
    /// </summary>
    private async Task RunStepAsync(
        Dictionary<string, object> logContext,
        Func<IServiceProvider, Task> step,
        Action<Exception> logFailure,
        CancellationToken cancellationToken)
    {
        using var stepContext = logger.BeginScope(logContext);
        try
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            await step(scope.ServiceProvider);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logFailure(exception);
        }
    }

    private static Task BootstrapAsync(AutomationPlan plan, IServiceProvider services, CancellationToken cancellationToken) => plan switch
    {
        AutomationPlan.Scout => services.GetRequiredService<IScoutAllMarketplacesPlanService>().EnsureBootstrappedAsync(cancellationToken),
        AutomationPlan.Explore => services.GetRequiredService<IExplorePlanService>().EnsureBootstrappedAsync(cancellationToken),
        AutomationPlan.Roles => services.GetRequiredService<IRolePlanService>().EnsureBootstrappedAsync(cancellationToken),
        AutomationPlan.Contract => services.GetRequiredService<IContractPlanService>().EnsureBootstrappedAsync(cancellationToken),
        AutomationPlan.ProbeDeployment => services.GetRequiredService<IProbeDeploymentPlanService>().EnsureBootstrappedAsync(cancellationToken),
        AutomationPlan.Survey => services.GetRequiredService<ISurveyPlanService>().EnsureBootstrappedAsync(cancellationToken),
        AutomationPlan.Mining => services.GetRequiredService<IMiningAutomationService>().EnsureBootstrappedAsync(cancellationToken),
        AutomationPlan.Siphon => services.GetRequiredService<ISiphonAutomationService>().EnsureBootstrappedAsync(cancellationToken),
        AutomationPlan.Construction => services.GetRequiredService<IConstructionPlanService>().EnsureBootstrappedAsync(cancellationToken),
        AutomationPlan.Trading => services.GetRequiredService<ITradingAutomationService>().EnsureBootstrappedAsync(cancellationToken),
        AutomationPlan.SpareTime => services.GetRequiredService<ISpareTimePlanService>().EnsureBootstrappedAsync(cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, "Unknown plan."),
    };

    private async Task RunContractAssignmentsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ShipAssignmentDto> activeAssignments;
        await using (var scope = serviceScopeFactory.CreateAsyncScope())
        {
            activeAssignments = await scope.ServiceProvider.GetRequiredService<IShipAssignmentRepository>().GetAllActiveAsync(cancellationToken);
        }

        foreach (var assignment in activeAssignments)
        {
            if (assignment.CompletedAt.HasValue
                || !string.Equals(assignment.AssignmentType, "Contract", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(assignment.CargoSymbol)
                || string.IsNullOrWhiteSpace(assignment.OriginWaypoint)
                || string.IsNullOrWhiteSpace(assignment.DestWaypoint)
                || string.IsNullOrWhiteSpace(assignment.ContractId))
            {
                continue;
            }

            await RunStepAsync(
                new Dictionary<string, object>
                {
                    ["Plan"] = AutomationPlan.Contract,
                    ["ShipSymbol"] = assignment.ShipSymbol,
                    ["ContractId"] = assignment.ContractId,
                },
                services => RunContractAssignmentAsync(assignment, services, cancellationToken),
                exception => logger.LogError(
                    exception,
                    "GameLoopService: contract work for ship {ShipSymbol} on contract {ContractId} failed; the rest of the tick carries on.",
                    assignment.ShipSymbol,
                    assignment.ContractId),
                cancellationToken);
        }
    }

    private static async Task RunContractAssignmentAsync(
        ShipAssignmentDto assignment,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var ship = await services.GetRequiredService<IShipRepository>().FindAsync(assignment.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            return;
        }

        var cargoUnits = ship.CargoInventory?
            .FirstOrDefault(i => i.Symbol.Equals(assignment.CargoSymbol, StringComparison.OrdinalIgnoreCase))?
            .Units ?? 0;

        // Deliver a whole trip, as MineResourceVolumeCommand mines it: what the contract still needs,
        // at most a full hold (B8). With nothing left to deliver, the delivery command fulfils the
        // contract.
        var unitsPerTrip = MineResourceVolumeHandler.UnitsPerTrip(assignment.RequiredUnits, ship.CargoCapacity);
        var holdIsFull = ship.CargoCapacity > 0 && ship.CargoCurrent >= ship.CargoCapacity;

        var bus = services.GetRequiredService<Wolverine.IMessageBus>();
        if (cargoUnits >= unitsPerTrip || (cargoUnits > 0 && holdIsFull))
        {
            await bus.InvokeAsync(new FulfillContractDeliveryCommand(
                assignment.ShipSymbol,
                assignment.ContractId!,
                assignment.CargoSymbol!,
                assignment.DestWaypoint!),
                cancellationToken);
        }
        else
        {
            await bus.InvokeAsync(new MineResourceVolumeCommand(
                assignment.ShipSymbol,
                assignment.CargoSymbol!,
                assignment.OriginWaypoint!,
                assignment.RequiredUnits),
                cancellationToken);
        }
    }

    private async Task PublishApiAvailabilityEventsAsync(
        Wolverine.IMessageBus bus,
        CancellationToken cancellationToken)
    {
        if (apiAvailability.ConsumeUnavailableTransition())
        {
            logger.LogWarning(
                "{EventKind:l}: the SpaceTraders API answered 502 (DDoS protection); no API calls until {PausedUntil}.",
                JournalEvents.ApiUnavailable,
                apiAvailability.PausedUntil);
            await bus.PublishAsync(new ApiUnavailableEvent(TimeProvider.System.GetUtcNow()));
        }

        if (apiAvailability.ConsumeAvailableTransition())
        {
            logger.LogInformation("{EventKind:l}: the SpaceTraders API answers again.", JournalEvents.ApiAvailable);
            await bus.PublishAsync(new ApiAvailableEvent(TimeProvider.System.GetUtcNow()));
        }
    }
}
