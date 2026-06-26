using System.Collections.Generic;

namespace ByGalacticAccord.Engine.Domain;

/// <summary>Where a ship is in its lifecycle. There is no continuous position — see §4.7.3.</summary>
public enum ShipStatus
{
    Docked,
    InTransit,
}

/// <summary>One line in a ship's life story. "Every object tells a story" (§1.1, §5.7).</summary>
public sealed record ShipLogEntry(long Tick, string Text);

/// <summary>
/// A ship aggregate. Long-running, with an event-log history surfaced by the Ship Sheet (§5.7).
/// In slice 0 stats are canonical (no variance, §6.1) but the per-instance fields are here so
/// Slice 2 variance/degradation can alter them with a logged reason.
/// </summary>
public sealed class Ship
{
    private readonly List<ShipLogEntry> _log = new();

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

    public LocationId Location { get; private set; }

    public ShipStatus Status { get; private set; }

    /// <summary>Effective capacity. Equals class capacity in slice 0; mutable so Slice 2 repairs can shave it.</summary>
    public decimal CargoCapacity { get; private set; }

    /// <summary>The contract this ship is currently committed to, if any.</summary>
    public ContractId? ActiveContract { get; private set; }

    /// <summary>Cargo presently aboard (a single haul's worth in slice 0).</summary>
    public Cargo? Manifest { get; private set; }

    /// <summary>The tick this ship last departed. Position is interpolated from this on demand (§4.7.3).</summary>
    public long DepartedTick { get; private set; }

    public ShipClassInfo Class => ShipClasses.Info(Type);

    public IReadOnlyList<ShipLogEntry> History => _log;

    public void Log(long tick, string text) => _log.Add(new ShipLogEntry(tick, text));

    /// <summary>Commit the ship to a contract before it physically departs, so the heartbeat won't double-book it.</summary>
    public void Reserve(long tick, ContractId contract)
    {
        Status = ShipStatus.InTransit;
        ActiveContract = contract;
        Log(tick, $"Committed to contract {contract}.");
    }

    public void Depart(long tick, Cargo cargo, ContractId contract)
    {
        Status = ShipStatus.InTransit;
        Manifest = cargo;
        ActiveContract = contract;
        DepartedTick = tick;
        Log(tick, $"Departed {Location} with {cargo.Quantity:0} {cargo.Good} for contract {contract}.");
    }

    public void DamageManifest(Cargo cargo) => Manifest = cargo;

    public void Arrive(long tick, LocationId destination)
    {
        Location = destination;
        Status = ShipStatus.Docked;
        Log(tick, $"Arrived {destination}.");
    }

    public void Unload(long tick)
    {
        Manifest = null;
        ActiveContract = null;
        Log(tick, "Cargo released; ship free.");
    }

    public void SetManifest(Cargo cargo) => Manifest = cargo;

    public void TransferOwnership(long tick, ActorId newOwner)
    {
        Owner = newOwner;
        Log(tick, $"Ownership transferred to {newOwner}.");
    }
}
