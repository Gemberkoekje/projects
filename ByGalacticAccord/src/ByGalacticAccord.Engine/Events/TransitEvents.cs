using System;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Events;

/// <summary>
/// A ship reaches its job's pickup and loads (§4.7.3): the shipper's goods under a Haul, a lot it bought, or its
/// owner's goods from storage. A job with a destination leaves laden, with an incident check scheduled
/// mid-route; nothing tracks the ship in between. A collect-only job ends here. Each transit event names the
/// job it belongs to and does nothing if the ship has since moved on to another.
/// </summary>
public sealed class ShipDeparted : ISimEvent
{
    public ShipDeparted(long tick, ShipId ship, ShipJob job)
    {
        Tick = tick;
        Ship = ship;
        Job = job;
    }

    public long Tick { get; }

    public ShipId Ship { get; }

    public ShipJob Job { get; }

    public string Describe(SimulationContext context) => $"Ship {Ship} loads for contract {Job.Main}.";

    public void Process(SimulationContext context)
    {
        Ship ship = context.Ship(Ship);
        if (!ReferenceEquals(ship.Job, Job))
        {
            return;
        }

        ship.ReachPickup(Job.Pickup);
        decimal loaded = Settlement.Load(context, ship, Job);
        if (Job.Purchase is { } purchase && context.PlayerDecisionFor(purchase) is not null)
        {
            // The player is inspecting what it bought: the ship waits at the pickup until it decides.
            context.Schedule(new ShipDeparted(Tick, Ship, Job));
            return;
        }

        if (Job.Destination is not { } destination)
        {
            ship.FinishJob(Tick);
            return;
        }

        if (loaded <= 0m)
        {
            // Nothing to carry: the purchase behind it fell through (a default outside the Accord, or the goods rejected).
            if (Job.Delivery is { } delivery)
            {
                Settlement.Fail(context, context.Contract(delivery), FailureReason.NotDelivered);
            }

            ship.FinishJob(Tick);
            return;
        }

        RoutePlan laden = context.FindPath(Job.Pickup, destination)
                          ?? throw new InvalidOperationException("A laden job has no route to its destination.");
        long travel = Math.Max(1, PriceModel.TravelTicks(laden.DistanceTicks, ship.Class.SpeedMultiplier));
        long midRoute = Tick + Math.Max(1, travel / 2);
        long scheduledArrival = Tick + travel;

        ship.DepartLaden(Tick, ShipCourse.Along(laden, Tick, travel));
        context.Schedule(new TransitIncidentCheck(midRoute, Ship, Job, scheduledArrival, laden.MaxDanger));
    }
}

/// <summary>The danger-weighted probabilistic check (§6.3). Fires an incident or proceeds clean to arrival.</summary>
public sealed class TransitIncidentCheck : ISimEvent
{
    private readonly decimal _danger;
    private readonly long _scheduledArrival;

    public TransitIncidentCheck(long tick, ShipId ship, ShipJob job, long scheduledArrival, decimal danger)
    {
        Tick = tick;
        Ship = ship;
        Job = job;
        _scheduledArrival = scheduledArrival;
        _danger = danger;
    }

    public long Tick { get; }

    public ShipId Ship { get; }

    public ShipJob Job { get; }

    public string Describe(SimulationContext context) => $"Transit incident check for {Job.Main}.";

    public void Process(SimulationContext context)
    {
        Ship ship = context.Ship(Ship);
        if (!ReferenceEquals(ship.Job, Job))
        {
            return;
        }

        decimal probability = context.Config.IncidentProbabilityScale * _danger * ship.Class.IncidentChanceModifier;
        if (context.Rng.Chance(probability))
        {
            TransitIncident incident = IncidentResolver.Resolve(context.Rng, _danger, ship.Class);
            context.Schedule(new TransitIncidentOccurred(Tick, Ship, Job, _scheduledArrival, incident));
        }
        else
        {
            context.Schedule(new ShipArrived(_scheduledArrival, Ship, Job));
        }
    }
}

/// <summary>Applies a resolved incident's effect to the goods aboard, then schedules arrival (possibly delayed).</summary>
public sealed class TransitIncidentOccurred : ISimEvent
{
    private readonly long _scheduledArrival;
    private readonly TransitIncident _incident;

    public TransitIncidentOccurred(long tick, ShipId ship, ShipJob job, long scheduledArrival, TransitIncident incident)
    {
        Tick = tick;
        Ship = ship;
        Job = job;
        _scheduledArrival = scheduledArrival;
        _incident = incident;
    }

    public long Tick { get; }

    public ShipId Ship { get; }

    public ShipJob Job { get; }

    public string Describe(SimulationContext context) => $"Transit incident on {Job.Main}: {_incident.Description}.";

    public void Process(SimulationContext context)
    {
        Ship ship = context.Ship(Ship);
        if (!ReferenceEquals(ship.Job, Job))
        {
            return;
        }

        ActorId owner = Job.GoodsOwner;
        switch (_incident.Kind)
        {
            case TransitIncidentKind.CargoDamage:
                ship.Alter(Tick, owner, Job.Good, cargo => cargo.Damaged(_incident.QualityLoss));
                break;
            case TransitIncidentKind.PartialLoss:
                ship.Alter(Tick, owner, Job.Good, cargo =>
                {
                    decimal lost = cargo.Quantity * _incident.QuantityLossFraction;
                    context.Lose(Job.Good, lost);
                    return cargo.LosingUnits(lost);
                });
                break;
            case TransitIncidentKind.TotalLoss:
                ship.Alter(Tick, owner, Job.Good, cargo =>
                {
                    context.Lose(Job.Good, cargo.Quantity);
                    return cargo.LosingUnits(cargo.Quantity);
                });
                break;
            case TransitIncidentKind.CreditCost:
                context.ApplySink(context.Actor(ship.Owner), _incident.CreditCost);
                break;
            case TransitIncidentKind.MinorDelay:
            default:
                break;
        }

        context.NoteIncident();
        ship.Log(Tick, $"Transit incident: {_incident.Description}.");
        ship.DelayArrival(_incident.DelayTicks);
        JournalEntry entry = context.Record(new IncidentEntry(Tick, Job.Main, ship.Owner, _incident));

        if (context.PlayerActorId == ship.Owner)
        {
            context.RaisePlayerAlert(entry);
        }

        context.Schedule(new ShipArrived(_scheduledArrival + _incident.DelayTicks, Ship, Job));
    }
}

/// <summary>
/// Arrival settles the job (§4.7.3): a carrier delivers the shipper's goods under its Haul, a seller hands its own
/// over under its Sale, or, if the goods were lost on the way, the contract fails.
/// </summary>
public sealed class ShipArrived : ISimEvent
{
    public ShipArrived(long tick, ShipId ship, ShipJob job)
    {
        Tick = tick;
        Ship = ship;
        Job = job;
    }

    public long Tick { get; }

    public ShipId Ship { get; }

    public ShipJob Job { get; }

    public string Describe(SimulationContext context) => $"Ship {Ship} arrives for contract {Job.Main}.";

    public void Process(SimulationContext context)
    {
        Ship ship = context.Ship(Ship);
        if (!ReferenceEquals(ship.Job, Job) || Job.Destination is not { } destination)
        {
            return;
        }

        ship.Arrive(Tick, destination);
        context.ApplySink(context.Actor(ship.Owner), context.Config.DockingFee);
        context.Scout(ship.Owner, destination);
        Settlement.Unload(context, ship, Job);
        ship.FinishJob(Tick);
    }
}
