using System;
using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Events;
using ByGalacticAccord.Engine.Random;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>
/// The whole world (the GDD's "SimulationContext", §5.4). Holds every aggregate, the event queue,
/// the seeded RNG, the journal, and the economy bookkeeping the invariants (§5.6) read. Exposes the
/// low-level, conservation-correct primitives (faucet, sink, transfer, escrow) that events build on;
/// the higher-level behaviour lives in the decision/economy/negotiation classes.
/// </summary>
public sealed class SimulationContext
{
    private readonly Dictionary<ActorId, ActorState> _actors = new();
    private readonly Dictionary<ShipId, Ship> _ships = new();
    private readonly Dictionary<LocationId, Location> _locations = new();
    private readonly Dictionary<ContractId, Contract> _contracts = new();
    private readonly List<Route> _routes = new();
    private readonly Dictionary<(ActorId Actor, LocationId Location), long> _scoutedUntil = new();

    private readonly List<string> _playerAlerts = new();

    private long _nextActorId = 1;
    private long _nextShipId = 1;
    private long _nextLocationId = 1;
    private long _nextContractId = 1;

    public SimulationContext(long seed, SimulationConfig? config = null)
    {
        Config = config ?? new SimulationConfig();
        Rng = new DeterministicRandom(seed);
        Queue = new EventQueue();
        Journal = new EventJournal();
        Reputation = new ReputationLedger();
    }

    public SimulationConfig Config { get; }

    public DeterministicRandom Rng { get; }

    public EventQueue Queue { get; }

    public EventJournal Journal { get; }

    public ReputationLedger Reputation { get; }

    public long CurrentTick { get; private set; }

    public ActorId? PlayerActorId { get; set; }

    // ---- economy bookkeeping for the invariants (§5.6) ----
    public Credits InitialCredits { get; private set; }

    public Credits TotalFaucet { get; private set; } = Credits.Zero;

    public Credits TotalSink { get; private set; } = Credits.Zero;

    public Credits EscrowLockedTotal { get; private set; } = Credits.Zero;

    public Credits EscrowReleasedTotal { get; private set; } = Credits.Zero;

    public Credits EscrowForfeitedTotal { get; private set; } = Credits.Zero;

    public Credits GovernmentRevenue { get; private set; } = Credits.Zero;

    /// <summary>Goods removed from origin owners (cargo conservation accounting, §5.6).</summary>
    public decimal CargoPickedUp { get; private set; }

    public decimal CargoDelivered { get; private set; }

    public decimal CargoLost { get; private set; }

    // ---- inflation-monitor state (§5.8) ----
    public decimal FaucetMultiplier { get; set; } = 1.0m;

    // ---- counters surfaced as telemetry ----
    public long ContractsFulfilled { get; private set; }

    public long ContractsBreached { get; private set; }

    public long IncidentsOccurred { get; private set; }

    public IReadOnlyDictionary<ActorId, ActorState> Actors => _actors;

    public IReadOnlyDictionary<ShipId, Ship> Ships => _ships;

    public IReadOnlyDictionary<LocationId, Location> Locations => _locations;

    public IReadOnlyDictionary<ContractId, Contract> Contracts => _contracts;

    public IReadOnlyList<Route> Routes => _routes;

    // ---- id generation (deterministic, counter-based) ----
    public ActorId NextActorId() => new(_nextActorId++);

    public ShipId NextShipId() => new(_nextShipId++);

    public LocationId NextLocationId() => new(_nextLocationId++);

    public ContractId NextContractId() => new(_nextContractId++);

    // ---- registration ----
    public void Add(ActorState actor) => _actors[actor.Id] = actor;

    public void Add(Ship ship) => _ships[ship.Id] = ship;

    public void Add(Location location) => _locations[location.Id] = location;

    public void Add(Route route) => _routes.Add(route);

    public void Add(Contract contract) => _contracts[contract.Id] = contract;

    public ActorState Actor(ActorId id) => _actors[id];

    public Ship Ship(ShipId id) => _ships[id];

    public Location Location(LocationId id) => _locations[id];

    public Contract Contract(ContractId id) => _contracts[id];

    /// <summary>Captures the starting money supply as the conservation baseline. Call once after world gen.</summary>
    public void CaptureInitialCredits() => InitialCredits = TotalActorCredits();

    public void AdvanceTo(long tick) => CurrentTick = tick;

    public void Schedule(ISimEvent simEvent) => Queue.Enqueue(simEvent);

    /// <summary>
    /// Advance the world by processing events up to and including <paramref name="targetTick"/> (§5.4).
    /// "Running" is just dequeuing as the clock moves. Stops early and returns <see cref="RunStop.PlayerAlert"/>
    /// if a player hard-pause was raised (§3.4); in headless runs that never happens.
    /// </summary>
    public RunStop RunTo(long targetTick)
    {
        while (Queue.HasEvents)
        {
            long next = Queue.NextTick!.Value;
            if (next > targetTick)
            {
                break;
            }

            ISimEvent simEvent = Queue.Dequeue();
            CurrentTick = simEvent.Tick;
            simEvent.Process(this);

            if (PlayerActorId is not null && HasPlayerAlerts)
            {
                return RunStop.PlayerAlert;
            }
        }

        if (CurrentTick < targetTick)
        {
            CurrentTick = targetTick;
        }

        return Queue.HasEvents ? RunStop.ReachedTarget : RunStop.Drained;
    }

    public void Record(string category, string text) => Journal.Record(CurrentTick, category, text);

    // ---- world graph helpers ----
    public Route? RouteBetween(LocationId a, LocationId b) =>
        _routes.FirstOrDefault(r => r.Connects(a, b));

    public IEnumerable<Location> Neighbours(LocationId from) =>
        _routes.Where(r => r.A == from || r.B == from)
               .Select(r => _locations[r.Other(from)]);

    /// <summary>
    /// Shortest path (by total travel ticks) between two locations, or null if unreachable. Returns the
    /// summed distance and the single most dangerous leg, so a Helios→frontier haul correctly inherits
    /// the red leg's risk. Deterministic: ties break by location id.
    /// </summary>
    public RoutePlan? FindPath(LocationId from, LocationId to)
    {
        if (from == to)
        {
            return new RoutePlan(0, 0m, 0, new[] { from });
        }

        var dist = new Dictionary<LocationId, long> { [from] = 0 };
        var maxDanger = new Dictionary<LocationId, decimal> { [from] = 0m };
        var hops = new Dictionary<LocationId, int> { [from] = 0 };
        var prev = new Dictionary<LocationId, LocationId>();
        var visited = new HashSet<LocationId>();
        var frontier = new List<LocationId> { from };

        while (frontier.Count > 0)
        {
            frontier.Sort((a, b) => dist[a] != dist[b] ? dist[a].CompareTo(dist[b]) : a.Value.CompareTo(b.Value));
            LocationId current = frontier[0];
            frontier.RemoveAt(0);
            if (!visited.Add(current))
            {
                continue;
            }

            if (current == to)
            {
                return new RoutePlan(dist[current], maxDanger[current], hops[current], Reconstruct(prev, from, to));
            }

            foreach (Route route in _routes.Where(r => r.A == current || r.B == current))
            {
                LocationId next = route.Other(current);
                long candidate = dist[current] + route.DistanceTicks;
                if (!dist.TryGetValue(next, out long existing) || candidate < existing)
                {
                    dist[next] = candidate;
                    maxDanger[next] = Math.Max(maxDanger[current], route.DangerRating);
                    hops[next] = hops[current] + 1;
                    prev[next] = current;
                    frontier.Add(next);
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<LocationId> Reconstruct(Dictionary<LocationId, LocationId> prev, LocationId from, LocationId to)
    {
        var path = new List<LocationId> { to };
        LocationId current = to;
        while (current != from && prev.TryGetValue(current, out LocationId step))
        {
            path.Add(step);
            current = step;
        }

        path.Reverse();
        return path;
    }

    public IEnumerable<Contract> OpenContracts =>
        _contracts.Values.Where(c => c.Status is ContractStatus.Open or ContractStatus.Negotiating)
                         .OrderBy(c => c.Id.Value);

    public IEnumerable<ActorState> ActorsOrdered =>
        _actors.Values.OrderBy(a => a.Id.Value);

    /// <summary>Finds the resident producer of a good at a location, if any.</summary>
    public ActorState? ProducerAt(LocationId location, GoodType good)
    {
        foreach (ActorId id in _locations[location].Residents)
        {
            ActorState a = _actors[id];
            if (a.Producer is { } p && p.Good == good)
            {
                return a;
            }
        }

        return null;
    }

    // ---- credit totals (actor credits + escrow held = the conserved supply) ----
    public Credits TotalActorCredits()
    {
        decimal sum = 0m;
        foreach (ActorState a in _actors.Values)
        {
            sum += a.Credits.Amount;
        }

        return Credits.Of(sum);
    }

    public Credits TotalEscrowHeld()
    {
        decimal sum = 0m;
        foreach (Contract c in _contracts.Values)
        {
            sum += c.EscrowHeld.Amount;
        }

        return Credits.Of(sum);
    }

    public Credits TotalCredits() => TotalActorCredits() + TotalEscrowHeld();

    // ---- faucet / sink (the only operations allowed to create or destroy credits, §4.2.1) ----
    public void ApplyFaucet(ActorState actor, Credits amount)
    {
        if (amount.Amount <= 0m)
        {
            return;
        }

        actor.Credit(amount);
        TotalFaucet += amount;
    }

    /// <summary>Destroy up to <paramref name="requested"/> credits from an actor. Returns the amount actually sunk.</summary>
    public Credits ApplySink(ActorState actor, Credits requested)
    {
        decimal actual = Math.Min(requested.Amount, Math.Max(0m, actor.Credits.Amount));
        if (actual <= 0m)
        {
            return Credits.Zero;
        }

        var amount = Credits.Of(actual);
        actor.ForceDebit(amount);
        TotalSink += amount;
        return amount;
    }

    // ---- transfers (conserve total; never touch faucet/sink) ----
    public bool TryTransfer(ActorState from, ActorState to, Credits amount)
    {
        if (!from.TryDebit(amount))
        {
            return false;
        }

        to.Credit(amount);
        return true;
    }

    // ---- escrow (held by the Accord clearing house; counted in TotalCredits) ----
    public bool LockToEscrow(Contract contract, ActorState payer, Credits amount)
    {
        if (amount.Amount <= 0m)
        {
            return true;
        }

        if (!payer.TryDebit(amount))
        {
            return false;
        }

        contract.AddEscrow(amount);
        EscrowLockedTotal += amount;
        return true;
    }

    public void ReleaseFromEscrow(Contract contract, ActorState payee, Credits amount)
    {
        Credits actual = CapToHeld(contract, amount);
        if (actual.Amount <= 0m)
        {
            return;
        }

        contract.ReleaseEscrow(actual);
        payee.Credit(actual);
        EscrowReleasedTotal += actual;
    }

    public void ForfeitFromEscrow(Contract contract, ActorState payee, Credits amount)
    {
        Credits actual = CapToHeld(contract, amount);
        if (actual.Amount <= 0m)
        {
            return;
        }

        contract.ReleaseEscrow(actual);
        payee.Credit(actual);
        EscrowForfeitedTotal += actual;
    }

    /// <summary>Tax taken out of escrow: leaves escrow (released) and is destroyed (sunk + booked as gov revenue).</summary>
    public void TaxFromEscrow(Contract contract, Credits amount)
    {
        Credits actual = CapToHeld(contract, amount);
        if (actual.Amount <= 0m)
        {
            return;
        }

        contract.ReleaseEscrow(actual);
        EscrowReleasedTotal += actual;
        TotalSink += actual;
        GovernmentRevenue += actual;
    }

    // A contract can never disburse more than it holds. Independent decimal divisions in the price and
    // quality maths can leave a settlement's recomputed shares a sub-cent over the held amount; capping
    // keeps per-contract escrow exactly >= 0 while global reconciliation stays exact.
    private static Credits CapToHeld(Contract contract, Credits amount) =>
        amount.Amount <= 0m ? Credits.Zero : Credits.Of(System.Math.Min(amount.Amount, contract.EscrowHeld.Amount));

    public void TaxDirect(ActorState payer, Credits amount)
    {
        Credits sunk = ApplySink(payer, amount);
        GovernmentRevenue += sunk;
    }

    // ---- cargo accounting (§5.6 cargo conservation) ----
    public void NotePickup(decimal quantity) => CargoPickedUp += quantity;

    public void NoteDelivered(decimal quantity) => CargoDelivered += quantity;

    public void NoteLost(decimal quantity) => CargoLost += quantity;

    // ---- counters ----
    public void NoteFulfilled() => ContractsFulfilled++;

    public void NoteBreached() => ContractsBreached++;

    public void NoteIncident() => IncidentsOccurred++;

    // ---- scouting / visibility (§4.6) ----
    public void Scout(ActorId actor, LocationId location) =>
        _scoutedUntil[(actor, location)] = CurrentTick + Config.ScoutFreshnessTicks;

    public bool HasFreshScout(ActorId actor, LocationId location) =>
        _scoutedUntil.TryGetValue((actor, location), out long until) && until >= CurrentTick;

    // ---- player hard-pause alerts (§3.4). In headless runs PlayerActorId is null and these never fire. ----
    public IReadOnlyList<string> PlayerAlerts => _playerAlerts;

    public bool HasPlayerAlerts => _playerAlerts.Count > 0;

    public void RaisePlayerAlert(string text) => _playerAlerts.Add(text);

    public void ClearPlayerAlerts() => _playerAlerts.Clear();
}
