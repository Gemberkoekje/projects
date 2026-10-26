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

/// <summary>One line in a ship's life story. "Every object tells a story" (§1.1, §5.7).</summary>
public sealed record ShipLogEntry(long Tick, string Text);

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
    /// <summary>The contract the job is mainly for: the haul it flies, else the sale it delivers, else the one it collects.</summary>
    public ContractId Main => Haul ?? Delivery ?? Purchase
        ?? throw new System.InvalidOperationException("A ship job serves no contract.");

    public bool Serves(ContractId contract) => Haul == contract || Purchase == contract || Delivery == contract;
}

/// <summary>
/// A ship aggregate. Long-running, with an event-log history surfaced by the Ship Sheet (§5.7). Its hold
/// carries lots of goods, each with its owner: a carrier's hold holds the shipper's goods.
/// </summary>
public sealed class Ship
{
    private readonly List<ShipLogEntry> _log = new();
    private readonly List<Lot> _hold = new();

    public Ship(ShipId id, ShipType type, ActorId owner, LocationId location, long createdTick)
    {
        Id = id;
        Type = type;
        Owner = owner;
        Location = location;
        Status = ShipStatus.Docked;
        ShipClassInfo info = ShipClasses.Info(type);
        CargoCapacity = info.CargoCapacity;
        Log(createdTick, $"Commissioned: {info.Name}, cargo capacity {CargoCapacity:0}.");
    }

    public ShipId Id { get; }

    public ShipType Type { get; }

    public ActorId Owner { get; private set; }

    /// <summary>Where the ship is, or last was: it changes when it loads at a pickup and when it arrives.</summary>
    public LocationId Location { get; private set; }

    public ShipStatus Status { get; private set; }

    /// <summary>Effective capacity. Equals class capacity in slice 0; mutable so Slice 2 repairs can shave it.</summary>
    public decimal CargoCapacity { get; private set; }

    /// <summary>What the ship is committed to, if anything.</summary>
    public ShipJob? Job { get; private set; }

    /// <summary>The goods aboard, by owner and good, each with its quality as last read.</summary>
    public IReadOnlyList<Lot> Hold => _hold;

    public decimal HoldQuantity => _hold.Sum(l => l.Quantity);

    public decimal FreeSpace => System.Math.Max(0m, CargoCapacity - HoldQuantity);

    /// <summary>The tick this ship last left a pickup laden.</summary>
    public long DepartedTick { get; private set; }

    /// <summary>The leg the ship is flying, empty or laden; null while it sits at <see cref="Location"/>.</summary>
    public ShipCourse? Course { get; private set; }

    public ShipClassInfo Class => ShipClasses.Info(Type);

    /// <summary>How much this hold slows perishable decay: 0 for a standard hold.</summary>
    public decimal Preservation => Class.Preservation;

    public IReadOnlyList<ShipLogEntry> History => _log;

    public bool IsFree => Status == ShipStatus.Docked && Job is null;

    public void Log(long tick, string text) => _log.Add(new ShipLogEntry(tick, text));

    /// <summary>The lot of <paramref name="good"/> that <paramref name="owner"/> has aboard, if any.</summary>
    public Lot? LotOf(ActorId owner, GoodType good) => _hold.FirstOrDefault(l => l.Owner == owner && l.Good == good);

    /// <summary>
    /// Commit the ship to a job before it physically departs, so a heartbeat won't double-book it.
    /// <paramref name="reposition"/> is the empty leg to the pickup, if the ship isn't already there.
    /// </summary>
    public void Reserve(long tick, ShipJob job, ShipCourse? reposition)
    {
        Status = ShipStatus.InTransit;
        Job = job;
        Course = reposition;
        Log(tick, $"Committed to contract {job.Main}.");
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

        Log(tick, $"Loaded {cargo.Quantity:0} {cargo.Good} at {Location}.");
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
        Log(tick, $"Departed {Location} for {course.To}.");
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
        Log(tick, $"Arrived {destination}.");
    }

    /// <summary>The job is done or has failed: the ship is free where it is.</summary>
    public void FinishJob(long tick)
    {
        Job = null;
        Course = null;
        Status = ShipStatus.Docked;
        Log(tick, "Free.");
    }

    public void TransferOwnership(long tick, ActorId newOwner)
    {
        Owner = newOwner;
        Log(tick, $"Ownership transferred to {newOwner}.");
    }
}
