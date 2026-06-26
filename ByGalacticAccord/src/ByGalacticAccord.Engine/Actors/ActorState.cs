using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Engine.Actors;

/// <summary>
/// An actor aggregate (§4.5). The player is just an actor whose decisions come from a human
/// rather than a personality vector (§4.7.3) — <see cref="IsPlayer"/> is the only distinction,
/// and the simulation never branches on it except to surface a prompt instead of auto-deciding.
/// </summary>
public sealed class ActorState
{
    private readonly Dictionary<GoodType, decimal> _stock = new();
    private readonly List<ShipId> _ships = new();

    public ActorState(
        ActorId id,
        string name,
        ActorRole role,
        LocationId home,
        Personality personality,
        Credits credits)
    {
        Id = id;
        Name = name;
        Role = role;
        Home = home;
        Personality = personality;
        Credits = credits;
    }

    public ActorId Id { get; }

    public string Name { get; }

    public ActorRole Role { get; }

    /// <summary>Where the actor and its idle ships sit. Also the actor's reputation "home" region key.</summary>
    public LocationId Home { get; set; }

    public Personality Personality { get; }

    public Credits Credits { get; private set; }

    public bool IsPlayer { get; init; }

    public ProducerProfile? Producer { get; init; }

    public IReadOnlyList<ConsumerDemand> Demands { get; init; } = new List<ConsumerDemand>();

    /// <summary>Consumer income (the faucet, §4.2.1): credits injected per heartbeat.</summary>
    public Credits IncomePerHeartbeat { get; init; } = Credits.Zero;

    public IReadOnlyList<ShipId> Ships => _ships;

    public decimal Stock(GoodType good) => _stock.TryGetValue(good, out decimal q) ? q : 0m;

    public IReadOnlyDictionary<GoodType, decimal> AllStock => _stock;

    public void AddStock(GoodType good, decimal quantity) =>
        _stock[good] = Stock(good) + quantity;

    public void RemoveStock(GoodType good, decimal quantity) =>
        _stock[good] = System.Math.Max(0m, Stock(good) - quantity);

    public void Credit(Credits amount) => Credits += amount;

    /// <summary>Debit credits. Returns false (and does nothing) if it would go negative — the actor can't afford it.</summary>
    public bool TryDebit(Credits amount)
    {
        if (Credits < amount)
        {
            return false;
        }

        Credits -= amount;
        return true;
    }

    public void ForceDebit(Credits amount) => Credits -= amount;

    public void AddShip(ShipId ship)
    {
        if (!_ships.Contains(ship))
        {
            _ships.Add(ship);
        }
    }

    public void RemoveShip(ShipId ship) => _ships.Remove(ship);

    public bool Owns(ShipId ship) => _ships.Contains(ship);

    public bool MovesCargo => Role is ActorRole.ShippingCompany or ActorRole.IndependentPilot;

    public override string ToString() => $"{Name} ({Id})";
}
