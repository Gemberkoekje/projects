using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.ValueObjects;

namespace SpaceTraders.Domain.Events;

public sealed record AgentCreditsChangedEvent
{
    public required long OldCredits { get; init; }

    public required long NewCredits { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public AgentCreditsChangedEvent(long OldCredits, long NewCredits)
    {
        this.OldCredits = OldCredits;
        this.NewCredits = NewCredits;
    }
}

public sealed record ShipAssignmentCompletedEvent
{
    public required string ShipSymbol { get; init; }

    public required AssignmentType CompletedType { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipAssignmentCompletedEvent(string ShipSymbol, AssignmentType CompletedType)
    {
        this.ShipSymbol = ShipSymbol;
        this.CompletedType = CompletedType;
    }
}

public sealed record ShipCargoSoldEvent
{
    public required string ShipSymbol { get; init; }

    public required TradeSymbol Good { get; init; }

    public required int Units { get; init; }

    public required long Revenue { get; init; }

    public required long NewAgentCredits { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipCargoSoldEvent(string ShipSymbol, TradeSymbol Good, int Units, long Revenue, long NewAgentCredits)
    {
        this.ShipSymbol = ShipSymbol;
        this.Good = Good;
        this.Units = Units;
        this.Revenue = Revenue;
        this.NewAgentCredits = NewAgentCredits;
    }
}

public sealed record ShipArrivedAtWaypointEvent
{
    public required string ShipSymbol { get; init; }

    public required WaypointSymbol Waypoint { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipArrivedAtWaypointEvent(string ShipSymbol, WaypointSymbol Waypoint)
    {
        this.ShipSymbol = ShipSymbol;
        this.Waypoint = Waypoint;
    }
}

public sealed record ShipBecameIdleEvent
{
    public required string ShipSymbol { get; init; }

    public required string Reason { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipBecameIdleEvent(string ShipSymbol, string Reason)
    {
        this.ShipSymbol = ShipSymbol;
        this.Reason = Reason;
    }
}

public sealed record ShipAssignedEvent
{
    public required string ShipSymbol { get; init; }

    public required string AssignmentType { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipAssignedEvent(string ShipSymbol, string AssignmentType)
    {
        this.ShipSymbol = ShipSymbol;
        this.AssignmentType = AssignmentType;
    }
}

public sealed record ShipFuelLowEvent
{
    public required string ShipSymbol { get; init; }

    public required int CurrentFuel { get; init; }

    public required int Capacity { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipFuelLowEvent(string ShipSymbol, int CurrentFuel, int Capacity)
    {
        this.ShipSymbol = ShipSymbol;
        this.CurrentFuel = CurrentFuel;
        this.Capacity = Capacity;
    }
}

public sealed record ContractAcceptedEvent
{
    public required string ContractId { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ContractAcceptedEvent(string ContractId)
    {
        this.ContractId = ContractId;
    }
}

public sealed record ContractNegotiatedEvent
{
    public required string ContractId { get; init; }

    public required string ShipSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ContractNegotiatedEvent(string ContractId, string ShipSymbol)
    {
        this.ContractId = ContractId;
        this.ShipSymbol = ShipSymbol;
    }
}

public sealed record ContractDeliveryRecordedEvent
{
    public required string ContractId { get; init; }

    public required string ShipSymbol { get; init; }

    public required string TradeSymbol { get; init; }

    public required int UnitsDelivered { get; init; }

    public required string DestinationWaypoint { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ContractDeliveryRecordedEvent(string ContractId, string ShipSymbol, string TradeSymbol, int UnitsDelivered, string DestinationWaypoint)
    {
        this.ContractId = ContractId;
        this.ShipSymbol = ShipSymbol;
        this.TradeSymbol = TradeSymbol;
        this.UnitsDelivered = UnitsDelivered;
        this.DestinationWaypoint = DestinationWaypoint;
    }
}

public sealed record ContractDeadlineApproachingEvent
{
    public required string ContractId { get; init; }

    public required TimeSpan Remaining { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ContractDeadlineApproachingEvent(string ContractId, TimeSpan Remaining)
    {
        this.ContractId = ContractId;
        this.Remaining = Remaining;
    }
}

public sealed record ContractFulfilledEvent
{
    public required string ContractId { get; init; }

    public required long Payment { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ContractFulfilledEvent(string ContractId, long Payment)
    {
        this.ContractId = ContractId;
        this.Payment = Payment;
    }
}

public sealed record MarketDataRefreshedEvent
{
    public required WaypointSymbol Waypoint { get; init; }

    public string? TradeGoodsJson { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MarketDataRefreshedEvent(WaypointSymbol Waypoint, string? TradeGoodsJson = null)
    {
        this.Waypoint = Waypoint;
        this.TradeGoodsJson = TradeGoodsJson;
    }
}

public sealed record ShipyardDataRefreshedEvent
{
    public required WaypointSymbol Waypoint { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipyardDataRefreshedEvent(WaypointSymbol Waypoint)
    {
        this.Waypoint = Waypoint;
    }
}

public sealed record NewShipPurchasedEvent
{
    public required string ShipSymbol { get; init; }

    public required ShipType Type { get; init; }

    public required long CostPaid { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public NewShipPurchasedEvent(string ShipSymbol, ShipType Type, long CostPaid)
    {
        this.ShipSymbol = ShipSymbol;
        this.Type = Type;
        this.CostPaid = CostPaid;
    }
}

public sealed record ApiUnavailableEvent
{
    public required DateTimeOffset DetectedAt { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ApiUnavailableEvent(DateTimeOffset DetectedAt)
    {
        this.DetectedAt = DetectedAt;
    }
}

public sealed record ApiAvailableEvent
{
    public required DateTimeOffset RestoredAt { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ApiAvailableEvent(DateTimeOffset RestoredAt)
    {
        this.RestoredAt = RestoredAt;
    }
}

public sealed record ServerResetWarningEvent
{
    public required DateTimeOffset NextResetAt { get; init; }

    public required TimeSpan Remaining { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ServerResetWarningEvent(DateTimeOffset NextResetAt, TimeSpan Remaining)
    {
        this.NextResetAt = NextResetAt;
        this.Remaining = Remaining;
    }
}

public sealed record AutomationPausedForServerResetEvent
{
    public required DateTimeOffset NextResetAt { get; init; }

    public required TimeSpan Remaining { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public AutomationPausedForServerResetEvent(DateTimeOffset NextResetAt, TimeSpan Remaining)
    {
        this.NextResetAt = NextResetAt;
        this.Remaining = Remaining;
    }
}

public sealed record AutomationResumedAfterServerResetEvent
{
    public required DateTimeOffset ResumedAt { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public AutomationResumedAfterServerResetEvent(DateTimeOffset ResumedAt)
    {
        this.ResumedAt = ResumedAt;
    }
}

public sealed record TokenResetMismatchDetectedEvent
{
    public required string Source { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public TokenResetMismatchDetectedEvent(string Source)
    {
        this.Source = Source;
    }
}

public sealed record CacheDivergenceDetectedEvent
{
    public required int MismatchCount { get; init; }

    public required string Summary { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public CacheDivergenceDetectedEvent(int MismatchCount, string Summary)
    {
        this.MismatchCount = MismatchCount;
        this.Summary = Summary;
    }
}

public sealed record CargoPurchasedEvent
{
    public required string ShipSymbol { get; init; }

    public required TradeSymbol Good { get; init; }

    public required int Units { get; init; }

    public required long Cost { get; init; }

    public required long NewAgentCredits { get; init; }

    public required string WaypointSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public CargoPurchasedEvent(string ShipSymbol, TradeSymbol Good, int Units, long Cost, long NewAgentCredits, string WaypointSymbol)
    {
        this.ShipSymbol = ShipSymbol;
        this.Good = Good;
        this.Units = Units;
        this.Cost = Cost;
        this.NewAgentCredits = NewAgentCredits;
        this.WaypointSymbol = WaypointSymbol;
    }
}

public sealed record ShipRefueledEvent
{
    public required string ShipSymbol { get; init; }

    public required long Cost { get; init; }

    public required long NewAgentCredits { get; init; }

    public required string WaypointSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipRefueledEvent(string ShipSymbol, long Cost, long NewAgentCredits, string WaypointSymbol)
    {
        this.ShipSymbol = ShipSymbol;
        this.Cost = Cost;
        this.NewAgentCredits = NewAgentCredits;
        this.WaypointSymbol = WaypointSymbol;
    }
}

public sealed record ShipRepairedEvent
{
    public required string ShipSymbol { get; init; }

    public required long Cost { get; init; }

    public required long NewAgentCredits { get; init; }

    public required string WaypointSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ShipRepairedEvent(string ShipSymbol, long Cost, long NewAgentCredits, string WaypointSymbol)
    {
        this.ShipSymbol = ShipSymbol;
        this.Cost = Cost;
        this.NewAgentCredits = NewAgentCredits;
        this.WaypointSymbol = WaypointSymbol;
    }
}

public sealed record MountInstalledEvent
{
    public required string ShipSymbol { get; init; }

    public required string MountSymbol { get; init; }

    public required long Cost { get; init; }

    public required long NewAgentCredits { get; init; }

    public required string WaypointSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MountInstalledEvent(string ShipSymbol, string MountSymbol, long Cost, long NewAgentCredits, string WaypointSymbol)
    {
        this.ShipSymbol = ShipSymbol;
        this.MountSymbol = MountSymbol;
        this.Cost = Cost;
        this.NewAgentCredits = NewAgentCredits;
        this.WaypointSymbol = WaypointSymbol;
    }
}

public sealed record ModuleInstalledEvent
{
    public required string ShipSymbol { get; init; }

    public required string ModuleSymbol { get; init; }

    public required long Cost { get; init; }

    public required long NewAgentCredits { get; init; }

    public required string WaypointSymbol { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public ModuleInstalledEvent(string ShipSymbol, string ModuleSymbol, long Cost, long NewAgentCredits, string WaypointSymbol)
    {
        this.ShipSymbol = ShipSymbol;
        this.ModuleSymbol = ModuleSymbol;
        this.Cost = Cost;
        this.NewAgentCredits = NewAgentCredits;
        this.WaypointSymbol = WaypointSymbol;
    }
}
