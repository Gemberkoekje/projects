using System.Collections.Generic;
using System.Linq;

namespace ByGalacticAccord.Engine.Domain;

/// <summary>
/// Whether a ship is free. <see cref="InTransit"/> covers a whole job: the empty leg to the pickup, loading,
/// and the laden leg. There is no continuous position (design §19).
/// </summary>
public enum ShipStatus
{
    Docked,
    InTransit,
}

/// <summary>Where a job's goods come from at its pickup.</summary>
public enum PickupSource
{
    /// <summary>Handed over by a seller under a Sale the ship's owner bought: the ship collects.</summary>
    Purchase,

    /// <summary>The shipper's goods under a Haul the ship's owner took.</summary>
    Haul,

    /// <summary>The ship owner's own goods in storage at the pickup.</summary>
    Storage,

    /// <summary>Already in the hold.</summary>
    Aboard,
}

/// <summary>What a line of a ship's history records (design §6.4).</summary>
public enum ShipEventKind
{
    /// <summary>Built (or, for a ship the sector began with, first registered): by whom, where, and from which parts.</summary>
    Commissioned,

    /// <summary>Sold, or handed over to the client that ordered it: from whom to whom, at what price, under which contract.</summary>
    ChangedHands,

    /// <summary>A transit incident on a run, and what kind.</summary>
    Incident,

    /// <summary>Broken up by a salvage yard that couldn't sell it: it leaves the sector.</summary>
    Scrapped,
}

/// <summary>
/// One line of a ship's history (design §6.4: where it was built, from which parts at which grades, what was substituted,
/// its owners, incidents, and the contracts it flew). Data, not prose: the interface words it (design §11.5).
/// <see cref="Wanted"/> is what the build ordered, so a substitution reads as the difference from the parts fitted.
/// </summary>
public sealed record ShipRecord(
    long Tick,
    ShipEventKind Kind,
    ActorId? From = null,
    ActorId? To = null,
    ContractId? Contract = null,
    Credits Price = default,
    LocationId? At = null,
    ShipParts? Wanted = null,
    Simulation.TransitIncidentKind? Incident = null);

/// <summary>
/// The leg a ship is flying: the locations it passes, origin to destination, and when it left and is due.
/// The engine never moves a ship between events (design §19); this is what the client interpolates
/// along. An incident's delay moves <see cref="ArrivalTick"/>.
/// </summary>
public sealed record ShipCourse(IReadOnlyList<LocationId> Path, long DepartedTick, long ArrivalTick)
{
    public LocationId From => Path[0];

    public LocationId To => Path[Path.Count - 1];

    /// <summary>A course along a planned path, leaving now and taking <paramref name="travelTicks"/>.</summary>
    public static ShipCourse Along(RoutePlan plan, long now, long travelTicks) => new(plan.Path, now, now + travelTicks);
}

/// <summary>
/// What a ship has been committed to: load <see cref="Quantity"/> of a good at <see cref="Pickup"/> and, if
/// there is a <see cref="Destination"/>, carry it there. The contracts it serves say what happens at each end:
/// a Haul's pickup and delivery, a Sale bought and collected at the pickup, a Sale handed over at the
/// destination. A job with no destination ends when the goods are loaded.
/// </summary>
public sealed record ShipJob(
    ActorId GoodsOwner,
    GoodType Good,
    decimal Quantity,
    LocationId Pickup,
    PickupSource Source,
    LocationId? Destination,
    ContractId? Haul = null,
    ContractId? Purchase = null,
    ContractId? Delivery = null)
{
    /// <summary>The contract the job is mainly for: the haul it flies, else the sale it delivers, else the one it collects; none for a ferry.</summary>
    public ContractId Main => Haul ?? Delivery ?? Purchase ?? default;

    /// <summary>A ferry: the ship flies empty to <see cref="Pickup"/> for its owner, under no contract (a ship it bought going home).</summary>
    public bool IsFerry => Haul is null && Delivery is null && Purchase is null;

    public bool Serves(ContractId contract) => Haul == contract || Purchase == contract || Delivery == contract;
}

/// <summary>
/// A ship aggregate (design §6): built from parts (<see cref="Parts"/>), its stats always computed from them
/// (<see cref="Stats"/>), with a history (design §6.4). Its hold carries lots of goods, each with its owner: a carrier's hold
/// holds the shipper's goods.
/// </summary>
public sealed class Ship
{
    private const int BusyKept = 64;
    private const int FlownKept = 12;

    private readonly List<ShipRecord> _history = new();
    private readonly List<ContractId> _flown = new();
    private readonly List<Lot> _hold = new();
    private readonly List<(long From, long To)> _busy = new();
    private long? _busySince;

    /// <summary>
    /// A ship of <paramref name="parts"/>, commissioned now for <paramref name="owner"/> at <paramref name="location"/>: built by
    /// <paramref name="builtBy"/> under <paramref name="build"/>, which ordered <paramref name="wanted"/>; for a ship the sector
    /// begins with, nobody.
    /// </summary>
    public Ship(ShipId id, ShipParts parts, ActorId owner, LocationId location, long createdTick, ActorId? builtBy = null, ContractId? build = null, ShipParts? wanted = null)
    {
        Id = id;
        Parts = parts;
        Stats = ShipStats.From(parts);
        Owner = owner;
        Location = location;
        Status = ShipStatus.Docked;
        BuiltTick = createdTick;
        BuiltBy = builtBy;
        BuiltAt = location;
        FreeSince = createdTick;
        OwnedSince = createdTick;
        _history.Add(new ShipRecord(createdTick, ShipEventKind.Commissioned, builtBy, owner, build, At: location, Wanted: wanted ?? parts));
    }

    /// <summary>A ship of a standard design, every part at <paramref name="grade"/>.</summary>
    public Ship(ShipId id, Design design, ActorId owner, LocationId location, long createdTick, Grade grade = Grade.A)
        : this(id, Designs.Parts(design, grade), owner, location, createdTick)
    {
    }

    public ShipId Id { get; }

    /// <summary>What it is built from (design §6.1).</summary>
    public ShipParts Parts { get; }

    /// <summary>Its stats, from its parts.</summary>
    public ShipStats Stats { get; }

    /// <summary>The standard design its parts make, whatever their grades; null for a custom build.</summary>
    public Design? Design => Parts.Design;

    /// <summary>Who built it (a yard), and where and when; nobody for a ship the sector began with.</summary>
    public ActorId? BuiltBy { get; }

    public LocationId BuiltAt { get; }

    public long BuiltTick { get; }

    public ActorId Owner { get; private set; }

    /// <summary>Since when its owner has had it: built, or bought.</summary>
    public long OwnedSince { get; private set; }

    /// <summary>Where the ship is, or last was: it changes when it loads at a pickup and when it arrives.</summary>
    public LocationId Location { get; private set; }

    public ShipStatus Status { get; private set; }

    /// <summary>Its cargo capacity, from its parts.</summary>
    public decimal CargoCapacity => Stats.CargoCapacity;

    /// <summary>What the ship is committed to, if anything.</summary>
    public ShipJob? Job { get; private set; }

    /// <summary>The ship Sale it is posted for, if its owner has it up for sale: it takes no work meanwhile.</summary>
    public ContractId? ListedFor { get; private set; }

    /// <summary>The goods aboard, by owner and good, each with its quality as last read.</summary>
    public IReadOnlyList<Lot> Hold => _hold;

    public decimal HoldQuantity => _hold.Sum(l => l.Quantity);

    public decimal FreeSpace => System.Math.Max(0m, CargoCapacity - HoldQuantity);

    /// <summary>The tick this ship last left a pickup laden.</summary>
    public long DepartedTick { get; private set; }

    /// <summary>The leg the ship is flying, empty or laden; null while it sits at <see cref="Location"/>.</summary>
    public ShipCourse? Course { get; private set; }

    /// <summary>How much this hold slows perishable decay: 0 for a standard hold.</summary>
    public decimal Preservation => Stats.Preservation;

    /// <summary>Its history, oldest first (design §6.4).</summary>
    public IReadOnlyList<ShipRecord> History => _history;

    /// <summary>How many contracts it has flown, and the last few, oldest first.</summary>
    public int ContractsFlown { get; private set; }

    public IReadOnlyList<ContractId> RecentlyFlown => _flown;

    /// <summary>Since when it has sat free (or since it was built); meaningless while it has a job.</summary>
    public long FreeSince { get; private set; }

    public bool IsFree => Status == ShipStatus.Docked && Job is null && ListedFor is null && !Scrapped;

    /// <summary>Whether it has been broken up: it is kept for the record, but belongs to no fleet.</summary>
    public bool Scrapped { get; private set; }

    /// <summary>The lot of <paramref name="good"/> that <paramref name="owner"/> has aboard, if any.</summary>
    public Lot? LotOf(ActorId owner, GoodType good) => _hold.FirstOrDefault(l => l.Owner == owner && l.Good == good);

    /// <summary>
    /// The share of the last <paramref name="window"/> hours (or of its life, if younger) it spent on jobs: what its owner
    /// judges whether it needs another ship, or one fewer, by.
    /// </summary>
    public decimal Utilization(long now, long window)
    {
        long from = System.Math.Max(BuiltTick, now - window);
        long span = now - from;
        if (span <= 0)
        {
            return _busySince is not null ? 1m : 0m;
        }

        long busy = 0;
        foreach ((long start, long end) in _busy)
        {
            busy += System.Math.Max(0, System.Math.Min(end, now) - System.Math.Max(start, from));
        }

        if (_busySince is { } since)
        {
            busy += System.Math.Max(0, now - System.Math.Max(since, from));
        }

        return System.Math.Clamp((decimal)busy / span, 0m, 1m);
    }

    internal void Record(ShipRecord record) => _history.Add(record);

    /// <summary>
    /// Commit the ship to a job before it physically departs, so a heartbeat won't double-book it.
    /// <paramref name="reposition"/> is the empty leg to the pickup, if the ship isn't already there.
    /// </summary>
    public void Reserve(long tick, ShipJob job, ShipCourse? reposition)
    {
        Status = ShipStatus.InTransit;
        Job = job;
        Course = reposition;
        _busySince ??= tick;
    }

    /// <summary>At the pickup, where any empty leg ended.</summary>
    public void ReachPickup(LocationId pickup)
    {
        Location = pickup;
        Course = null;
    }

    /// <summary>Put goods aboard; a lot of the same owner and good merges with what is there.</summary>
    public void Load(long tick, ActorId owner, Cargo cargo)
    {
        int index = _hold.FindIndex(l => l.Owner == owner && l.Good == cargo.Good);
        if (index < 0)
        {
            _hold.Add(new Lot(owner, cargo, tick));
        }
        else
        {
            Lot current = _hold[index].At(tick, Preservation);
            _hold[index] = current with { Cargo = current.Cargo.Merged(cargo) };
        }
    }
    /// <summary>Take up to <paramref name="quantity"/> of an owner's good out of the hold, as it stands now.</summary>
    public Cargo Unload(long tick, ActorId owner, GoodType good, decimal quantity)
    {
        int index = _hold.FindIndex(l => l.Owner == owner && l.Good == good);
        if (index < 0)
        {
            return new Cargo(good, 0m, 1m);
        }

        Lot current = _hold[index].At(tick, Preservation);
        decimal taken = System.Math.Min(quantity, current.Quantity);
        if (taken >= current.Quantity)
        {
            _hold.RemoveAt(index);
        }
        else
        {
            _hold[index] = current with { Cargo = current.Cargo.LosingUnits(taken) };
        }

        return current.Cargo with { Quantity = taken };
    }

    /// <summary>Bring an owner's lot up to <paramref name="tick"/> and change it (damage, loss in an incident).</summary>
    public void Alter(long tick, ActorId owner, GoodType good, System.Func<Cargo, Cargo> change)
    {
        int index = _hold.FindIndex(l => l.Owner == owner && l.Good == good);
        if (index < 0)
        {
            return;
        }

        Lot current = _hold[index].At(tick, Preservation);
        Cargo changed = change(current.Cargo);
        if (changed.Quantity <= 0m)
        {
            _hold.RemoveAt(index);
        }
        else
        {
            _hold[index] = current with { Cargo = changed };
        }
    }

    /// <summary>Leave the pickup laden, along <paramref name="course"/>.</summary>
    public void DepartLaden(long tick, ShipCourse course)
    {
        DepartedTick = tick;
        Course = course;
    }

    /// <summary>An incident's delay pushes the arrival back.</summary>
    public void DelayArrival(long ticks)
    {
        if (Course is { } course && ticks > 0)
        {
            Course = course with { ArrivalTick = course.ArrivalTick + ticks };
        }
    }

    public void Arrive(long tick, LocationId destination)
    {
        Location = destination;
        Course = null;
    }

    /// <summary>The job is done or has failed: the ship is free where it is.</summary>
    public void FinishJob(long tick)
    {
        if (Job is { IsFerry: false } job)
        {
            ContractsFlown++;
            _flown.Add(job.Main);
            if (_flown.Count > FlownKept)
            {
                _flown.RemoveAt(0);
            }
        }

        Job = null;
        Course = null;
        Status = ShipStatus.Docked;
        FreeSince = tick;
        if (_busySince is { } since)
        {
            _busy.Add((since, tick));
            if (_busy.Count > BusyKept)
            {
                _busy.RemoveAt(0);
            }

            _busySince = null;
        }
    }

    internal void List(ContractId? sale) => ListedFor = sale;

    internal void Scrap(long tick)
    {
        _history.Add(new ShipRecord(tick, ShipEventKind.Scrapped, Owner, At: Location));
        Scrapped = true;
        ListedFor = null;
    }

    /// <summary>The ship changes hands (design §6.4): sold, or handed over to the client that ordered it.</summary>
    internal void TransferOwnership(long tick, ActorId newOwner, Credits price, ContractId? contract)
    {
        _history.Add(new ShipRecord(tick, ShipEventKind.ChangedHands, Owner, newOwner, contract, price, Location));
        Owner = newOwner;
        ListedFor = null;
        FreeSince = tick;
        OwnedSince = tick;
    }
}
