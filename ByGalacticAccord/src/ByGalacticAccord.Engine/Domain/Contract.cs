using System.Collections.Generic;
using System.Linq;

namespace ByGalacticAccord.Engine.Domain;

/// <summary>Contract participation modes / kinds (§4.1.1). Slice 0 exercises Haul and Procure.</summary>
public enum ContractType
{
    Haul,
    Procure,
    Service,
    Insurance,
    Brokered,
    Bond,
}

/// <summary>Contract lifecycle states (§4.1.2).</summary>
public enum ContractStatus
{
    Open,
    Negotiating,
    Active,
    Fulfilled,
    Breached,
    Expired,
    Disputed,
}

/// <summary>
/// The Accord-standard contract aggregate (§4.1.1). Short-lived; in the GDD's Marten design it is
/// a live aggregation with no snapshot. Here it is a plain mutable aggregate the events advance.
/// </summary>
public sealed class Contract
{
    public Contract(
        ContractId id,
        ContractType type,
        ActorId postedBy,
        GoodType good,
        decimal quantity,
        LocationId origin,
        LocationId destination,
        Credits offeredPayment,
        Credits buyerReservationFee,
        Credits goodsCost,
        decimal taxRate,
        long deadlineTick,
        AccordReach jurisdiction,
        string region,
        IEnumerable<ContractClause> clauses)
    {
        Id = id;
        Type = type;
        PostedBy = postedBy;
        Good = good;
        Quantity = quantity;
        OriginLocationId = origin;
        DestinationLocationId = destination;
        OfferedPayment = offeredPayment;
        BuyerReservationFee = buyerReservationFee;
        GoodsCost = goodsCost;
        TaxRate = taxRate;
        DeadlineTick = deadlineTick;
        Jurisdiction = jurisdiction;
        Region = region;
        Clauses = clauses.ToList();
        Status = ContractStatus.Open;
        AccordCompliant = jurisdiction is AccordReach.DeepAccord or AccordReach.AccordEdge;
    }

    public ContractId Id { get; }

    public ContractType Type { get; }

    public ActorId PostedBy { get; }

    public ActorId? CounterpartyId { get; private set; }

    public GoodType Good { get; }

    public decimal Quantity { get; }

    public LocationId OriginLocationId { get; }

    public LocationId DestinationLocationId { get; }

    public Credits OfferedPayment { get; }

    /// <summary>The poster's hidden walk-away fee (§4.1.6). Never shown to a player — only soft tells leak it.</summary>
    public Credits BuyerReservationFee { get; }

    /// <summary>Haul fee agreed after negotiation (null until concluded).</summary>
    public Credits? AgreedPayment { get; private set; }

    /// <summary>Price paid to the origin producer for the goods (fixed at posting in slice 0).</summary>
    public Credits GoodsCost { get; }

    /// <summary>Government tax rate snapshot applied to the haul fee on settlement.</summary>
    public decimal TaxRate { get; }

    public long DeadlineTick { get; private set; }

    public ContractStatus Status { get; private set; }

    public IReadOnlyList<ContractClause> Clauses { get; }

    public Credits EscrowHeld { get; private set; }

    public bool AccordCompliant { get; }

    public AccordReach Jurisdiction { get; }

    public string Region { get; }

    public ShipId? AssignedShip { get; private set; }

    /// <summary>Whether enforcement here locks escrow (Accord reach) or is reputation-only (frontier).</summary>
    public bool UsesEscrow => Jurisdiction is AccordReach.DeepAccord or AccordReach.AccordEdge;

    public Credits Fee => AgreedPayment ?? OfferedPayment;

    public PenaltyOnLateDelivery? LatePenalty =>
        Clauses.OfType<PenaltyOnLateDelivery>().FirstOrDefault();

    public QualityWarranty? Warranty =>
        Clauses.OfType<QualityWarranty>().FirstOrDefault();

    /// <summary>The worst-case late penalty the clause allows — the bond a fully-solvent pilot posts.</summary>
    public Credits PerformanceBond => LatePenalty?.MaxPenalty ?? Credits.Zero;

    /// <summary>
    /// The bond actually locked at acceptance: min(<see cref="PerformanceBond"/>, the pilot's cash). A
    /// near-broke pilot can still take a job (locking less), which is what lets the AI economy recover
    /// from a bad run without the §7.8 bankruptcy machinery (deferred). Penalties cap at this amount.
    /// </summary>
    public Credits LockedBond { get; private set; }

    public void BeginNegotiation() => Status = ContractStatus.Negotiating;

    public void Activate(ActorId counterparty, ShipId ship, Credits agreedPayment)
    {
        CounterpartyId = counterparty;
        AssignedShip = ship;
        AgreedPayment = agreedPayment;
        Status = ContractStatus.Active;
    }

    public void SetLockedBond(Credits bond) => LockedBond = bond;

    public void AddEscrow(Credits amount) => EscrowHeld += amount;

    public void ReleaseEscrow(Credits amount) => EscrowHeld -= amount;

    public void MarkFulfilled() => Status = ContractStatus.Fulfilled;

    public void MarkBreached() => Status = ContractStatus.Breached;

    public void MarkExpired() => Status = ContractStatus.Expired;

    public void ExtendDeadline(long newDeadline) => DeadlineTick = newDeadline;
}
