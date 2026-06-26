using System;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Events;

/// <summary>
/// A ship leaves origin for destination (§4.7.3). Goods are picked up here; an incident check is
/// scheduled mid-route. Nothing tracks the ship in between — it is purely a scheduled arrival.
/// </summary>
public sealed class ShipDeparted : ISimEvent
{
    public ShipDeparted(long tick, ContractId contract)
    {
        Tick = tick;
        Contract = contract;
    }

    public long Tick { get; }

    public ContractId Contract { get; }

    public string Describe(SimulationContext context) => $"Ship departs for contract {Contract}.";

    public void Process(SimulationContext context)
    {
        Contract contract = context.Contract(Contract);
        if (contract.Status != ContractStatus.Active || contract.AssignedShip is null)
        {
            return;
        }

        Ship ship = context.Ship(contract.AssignedShip.Value);
        ActorState pilot = context.Actor(contract.CounterpartyId!.Value);

        Settlement.Pickup(context, contract, ship, pilot);

        RoutePlan laden = context.FindPath(contract.OriginLocationId, contract.DestinationLocationId)
                          ?? throw new InvalidOperationException("Active haul has no laden route.");
        long travel = Math.Max(1, PriceModel.TravelTicks(laden.DistanceTicks, ship.Class.SpeedMultiplier));
        long midRoute = Tick + Math.Max(1, travel / 2);
        long scheduledArrival = Tick + travel;

        context.Schedule(new TransitIncidentCheck(midRoute, Contract, scheduledArrival, laden.MaxDanger));
    }
}

/// <summary>The danger-weighted probabilistic check (§6.3). Fires an incident or proceeds clean to arrival.</summary>
public sealed class TransitIncidentCheck : ISimEvent
{
    private readonly decimal _danger;
    private readonly long _scheduledArrival;

    public TransitIncidentCheck(long tick, ContractId contract, long scheduledArrival, decimal danger)
    {
        Tick = tick;
        Contract = contract;
        _scheduledArrival = scheduledArrival;
        _danger = danger;
    }

    public long Tick { get; }

    public ContractId Contract { get; }

    public string Describe(SimulationContext context) => $"Transit incident check for {Contract}.";

    public void Process(SimulationContext context)
    {
        Contract contract = context.Contract(Contract);
        if (contract.Status != ContractStatus.Active || contract.AssignedShip is null)
        {
            return;
        }

        Ship ship = context.Ship(contract.AssignedShip.Value);
        decimal probability = context.Config.IncidentProbabilityScale * _danger * ship.Class.IncidentChanceModifier;

        if (context.Rng.Chance(probability))
        {
            TransitIncident incident = IncidentResolver.Resolve(context.Rng, _danger, ship.Class);
            context.Schedule(new TransitIncidentOccurred(Tick, Contract, _scheduledArrival, incident));
        }
        else
        {
            context.Schedule(new ShipArrived(_scheduledArrival, Contract));
        }
    }
}

/// <summary>Applies a resolved incident's effect to the haul, then schedules arrival (possibly delayed).</summary>
public sealed class TransitIncidentOccurred : ISimEvent
{
    private readonly long _scheduledArrival;
    private readonly TransitIncident _incident;

    public TransitIncidentOccurred(long tick, ContractId contract, long scheduledArrival, TransitIncident incident)
    {
        Tick = tick;
        Contract = contract;
        _scheduledArrival = scheduledArrival;
        _incident = incident;
    }

    public long Tick { get; }

    public ContractId Contract { get; }

    public string Describe(SimulationContext context) => $"Transit incident on {Contract}: {_incident.Description}.";

    public void Process(SimulationContext context)
    {
        Contract contract = context.Contract(Contract);
        if (contract.Status != ContractStatus.Active || contract.AssignedShip is null)
        {
            return;
        }

        Ship ship = context.Ship(contract.AssignedShip.Value);
        ActorState pilot = context.Actor(contract.CounterpartyId!.Value);
        Cargo cargo = ship.Manifest ?? Cargo.Fresh(contract.Good, contract.Quantity);

        switch (_incident.Kind)
        {
            case TransitIncidentKind.CargoDamage:
                ship.DamageManifest(cargo.Damaged(_incident.QualityLoss));
                break;
            case TransitIncidentKind.PartialLoss:
            {
                decimal lost = cargo.Quantity * _incident.QuantityLossFraction;
                context.NoteLost(lost);
                ship.DamageManifest(cargo.LosingUnits(lost));
                break;
            }

            case TransitIncidentKind.TotalLoss:
                context.NoteLost(cargo.Quantity);
                ship.DamageManifest(cargo.LosingUnits(cargo.Quantity));
                break;
            case TransitIncidentKind.CreditCost:
                context.ApplySink(pilot, _incident.CreditCost);
                break;
            case TransitIncidentKind.MinorDelay:
            default:
                break;
        }

        context.NoteIncident();
        ship.Log(Tick, $"Transit incident: {_incident.Description}.");
        context.Record("Incident", $"{Contract}: {pilot.Name} hit by {_incident.Description}.");

        if (context.PlayerActorId == pilot.Id)
        {
            context.RaisePlayerAlert($"Transit incident on {Contract}: {_incident.Description}.");
        }

        context.Schedule(new ShipArrived(_scheduledArrival + _incident.DelayTicks, Contract));
    }
}

/// <summary>Arrival resolves the haul: decay is applied, then the contract is fulfilled or breached (§4.7.3).</summary>
public sealed class ShipArrived : ISimEvent
{
    public ShipArrived(long tick, ContractId contract)
    {
        Tick = tick;
        Contract = contract;
    }

    public long Tick { get; }

    public ContractId Contract { get; }

    public string Describe(SimulationContext context) => $"Ship arrives for contract {Contract}.";

    public void Process(SimulationContext context)
    {
        Contract contract = context.Contract(Contract);
        if (contract.Status != ContractStatus.Active || contract.AssignedShip is null)
        {
            return;
        }

        Ship ship = context.Ship(contract.AssignedShip.Value);
        ActorState pilot = context.Actor(contract.CounterpartyId!.Value);

        // Perishable quality decays over the actual time in transit, slowed by refrigeration.
        long transitTicks = Tick - ship.DepartedTick;
        if (ship.Manifest is { } manifest)
        {
            ship.DamageManifest(manifest.Decayed(transitTicks, ship.Class.Preservation));
        }

        ship.Arrive(Tick, contract.DestinationLocationId);
        context.ApplySink(pilot, context.Config.DockingFee);
        context.Scout(pilot.Id, contract.DestinationLocationId);

        decimal deliveredQuantity = ship.Manifest?.Quantity ?? 0m;
        if (deliveredQuantity <= 0m)
        {
            Settlement.Breach(context, contract, ship, Tick, "cargo lost in transit");
        }
        else
        {
            Settlement.Fulfill(context, contract, ship, Tick);
        }
    }
}
