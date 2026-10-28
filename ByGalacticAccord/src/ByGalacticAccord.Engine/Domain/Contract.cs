using System.Collections.Generic;
using System.Linq;

namespace ByGalacticAccord.Engine.Domain;

/// <summary>
/// Contract kinds (design §3.2). Slice 1 has Sale, Haul, Build and Storage; S3 creates Sale, Haul and Storage.
/// The later kinds are named so the schema is whole.
/// </summary>
public enum ContractType
{
    Sale,
    Haul,
    Storage,
    Build,
    Service,
    Insurance,
    Bond,
    Information,
}

/// <summary>
/// Contract lifecycle states (design §16.2). <see cref="Open"/> is posted and collecting responses: it stays open to
/// everyone while any of them haggles over it (design §3.6). <see cref="Active"/> is signed and being performed. It ends
/// <see cref="Fulfilled"/> (on time or late), <see cref="PartlyFulfilled"/> (a short delivery paid pro rata),
/// <see cref="Breached"/> (the performer failed by the terms: the goods lost, a short delivery treated as a failure,
/// goods rejected on inspection), <see cref="Cancelled"/> (at will, or for default), or <see cref="Defaulted"/> (the
/// payer didn't pay). An open post ends <see cref="Expired"/> or <see cref="Withdrawn"/>.
/// </summary>
public enum ContractStatus
{
    Open,
    Active,
    Fulfilled,
    PartlyFulfilled,
    Breached,
    Cancelled,
    Defaulted,
    Expired,
    Withdrawn,
}

/// <summary>Who posted a Sale: the buyer ("wanted") or the seller ("for sale") (design §3.2).</summary>
public enum SaleSide
{
    Wanted,
    ForSale,
}

/// <summary>The three places goods can sit (design §16.1).</summary>
public enum HoldingKind
{
    /// <summary>An actor's own stock at its home: a producer's warehouse, a consumer's larder.</summary>
    Stock,

    /// <summary>Space rented from a station operator under a Storage contract.</summary>
    Storage,

    /// <summary>A ship's hold.</summary>
    Ship,
}

/// <summary>Where an actor's goods are, or are to be put: its stock, its storage at a location, or one of its ships.</summary>
public readonly record struct Holding(HoldingKind Kind, LocationId Location, ShipId? Ship = null)
{
    public static Holding StockAt(LocationId home) => new(HoldingKind.Stock, home);

    public static Holding StorageAt(LocationId location) => new(HoldingKind.Storage, location);

    public static Holding Aboard(ShipId ship, LocationId location) => new(HoldingKind.Ship, location, ship);
}

/// <summary>
/// The Accord standard contract (design §3.1): parties, a price, a schedule, terms, security and jurisdiction.
/// Each kind adds its obligation. A contract is a plain mutable aggregate that events advance; it is never
/// removed from the world, so the journal can always refer back to it.
/// </summary>
public abstract class Contract
{
    protected Contract(
        ContractId id,
        ActorId postedBy,
        long postedTick,
        Credits offeredPrice,
        Credits reservation,
        decimal taxRate,
        long deadlineTick,
        AccordReach jurisdiction,
        string region,
        ContractTerms terms)
    {
        Id = id;
        PostedBy = postedBy;
        PostedTick = postedTick;
        OfferedPrice = offeredPrice;
        Reservation = reservation;
        TaxRate = taxRate;
        DeadlineTick = deadlineTick;
        PostedDeadline = deadlineTick;
        Jurisdiction = jurisdiction;
        Region = region;
        Terms = terms;
        Status = ContractStatus.Open;
    }

    public ContractId Id { get; }

    public abstract ContractType Type { get; }

    public ActorId PostedBy { get; }

    /// <summary>Who took it; empty while it is open.</summary>
    public ActorId? CounterpartyId { get; private set; }

    public long PostedTick { get; }

    public long? SignedTick { get; private set; }

    /// <summary>The price as posted: the goods' price for a Sale, the fee for a Haul, the daily rate per unit for Storage.</summary>
    public Credits OfferedPrice { get; }

    /// <summary>The deadline as posted; a haggle may have agreed another (<see cref="DeadlineTick"/>).</summary>
    public long PostedDeadline { get; private set; }

    /// <summary>
    /// The poster's hidden walk-away price (design §3.6). Never shown and never used to set terms; AI-to-AI deals settle
    /// against it in closed form, and an AI poster haggling with the player concedes toward it. The player's own posts
    /// carry their price here: the player has no hidden number for the engine to use.
    /// </summary>
    public Credits Reservation { get; }

    /// <summary>The price signed at, once taken (null while open).</summary>
    public Credits? AgreedPrice { get; private set; }

    public Credits Price => AgreedPrice ?? OfferedPrice;

    /// <summary>The Accord tax rate on the price; zero outside the Accord, where nobody collects it.</summary>
    public decimal TaxRate { get; }

    /// <summary>The tax on the price, paid by the payer.</summary>
    public Credits Tax => Credits.Of(Price.Amount * TaxRate);

    public long DeadlineTick { get; private set; }

    public ContractStatus Status { get; private set; }

    /// <summary>What happens when it goes wrong (design §3.4): as posted, or as agreed in a haggle.</summary>
    public ContractTerms Terms { get; private set; }

    public Credits EscrowHeld { get; private set; }

    /// <summary>Where it is performed: a Sale's handover place, a Haul's destination, a store's location (design §3.5).</summary>
    public AccordReach Jurisdiction { get; }

    public string Region { get; }

    /// <summary>The bond the performer actually locked: at most its <see cref="PerformanceBond"/>, at most its cash then.</summary>
    public Credits LockedBond { get; private set; }

    /// <summary>Whether enforcement here locks escrow (Accord reach) or is reputation-only (the frontier).</summary>
    public bool UsesEscrow => Jurisdiction is AccordReach.DeepAccord or AccordReach.AccordEdge;

    public bool AccordCompliant => UsesEscrow;

    /// <summary>
    /// Whether Accord courts enforce it (deep Accord, design §3.5): a remedy the escrow can't cover is collected from the
    /// party's account. At the Accord's edge remedies stop at what escrow holds; on the frontier nobody enforces them.
    /// </summary>
    public bool Arbitrated => Jurisdiction == AccordReach.DeepAccord;

    /// <summary>Who pays: a Sale's buyer, a Haul's shipper, a store's renter. Empty while that side is open.</summary>
    public abstract ActorId? Payer { get; }

    /// <summary>Who performs: a Sale's seller, a Haul's carrier, a store's operator. Empty while that side is open.</summary>
    public abstract ActorId? Performer { get; }

    /// <summary>The locations it concerns, for who can see it (design §7).</summary>
    public abstract IReadOnlyList<LocationId> Places { get; }

    /// <summary>Who cancelled it and why, if it was cancelled.</summary>
    public Cancellation? Cancellation { get; private set; }

    /// <summary>
    /// The journal's sequence number of the latest entry about this contract (or a haul delivering it): the default cause
    /// of the next one, so its record reads as a chain of causes (design §16.2).
    /// </summary>
    public long? LastEntry { get; internal set; }

    public bool IsOpen => Status == ContractStatus.Open;

    /// <summary>Open, or signed and not yet settled.</summary>
    public bool IsLive => IsOpen || Status == ContractStatus.Active;

    public bool Involves(ActorId actor) => PostedBy == actor || CounterpartyId == actor;

    /// <summary>The other party to <paramref name="actor"/>, if there is one yet.</summary>
    public ActorId? Other(ActorId actor) => PostedBy == actor ? CounterpartyId : PostedBy;

    public PenaltyOnLateDelivery? LatePenalty => Terms.LateFee;

    public QualityWarranty? Warranty => Terms.Warranty;

    /// <summary>The bond a fully solvent performer posts: the late fee's cap (design §16.4).</summary>
    public Credits PerformanceBond => Terms.Bond;

    /// <summary>When the grace after the deadline ends: from then a signed contract not yet performed is in default.</summary>
    public long GraceEndsTick => DeadlineTick == long.MaxValue ? long.MaxValue : DeadlineTick + Terms.GraceHours;

    /// <summary>Signed, not performed, and past its grace: the payer may cancel for default (design §3.4).</summary>
    public bool InDefault(long now) => Status == ContractStatus.Active && now >= GraceEndsTick;

    /// <summary>A haggle agreed other terms than posted: the deadline and the terms rebuilt from the agreed offer. Before signing.</summary>
    internal void Amend(long deadline, ContractTerms terms)
    {
        DeadlineTick = deadline;
        Terms = terms;
    }

    internal void Sign(ActorId counterparty, Credits price, long tick)
    {
        CounterpartyId = counterparty;
        AgreedPrice = price;
        SignedTick = tick;
        Status = ContractStatus.Active;
    }

    internal void SetLockedBond(Credits bond) => LockedBond = bond;

    internal void AddEscrow(Credits amount) => EscrowHeld += amount;

    internal void ReleaseEscrow(Credits amount) => EscrowHeld -= amount;

    internal void MarkFulfilled() => Status = ContractStatus.Fulfilled;

    internal void MarkPartlyFulfilled() => Status = ContractStatus.PartlyFulfilled;

    internal void MarkBreached() => Status = ContractStatus.Breached;

    internal void MarkCancelled(Cancellation cancellation)
    {
        Cancellation = cancellation;
        Status = ContractStatus.Cancelled;
    }

    internal void MarkDefaulted() => Status = ContractStatus.Defaulted;

    internal void MarkExpired() => Status = ContractStatus.Expired;

    internal void MarkWithdrawn() => Status = ContractStatus.Withdrawn;
}

/// <summary>
/// A Sale (design §3.2): goods change hands at a named place by a deadline. Posted by the buyer ("wanted") or
/// the seller ("for sale"). When the place is the buyer's own location the Sale is "delivered" and moving the
/// goods is the seller's problem; when it is the seller's, the buyer collects.
/// </summary>
public sealed class SaleContract : Contract
{
    private LocationId[]? _places;

    public SaleContract(
        ContractId id,
        ActorId postedBy,
        long postedTick,
        SaleSide side,
        GoodType good,
        decimal quantity,
        LocationId place,
        Credits offeredPrice,
        Credits reservation,
        decimal taxRate,
        long deadlineTick,
        AccordReach jurisdiction,
        string region,
        ContractTerms terms)
        : base(id, postedBy, postedTick, offeredPrice, reservation, taxRate, deadlineTick, jurisdiction, region, terms)
    {
        Side = side;
        Good = good;
        Quantity = quantity;
        Place = place;
    }

    public override ContractType Type => ContractType.Sale;

    public SaleSide Side { get; }

    public GoodType Good { get; }

    public decimal Quantity { get; }

    /// <summary>Where the goods change hands.</summary>
    public LocationId Place { get; }

    public ActorId? Buyer => Side == SaleSide.Wanted ? PostedBy : CounterpartyId;

    public ActorId? Seller => Side == SaleSide.ForSale ? PostedBy : CounterpartyId;

    public override ActorId? Payer => Buyer;

    public override ActorId? Performer => Seller;

    public override IReadOnlyList<LocationId> Places => _places ??= new[] { Place };

    /// <summary>Where the buyer takes the goods at the place: its stock, its storage, or one of its ships collecting.</summary>
    public Holding? Receipt { get; private set; }

    /// <summary>Where the seller's goods for this sale are set aside, if they are: its stock or its storage.</summary>
    public Holding? Reserved { get; private set; }

    /// <summary>
    /// Whether the buyer collects with its own ship at the place, so the seller performs by having the goods ready: a
    /// lot it bought, or goods bought at a producer's door. Nobody waits on the seller, so it can't be late.
    /// </summary>
    public bool Collected => Receipt?.Kind == HoldingKind.Ship;

    /// <summary>Goods actually handed over (less than the quantity after a loss on the way).</summary>
    public decimal Delivered { get; private set; }

    /// <summary>For a lot posted from storage, its quality when posted: it is warranted as seen (<see cref="Simulation.Terms"/>).</summary>
    public decimal? SeenQuality { get; private set; }

    public Credits UnitPrice => Credits.Of(Quantity <= 0m ? 0m : Price.Amount / Quantity);

    /// <summary>
    /// An order at a producer's door (<see cref="Simulation.Market.BuyAtSource"/>): a wanted Sale at its place, answered on the spot
    /// at the producer's ask up to the price posted, and never on a board or in the news.
    /// </summary>
    public bool AtTheDoor { get; internal set; }

    internal void SetReceipt(Holding receipt) => Receipt = receipt;

    internal void SetReserved(Holding? reserved) => Reserved = reserved;

    internal void NoteDelivered(decimal quantity) => Delivered = quantity;

    internal void SetSeen(decimal? quality) => SeenQuality = quality;
}

/// <summary>
/// A Haul (design §3.2): move goods the carrier doesn't own, from an origin to a destination by a deadline.
/// Posted by whoever owns the goods. A haul may serve a Sale: the shipper sold the goods delivered and the
/// carrier's arrival is the handover.
/// </summary>
public sealed class HaulContract : Contract
{
    private LocationId[]? _places;

    public HaulContract(
        ContractId id,
        ActorId shipper,
        long postedTick,
        GoodType good,
        decimal quantity,
        LocationId origin,
        LocationId destination,
        Holding pickupFrom,
        ContractId? serves,
        Credits offeredFee,
        Credits reservation,
        decimal taxRate,
        long deadlineTick,
        AccordReach jurisdiction,
        string region,
        ContractTerms terms)
        : base(id, shipper, postedTick, offeredFee, reservation, taxRate, deadlineTick, jurisdiction, region, terms)
    {
        Good = good;
        Quantity = quantity;
        Origin = origin;
        Destination = destination;
        PickupFrom = pickupFrom;
        Serves = serves;
        DeclaredUnitValue = Credits.Of(Goods.Info(good).BaseValue);
    }

    public override ContractType Type => ContractType.Haul;

    public GoodType Good { get; }

    public decimal Quantity { get; }

    public LocationId Origin { get; }

    public LocationId Destination { get; }

    /// <summary>Where the shipper's goods wait at the origin: its stock there, or its storage.</summary>
    public Holding PickupFrom { get; }

    /// <summary>The Sale whose handover this haul's arrival is, if the shipper sold the goods delivered.</summary>
    public ContractId? Serves { get; }

    /// <summary>What the goods are declared at, a unit, for the carrier's liability: the Sale it serves' unit price, else their base value.</summary>
    public Credits DeclaredUnitValue { get; private set; }

    public ActorId Shipper => PostedBy;

    public ActorId? Carrier => CounterpartyId;

    public Credits Fee => Price;

    public ShipId? AssignedShip { get; private set; }

    /// <summary>Whether the carrier has loaded the goods: from then on they travel with its ship, whatever becomes of the haul.</summary>
    public bool PickedUp { get; private set; }

    /// <summary>
    /// The goods' quality when the carrier loaded them. A carrier answers only for what happens in its hold, so the
    /// warranty judges the goods it delivers against this (<see cref="InTransitQuality"/>).
    /// </summary>
    public decimal PickupQuality { get; private set; } = 1m;

    /// <summary>What the warranty judges delivered goods at: their quality as a share of what they were when loaded.</summary>
    public decimal InTransitQuality(decimal delivered) => PickupQuality <= 0m ? 1m : System.Math.Min(1m, delivered / PickupQuality);

    public override ActorId? Payer => Shipper;

    public override ActorId? Performer => Carrier;

    public override IReadOnlyList<LocationId> Places => _places ??= new[] { Origin, Destination };

    internal void Assign(ShipId ship) => AssignedShip = ship;

    internal void Declare(Credits unitValue) => DeclaredUnitValue = unitValue;

    internal void MarkPickedUp(decimal quality)
    {
        PickedUp = true;
        PickupQuality = quality;
    }
}

/// <summary>
/// A Storage contract (design §3.2): a station operator provides space for goods at its location. Rent is
/// charged per unit per day on what is held; the contract runs while the renter keeps goods there and ends
/// when they are all taken out. Station storage is a standard hold: perishables decay in it.
/// </summary>
public sealed class StorageContract : Contract
{
    private readonly List<Lot> _lots = new();
    private readonly Dictionary<GoodType, decimal> _reserved = new();

    public StorageContract(
        ContractId id,
        ActorId stationOperator,
        ActorId renter,
        long tick,
        LocationId location,
        Credits ratePerUnitDay,
        AccordReach jurisdiction,
        string region)
        : base(id, stationOperator, tick, ratePerUnitDay, ratePerUnitDay, 0m, long.MaxValue, jurisdiction, region, ContractTerms.None)
    {
        Location = location;
        LastChargedTick = tick;
        Sign(renter, ratePerUnitDay, tick);
    }

    public override ContractType Type => ContractType.Storage;

    public LocationId Location { get; }

    public ActorId Operator => PostedBy;

    public ActorId Renter => CounterpartyId!.Value;

    public Credits RatePerUnitDay => Price;

    public override ActorId? Payer => Renter;

    public override ActorId? Performer => Operator;

    public override IReadOnlyList<LocationId> Places => new[] { Location };

    /// <summary>Rent is settled up to this tick.</summary>
    public long LastChargedTick { get; private set; }

    public Credits RentPaid { get; private set; }

    public IReadOnlyList<Lot> Lots => _lots;

    public decimal QuantityHeld => _lots.Sum(l => l.Quantity);

    public decimal Held(GoodType good) => _lots.Where(l => l.Good == good).Sum(l => l.Quantity);

    public decimal ReservedOf(GoodType good) => _reserved.TryGetValue(good, out decimal q) ? q : 0m;

    /// <summary>Held and not set aside for a sale or haul.</summary>
    public decimal Available(GoodType good) => System.Math.Max(0m, Held(good) - ReservedOf(good));

    /// <summary>The goods of <paramref name="good"/> as they stand at <paramref name="tick"/>, or null.</summary>
    public Cargo? Peek(GoodType good, long tick) =>
        _lots.FirstOrDefault(l => l.Good == good)?.At(tick, 0m).Cargo;

    /// <summary>Rent owed since it was last settled, on what is held now.</summary>
    public Credits RentDue(long tick) =>
        tick <= LastChargedTick ? Credits.Zero : Credits.Of(RatePerUnitDay.Amount * QuantityHeld * (tick - LastChargedTick) / 24m);

    internal void Charged(long tick, Credits paid)
    {
        LastChargedTick = tick;
        RentPaid += paid;
    }

    internal void Reserve(GoodType good, decimal quantity) => _reserved[good] = ReservedOf(good) + quantity;

    internal void Unreserve(GoodType good, decimal quantity) => _reserved[good] = System.Math.Max(0m, ReservedOf(good) - quantity);

    internal void Deposit(Cargo cargo, long tick)
    {
        int index = _lots.FindIndex(l => l.Good == cargo.Good);
        if (index < 0)
        {
            _lots.Add(new Lot(Renter, cargo, tick));
            return;
        }

        Lot current = _lots[index].At(tick, 0m);
        _lots[index] = current with { Cargo = current.Cargo.Merged(cargo) };
    }

    /// <summary>Take out up to <paramref name="quantity"/> of a good, as it stands now: goods set aside for this, or free ones.</summary>
    internal Cargo Withdraw(GoodType good, decimal quantity, long tick, bool reserved)
    {
        int index = _lots.FindIndex(l => l.Good == good);
        if (index < 0)
        {
            return new Cargo(good, 0m, 1m);
        }

        Lot current = _lots[index].At(tick, 0m);
        decimal taken = System.Math.Min(quantity, reserved ? current.Quantity : Available(good));
        if (reserved)
        {
            Unreserve(good, taken);
        }

        if (taken >= current.Quantity)
        {
            _lots.RemoveAt(index);
        }
        else
        {
            _lots[index] = current with { Cargo = current.Cargo.LosingUnits(taken) };
        }

        return current.Cargo with { Quantity = taken };
    }

    internal void Close() => MarkFulfilled();
}
