using System;
using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Events;
using ByGalacticAccord.Engine.Random;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>
/// The whole world (the GDD's "SimulationContext", §5.4). Holds every aggregate, the event queue,
/// the seeded RNG, the journal, and the economy bookkeeping the invariants (§5.6) read. Exposes the
/// low-level, conservation-correct primitives that events build on: for money (faucet, sink, transfer,
/// escrow) and for goods (produce, consume, lose, and moves between stock, storage and holds). The
/// higher-level behaviour lives in <see cref="Market"/>, <see cref="Settlement"/> and the decision engine.
/// </summary>
public sealed class SimulationContext
{
    private readonly Dictionary<ActorId, ActorState> _actors = new();
    private readonly Dictionary<ShipId, Ship> _ships = new();
    private readonly Dictionary<LocationId, Location> _locations = new();
    private readonly Dictionary<ContractId, Contract> _contracts = new();
    private readonly List<Route> _routes = new();

    // Shortest paths are a pure function of the static route graph, so each pair is solved once.
    // Adding a location or a route clears the cache.
    private readonly Dictionary<(LocationId From, LocationId To), RoutePlan?> _pathCache = new();

    // Contracts that were open when last looked at, in id order. A contract never returns to Open once
    // it leaves it, so OpenContracts prunes this list instead of scanning every contract ever posted.
    private readonly List<Contract> _openCandidates = new();

    // Each actor's contracts that were open or signed when last looked at, in the order it joined them.
    // A settled contract never becomes live again, so LiveContractsOf prunes these lists the same way.
    private readonly Dictionary<ActorId, List<Contract>> _liveByActor = new();

    // Storage: the open stores, in the order they were opened, and each renter's open one per location. A store
    // closes when it is emptied, so a closed one holds nothing the goods ledger needs.
    private readonly List<StorageContract> _storages = new();
    private readonly Dictionary<(ActorId Renter, LocationId Location), StorageContract> _storageByRenter = new();

    // The goods ledger (design §20.2): what was produced, consumed and lost, per good.
    private readonly Dictionary<GoodType, decimal> _produced = new();
    private readonly Dictionary<GoodType, decimal> _consumed = new();
    private readonly Dictionary<GoodType, decimal> _lost = new();

    // Haggles (design §3.6), by id, and the open ones per contract in the order they began.
    private readonly Dictionary<NegotiationId, Negotiation> _negotiations = new();
    private readonly Dictionary<ContractId, List<Negotiation>> _negotiationsOn = new();
    private readonly Dictionary<NegotiationId, long> _negotiationLastEntry = new();

    private readonly List<JournalEntry> _playerAlerts = new();
    private readonly List<PlayerDecision> _playerDecisions = new();

    // How often each failure path of design §16.3 has happened, for the soak's telemetry.
    private readonly long[] _failures = new long[Enum.GetValues<FailureKind>().Length];

    // Contracts that have held escrow and may still: TotalEscrowHeld sums these rather than every contract ever.
    private readonly List<Contract> _escrowed = new();

    // Each location's neighbours by route, for the local board (visibility); rebuilt when a route is added.
    private readonly Dictionary<LocationId, HashSet<LocationId>> _adjacent = new();

    // Actors in id order, and the producers of each good, rebuilt only when an actor is added.
    private readonly Dictionary<GoodType, ActorState[]> _producersOf = new();
    private ActorState[]? _actorsOrdered;

    private long _nextActorId = 1;
    private long _nextShipId = 1;
    private long _nextLocationId = 1;
    private long _nextContractId = 1;
    private long _nextNegotiationId = 1;

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

    /// <summary>The morning price bulletin: every producer's ask, posted daily (design §7.1, "near": prices updated daily).</summary>
    public PriceBulletin Bulletin { get; } = new();

    /// <summary>The Daily Accord's issues and what they made known (design §7.4).</summary>
    public NewsDesk News { get; } = new();

    /// <summary>The Accord's public record and each actor's own dealings (design §7.3).</summary>
    public Records Records { get; } = new();

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

    // ---- inflation-monitor state (§5.8) ----
    public decimal FaucetMultiplier { get; set; } = 1.0m;

    // ---- counters surfaced as telemetry ----
    public long ContractsFulfilled { get; private set; }

    /// <summary>Contracts whose performer failed: cargo lost, or never delivered.</summary>
    public long ContractsBreached { get; private set; }

    /// <summary>Contracts whose payer didn't pay (outside the Accord).</summary>
    public long ContractsDefaulted { get; private set; }

    /// <summary>Signed contracts cancelled, at will or for default.</summary>
    public long ContractsCancelled { get; private set; }

    public long IncidentsOccurred { get; private set; }

    /// <summary>Goods handed over under Sales, all goods together.</summary>
    public decimal GoodsSold { get; private set; }

    /// <summary>Contracts AI actors signed between them in closed form, at a price other than posted (design §3.6).</summary>
    public long DealsHaggled { get; private set; }

    /// <summary>Haggles with the player that began, and that ended in a deal.</summary>
    public long NegotiationsOpened { get; private set; }

    public long NegotiationsAgreed { get; private set; }

    /// <summary>Orders at a producer's door (design §7: at the ask the buyer knew, up to a limit), and those turned down: its ask above the limit, or too little stock.</summary>
    public long DoorOrders { get; private set; }

    public long DoorRefusedForPrice { get; private set; }

    public long DoorRefusedForStock { get; private set; }

    /// <summary>Contracts signed by a taker that knew of them only from The Daily Accord (design §7.4).</summary>
    public long SignedFromNews { get; private set; }

    /// <summary>Tries at a contract known only from the news that found it already closed.</summary>
    public long FoundClosedFromNews { get; private set; }

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

    public NegotiationId NextNegotiationId() => new(_nextNegotiationId++);

    // ---- registration ----
    public void Add(ActorState actor)
    {
        _actors[actor.Id] = actor;
        _actorsOrdered = null;
        _producersOf.Clear();
    }

    public void Add(Ship ship) => _ships[ship.Id] = ship;

    public void Add(Location location)
    {
        _locations[location.Id] = location;
        _pathCache.Clear();
    }

    public void Add(Route route)
    {
        _routes.Add(route);
        _pathCache.Clear();
        _adjacent.Clear();
    }

    public void Add(Contract contract)
    {
        _contracts[contract.Id] = contract;
        Index(contract.PostedBy, contract);
        if (contract.CounterpartyId is { } counterparty)
        {
            Index(counterparty, contract);
        }

        if (contract is StorageContract storage)
        {
            _storages.Add(storage);
            _storageByRenter[(storage.Renter, storage.Location)] = storage;
        }

        // Keep the open list in id order. Contracts arrive in id order, so this is usually an append.
        int index = _openCandidates.FindLastIndex(c => c.Id.Value <= contract.Id.Value);
        if (index >= 0 && _openCandidates[index].Id == contract.Id)
        {
            _openCandidates.RemoveAt(index);
            index--;
        }

        if (IsOpen(contract))
        {
            _openCandidates.Insert(index + 1, contract);
        }
    }

    /// <summary>Register a haggle; it is open on its contract until it ends.</summary>
    public void Add(Negotiation negotiation)
    {
        _negotiations[negotiation.Id] = negotiation;
        if (!_negotiationsOn.TryGetValue(negotiation.Contract, out List<Negotiation>? list))
        {
            list = new List<Negotiation>();
            _negotiationsOn[negotiation.Contract] = list;
        }

        list.Add(negotiation);
        NegotiationsOpened++;
    }

    public IReadOnlyDictionary<NegotiationId, Negotiation> Negotiations => _negotiations;

    public Negotiation Negotiation(NegotiationId id) => _negotiations[id];

    /// <summary>The open haggles over a contract, in the order they began.</summary>
    public IReadOnlyList<Negotiation> NegotiationsOn(ContractId contract)
    {
        if (!_negotiationsOn.TryGetValue(contract, out List<Negotiation>? list))
        {
            return Array.Empty<Negotiation>();
        }

        list.RemoveAll(n => !n.IsOpen);
        return list.ToArray();
    }

    /// <summary>The open haggle between a contract's poster and <paramref name="responder"/>, if there is one.</summary>
    public Negotiation? NegotiationWith(ContractId contract, ActorId responder) =>
        NegotiationsOn(contract).FirstOrDefault(n => n.Responder == responder);

    /// <summary>
    /// A contract left the open board: every haggle still open over it ends. One with the actor that signed it ends agreed,
    /// whatever it last offered (it signed after all); the rest end because the post closed.
    /// </summary>
    internal void CloseNegotiationsOn(Contract contract)
    {
        foreach (Negotiation negotiation in NegotiationsOn(contract.Id))
        {
            if (contract.CounterpartyId == negotiation.Responder)
            {
                negotiation.Agree(negotiation.Responder, CurrentTick);
                NegotiationsAgreed++;
            }
            else
            {
                negotiation.Close(NegotiationEnd.PostClosed, null, CurrentTick);
            }
        }
    }

    internal void NoteAgreed() => NegotiationsAgreed++;

    public void NoteHaggled() => DealsHaggled++;

    internal void NoteDoorOrder(bool refused, bool forStock)
    {
        DoorOrders++;
        if (refused && forStock)
        {
            DoorRefusedForStock++;
        }
        else if (refused)
        {
            DoorRefusedForPrice++;
        }
    }

    internal void NoteSignedFromNews() => SignedFromNews++;

    internal void NoteFoundClosedFromNews() => FoundClosedFromNews++;

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
    /// if a player alert was raised, and <see cref="RunStop.PlayerDecision"/> if a decision landed on the player
    /// (the hard pause, design §8.3); while that decision is pending the world doesn't move at all. In headless
    /// runs neither happens.
    /// </summary>
    public RunStop RunTo(long targetTick)
    {
        if (PlayerActorId is not null && HasPlayerDecisions)
        {
            return RunStop.PlayerDecision;
        }

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

            if (PlayerActorId is not null && HasPlayerDecisions)
            {
                return RunStop.PlayerDecision;
            }

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

    /// <summary>
    /// Record what happened, with its cause (design §16.2): the entry's own, else the contract's latest entry. The
    /// contract it is about, and the Sale a haul delivers, then take it as their latest. Returns the entry as kept.
    /// </summary>
    public JournalEntry Record(JournalEntry entry)
    {
        Contract? about = entry is IContractEntry e && _contracts.TryGetValue(e.Contract, out Contract? c) ? c : null;
        if (entry is INegotiationEntry haggle)
        {
            // A haggle's entries chain among themselves, from the post. Only an accepted offer becomes the contract's latest:
            // the signing follows from it. A haggle that came to nothing leaves the contract's own record as it was.
            long? previous = _negotiationLastEntry.TryGetValue(haggle.Negotiation, out long seq) ? seq : about?.LastEntry;
            JournalEntry kept = Journal.Record(entry.Cause is null && previous is { } p ? entry with { Cause = p } : entry);
            _negotiationLastEntry[haggle.Negotiation] = kept.Seq;
            if (entry is OfferAcceptedEntry && about is not null)
            {
                about.LastEntry = kept.Seq;
            }

            return kept;
        }

        if (entry.Cause is null && about?.LastEntry is { } last)
        {
            entry = entry with { Cause = last };
        }

        JournalEntry recorded = Journal.Record(entry);
        if (about is not null)
        {
            about.LastEntry = recorded.Seq;
            if (about is HaulContract { Serves: { } served } && _contracts.TryGetValue(served, out Contract? sale) && sale.IsLive)
            {
                sale.LastEntry = recorded.Seq;
            }
        }

        return recorded;
    }

    /// <summary>Sign a contract for <paramref name="taker"/> at <paramref name="price"/>. Escrow and bonds are <see cref="Market"/>'s.</summary>
    internal void Sign(Contract contract, ActorState taker, Credits price)
    {
        contract.Sign(taker.Id, price, CurrentTick);
        Index(taker.Id, contract);
    }

    private void Index(ActorId actor, Contract contract)
    {
        if (!_liveByActor.TryGetValue(actor, out List<Contract>? list))
        {
            list = new List<Contract>();
            _liveByActor[actor] = list;
        }

        if (!list.Contains(contract))
        {
            list.Add(contract);
        }
    }

    /// <summary>The contracts <paramref name="actor"/> is party to that are open or signed and unsettled, as they stand now.</summary>
    public IEnumerable<Contract> LiveContractsOf(ActorId actor)
    {
        if (!_liveByActor.TryGetValue(actor, out List<Contract>? list))
        {
            return Array.Empty<Contract>();
        }

        list.RemoveAll(c => !c.IsLive);
        return list.ToArray();
    }

    // ---- world graph helpers ----
    public Route? RouteBetween(LocationId a, LocationId b) =>
        _routes.FirstOrDefault(r => r.Connects(a, b));

    /// <summary>Whether a route joins two locations directly.</summary>
    public bool Adjacent(LocationId a, LocationId b)
    {
        if (_adjacent.Count == 0)
        {
            foreach (Route route in _routes)
            {
                Neighbour(route.A).Add(route.B);
                Neighbour(route.B).Add(route.A);
            }
        }

        return _adjacent.TryGetValue(a, out HashSet<LocationId>? set) && set.Contains(b);
    }

    private HashSet<LocationId> Neighbour(LocationId id)
    {
        if (!_adjacent.TryGetValue(id, out HashSet<LocationId>? set))
        {
            set = new HashSet<LocationId>();
            _adjacent[id] = set;
        }

        return set;
    }

    public IEnumerable<Location> Neighbours(LocationId from) =>
        _routes.Where(r => r.A == from || r.B == from)
               .Select(r => _locations[r.Other(from)]);

    /// <summary>
    /// Shortest path (by total travel ticks) between two locations, or null if unreachable. Returns the
    /// summed distance and the single most dangerous leg, so a Helios→frontier haul correctly inherits
    /// the red leg's risk. Deterministic: ties break by location id. Cached per pair, since routes are static.
    /// </summary>
    public RoutePlan? FindPath(LocationId from, LocationId to)
    {
        if (!_pathCache.TryGetValue((from, to), out RoutePlan? plan))
        {
            plan = SolvePath(from, to);
            _pathCache[(from, to)] = plan;
        }

        return plan;
    }

    private RoutePlan? SolvePath(LocationId from, LocationId to)
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
        return path.ToArray();
    }

    /// <summary>Open and negotiating contracts in id order, as they stand when enumeration starts.</summary>
    public IEnumerable<Contract> OpenContracts => EnumerateOpenContracts();

    private IEnumerable<Contract> EnumerateOpenContracts()
    {
        _openCandidates.RemoveAll(c => !IsOpen(c));
        Contract[] snapshot = _openCandidates.ToArray();
        foreach (Contract contract in snapshot)
        {
            yield return contract;
        }
    }

    private static bool IsOpen(Contract contract) => contract.IsOpen;

    /// <summary>Every actor, in id order. Actors are never removed, so the order is kept until one is added.</summary>
    public IReadOnlyList<ActorState> ActorsOrdered => _actorsOrdered ??= _actors.Values.OrderBy(a => a.Id.Value).ToArray();

    /// <summary>The producers of a good, in id order.</summary>
    public IReadOnlyList<ActorState> ProducersOf(GoodType good)
    {
        if (!_producersOf.TryGetValue(good, out ActorState[]? producers))
        {
            producers = ActorsOrdered.Where(a => a.Producer is { } p && p.Good == good).ToArray();
            _producersOf[good] = producers;
        }

        return producers;
    }

    /// <summary>The station operator at a location, who rents out storage there (design §8.1).</summary>
    public ActorState? StationOperatorAt(LocationId location)
    {
        foreach (ActorId id in _locations[location].Residents)
        {
            ActorState a = _actors[id];
            if (a.Station is not null)
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

    /// <summary>Everything held in escrow, summed over the contracts that hold any (a settled contract holds nothing).</summary>
    public Credits TotalEscrowHeld()
    {
        _escrowed.RemoveAll(c => c.EscrowHeld.Amount == 0m && !c.IsLive);
        decimal sum = 0m;
        foreach (Contract c in _escrowed)
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

    /// <summary>Move as much of <paramref name="amount"/> as the payer has. Returns what moved.</summary>
    public Credits TransferUpTo(ActorState from, ActorState to, Credits amount)
    {
        decimal actual = Math.Min(amount.Amount, Math.Max(0m, from.Credits.Amount));
        if (actual <= 0m)
        {
            return Credits.Zero;
        }

        var moved = Credits.Of(actual);
        from.ForceDebit(moved);
        to.Credit(moved);
        return moved;
    }

    // ---- escrow (held by the Accord clearing house in whole cents, so its books reconcile exactly; counted in TotalCredits) ----

    /// <summary>
    /// Lock <paramref name="amount"/>, to the cent, from <paramref name="payer"/> into a contract's escrow. A payer with just
    /// the amount but not its rounding-up locks the cents it has.
    /// </summary>
    public bool LockToEscrow(Contract contract, ActorState payer, Credits amount)
    {
        amount = Cents(amount);
        if (amount > payer.Credits && payer.Credits.Amount > 0m)
        {
            amount = Credits.Of(Math.Floor(payer.Credits.Amount * 100m) / 100m);
        }

        if (amount.Amount <= 0m)
        {
            return true;
        }

        if (!payer.TryDebit(amount))
        {
            return false;
        }

        if (contract.EscrowHeld.Amount == 0m && !_escrowed.Contains(contract))
        {
            _escrowed.Add(contract);
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

    // Escrow pays out in whole cents and never more than a contract holds: a settlement's shares (pro rata, refunds, tax)
    // are rounded to the cent, and the last payout of a settlement takes exactly what is left.
    private static Credits CapToHeld(Contract contract, Credits amount) =>
        amount.Amount <= 0m ? Credits.Zero : Credits.Of(System.Math.Min(Cents(amount).Amount, contract.EscrowHeld.Amount));

    private static Credits Cents(Credits amount) => Credits.Of(Math.Round(amount.Amount, 2, MidpointRounding.AwayFromZero));

    public void TaxDirect(ActorState payer, Credits amount)
    {
        Credits sunk = ApplySink(payer, amount);
        GovernmentRevenue += sunk;
    }

    // ---- goods (design §16.1, §20.2). Goods are created only by Produce, destroyed only by Consume and Lose,
    // and otherwise move between an actor's stock, its storage and ships' holds through the methods below. ----
    public decimal GoodsProduced(GoodType good) => _produced.GetValueOrDefault(good);

    public decimal GoodsConsumed(GoodType good) => _consumed.GetValueOrDefault(good);

    public decimal GoodsLost(GoodType good) => _lost.GetValueOrDefault(good);

    /// <summary>Everything of a good that exists: in stock, in storage and in holds.</summary>
    public decimal GoodsHeld(GoodType good)
    {
        decimal sum = 0m;
        foreach (ActorState a in _actors.Values)
        {
            sum += a.Stock(good);
        }

        foreach (StorageContract s in _storages)
        {
            sum += s.Held(good);
        }

        foreach (Ship ship in _ships.Values)
        {
            foreach (Lot lot in ship.Hold)
            {
                if (lot.Good == good)
                {
                    sum += lot.Quantity;
                }
            }
        }

        return sum;
    }

    /// <summary>Make goods into an actor's stock (a producer's output, or a world's starting stock).</summary>
    public void Produce(ActorState actor, GoodType good, decimal quantity)
    {
        if (quantity <= 0m)
        {
            return;
        }

        actor.AddStock(good, quantity);
        _produced[good] = GoodsProduced(good) + quantity;
    }

    /// <summary>Use up to <paramref name="quantity"/> from an actor's stock; returns what was used.</summary>
    public decimal Consume(ActorState actor, GoodType good, decimal quantity)
    {
        decimal used = actor.RemoveStock(good, Math.Min(quantity, actor.Available(good)));
        _consumed[good] = GoodsConsumed(good) + used;
        actor.NoteConsumed(good, used);
        return used;
    }

    /// <summary>Book goods already taken out of a holding as lost (a transit incident).</summary>
    public void Lose(GoodType good, decimal quantity)
    {
        if (quantity > 0m)
        {
            _lost[good] = GoodsLost(good) + quantity;
        }
    }

    /// <summary>Take goods out of an actor's stock: set-aside goods for the sale they were set aside for, or free ones. Stock is fresh.</summary>
    public Cargo TakeFromStock(ActorState owner, GoodType good, decimal quantity, bool reserved)
    {
        decimal taken = owner.RemoveStock(good, Math.Min(quantity, reserved ? owner.Stock(good) : owner.Available(good)));
        if (reserved)
        {
            owner.Unreserve(good, taken);
        }

        return Cargo.Fresh(good, taken);
    }

    /// <summary>Put goods into an actor's stock. A facility's stock doesn't track quality.</summary>
    public void PutInStock(ActorState owner, Cargo cargo) => owner.AddStock(cargo.Good, cargo.Quantity);

    public void ReserveStock(ActorState owner, GoodType good, decimal quantity) => owner.Reserve(good, quantity);

    public void UnreserveStock(ActorState owner, GoodType good, decimal quantity) => owner.Unreserve(good, quantity);

    /// <summary>The renter's open store at a location, if it has one.</summary>
    public StorageContract? StorageOf(ActorId renter, LocationId location) =>
        _storageByRenter.TryGetValue((renter, location), out StorageContract? storage) ? storage : null;

    /// <summary>The open stores at a location, in the order they were opened.</summary>
    public IEnumerable<StorageContract> StoragesAt(LocationId location) =>
        _storages.Where(s => s.Location == location).ToArray();

    /// <summary>Every open store, in the order they were opened.</summary>
    public IReadOnlyList<StorageContract> OpenStorages => _storages;

    /// <summary>One more than the largest location id: the length of an array indexed by location.</summary>
    public long LocationIdLimit => _nextLocationId;

    /// <summary>The renter's open stores, in the order they were opened.</summary>
    public IEnumerable<StorageContract> StoragesOf(ActorId renter) =>
        _storages.Where(s => s.Renter == renter).ToArray();

    /// <summary>Put goods into the owner's storage at a location, renting it from the station operator first if needed.</summary>
    public StorageContract PutInStorage(ActorState owner, LocationId location, Cargo cargo)
    {
        StorageContract storage = StorageOf(owner.Id, location) ?? OpenStorage(owner, location);
        SettleRent(storage);
        storage.Deposit(cargo, CurrentTick);
        return storage;
    }

    /// <summary>Take goods out of the owner's storage at a location; the store closes when it is empty.</summary>
    public Cargo TakeFromStorage(ActorState owner, LocationId location, GoodType good, decimal quantity, bool reserved)
    {
        StorageContract? storage = StorageOf(owner.Id, location);
        if (storage is null)
        {
            return new Cargo(good, 0m, 1m);
        }

        SettleRent(storage);
        Cargo taken = storage.Withdraw(good, quantity, CurrentTick, reserved);
        if (storage.QuantityHeld <= 0m)
        {
            storage.Close();
            _storageByRenter.Remove((owner.Id, location));
            _storages.Remove(storage);
            Record(new StorageClosedEntry(CurrentTick, storage.Id, owner.Id, storage.RentPaid));
        }

        return taken;
    }

    public void ReserveStorage(StorageContract storage, GoodType good, decimal quantity) => storage.Reserve(good, quantity);

    public void UnreserveStorage(StorageContract storage, GoodType good, decimal quantity) => storage.Unreserve(good, quantity);

    /// <summary>Charge the rent owed on a store since it was last settled: what the renter can pay goes to the operator.</summary>
    public void SettleRent(StorageContract storage)
    {
        Credits due = storage.RentDue(CurrentTick);
        Credits paid = due.Amount > 0m
            ? TransferUpTo(Actor(storage.Renter), Actor(storage.Operator), due)
            : Credits.Zero;
        storage.Charged(CurrentTick, paid);
    }

    private StorageContract OpenStorage(ActorState renter, LocationId location)
    {
        ActorState station = StationOperatorAt(location)
                             ?? throw new InvalidOperationException($"No station operator at {location} to rent storage from.");
        Location place = Location(location);
        var storage = new StorageContract(
            NextContractId(), station.Id, renter.Id, CurrentTick, location, station.Station!.StorageRatePerUnitDay, place.Reach, place.Region);
        Add(storage);
        Record(new StorageOpenedEntry(CurrentTick, storage.Id, renter.Id));
        return storage;
    }

    /// <summary>Put goods aboard a ship, owned by <paramref name="owner"/>.</summary>
    public void Load(Ship ship, ActorId owner, Cargo cargo)
    {
        if (cargo.Quantity > 0m)
        {
            ship.Load(CurrentTick, owner, cargo);
        }
    }

    /// <summary>Take an owner's goods out of a ship's hold, as they stand now.</summary>
    public Cargo Unload(Ship ship, ActorId owner, GoodType good, decimal quantity) =>
        ship.Unload(CurrentTick, owner, good, quantity);

    // ---- counters ----
    public void NoteFulfilled() => ContractsFulfilled++;

    public void NoteBreached() => ContractsBreached++;

    public void NoteDefaulted() => ContractsDefaulted++;

    public void NoteCancelled() => ContractsCancelled++;

    /// <summary>Count one occurrence of a failure path (design §16.3).</summary>
    public void NoteFailure(FailureKind kind) => _failures[(int)kind]++;

    /// <summary>How often a failure path has happened.</summary>
    public long Failures(FailureKind kind) => _failures[(int)kind];

    public void NoteIncident() => IncidentsOccurred++;

    public void NoteSold(decimal quantity) => GoodsSold += quantity;

    // ---- player hard-pause alerts (§3.4). In headless runs PlayerActorId is null and these never fire. ----
    // An alert is the journal entry that needs the player's attention; the interface words it.
    public IReadOnlyList<JournalEntry> PlayerAlerts => _playerAlerts;

    public bool HasPlayerAlerts => _playerAlerts.Count > 0;

    public void RaisePlayerAlert(JournalEntry entry) => _playerAlerts.Add(entry);

    public void ClearPlayerAlerts() => _playerAlerts.Clear();

    // ---- decisions waiting on the player (design §8.3). While any is pending RunTo doesn't move the world. A decision
    // about a contract that has since ended some other way (the player cancelled it at will) lapses. ----
    public IReadOnlyList<PlayerDecision> PlayerDecisions
    {
        get
        {
            _playerDecisions.RemoveAll(d => _contracts[d.Contract].Status != ContractStatus.Active);
            return _playerDecisions;
        }
    }

    public bool HasPlayerDecisions => PlayerDecisions.Count > 0;

    /// <summary>The pending decision about a contract, if there is one.</summary>
    public PlayerDecision? PlayerDecisionFor(ContractId contract) => PlayerDecisions.FirstOrDefault(d => d.Contract == contract);

    internal void RaisePlayerDecision(PlayerDecision decision) => _playerDecisions.Add(decision);

    internal bool ResolvePlayerDecision(PlayerDecision decision) => _playerDecisions.Remove(decision);
}
