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
    private readonly Dictionary<GoodType, decimal> _reserved = new();
    private readonly List<ShipId> _ships = new();
    private readonly Dictionary<PriceKey, PriceMemory> _prices = new();
    private readonly Dictionary<GoodType, decimal> _consumed = new();

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

    /// <summary>A station operator's terms: what it charges for storage, and the float it keeps (design §8.1).</summary>
    public StationProfile? Station { get; init; }

    public IReadOnlyList<ShipId> Ships => _ships;

    public decimal Stock(GoodType good) => _stock.TryGetValue(good, out decimal q) ? q : 0m;

    /// <summary>Stock set aside for a sale: posted for sale, or sold and waiting to be handed over or picked up.</summary>
    public decimal Reserved(GoodType good) => _reserved.TryGetValue(good, out decimal q) ? q : 0m;

    /// <summary>Stock that is not set aside.</summary>
    public decimal Available(GoodType good) => System.Math.Max(0m, Stock(good) - Reserved(good));

    /// <summary>Whether the actor keeps goods at home: a producer's warehouse, a consumer's larder (design §16.1).</summary>
    public bool KeepsStock => Producer is not null || Demands.Count > 0;

    /// <summary>Stock by good, in the goods' order.</summary>
    public IEnumerable<(GoodType Good, decimal Quantity)> AllStock =>
        Goods.All.Where(g => Stock(g) > 0m).Select(g => (g, Stock(g)));

    // Stock changes go through SimulationContext, which keeps the goods ledger (design §20.2).
    internal void AddStock(GoodType good, decimal quantity) =>
        _stock[good] = Stock(good) + quantity;

    /// <summary>Remove up to <paramref name="quantity"/>; returns what was removed. Never below zero.</summary>
    internal decimal RemoveStock(GoodType good, decimal quantity)
    {
        decimal removed = System.Math.Min(Stock(good), System.Math.Max(0m, quantity));
        _stock[good] = Stock(good) - removed;
        return removed;
    }

    /// <summary>How much of a good this actor has used up, ever: a consumer's met demand.</summary>
    public decimal Consumed(GoodType good) => _consumed.TryGetValue(good, out decimal q) ? q : 0m;

    internal void NoteConsumed(GoodType good, decimal quantity) => _consumed[good] = Consumed(good) + quantity;

    internal void Reserve(GoodType good, decimal quantity) => _reserved[good] = Reserved(good) + quantity;

    internal void Unreserve(GoodType good, decimal quantity) => _reserved[good] = System.Math.Max(0m, Reserved(good) - quantity);

    /// <summary>
    /// What an AI poster has learned about one kind of post it makes, from its own past posts (design §4.3: it decides on
    /// what it knows). <paramref name="initial"/> is the factor it starts from.
    /// </summary>
    internal PriceMemory Prices(PriceKey key, decimal initial)
    {
        if (!_prices.TryGetValue(key, out PriceMemory? memory))
        {
            memory = new PriceMemory(initial);
            _prices[key] = memory;
        }

        return memory;
    }

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

    public bool MovesCargo => Role is ActorRole.ShippingCompany or ActorRole.IndependentPilot;

    public override string ToString() => $"{Name} ({Id})";
}
