using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.ValueObjects;

namespace SpaceTraders.Domain.Events;

public sealed record AgentCreditsChangedEvent(long OldCredits, long NewCredits);

public sealed record ShipAssignmentCompletedEvent(string ShipSymbol, AssignmentType CompletedType);

public sealed record ShipCargoSoldEvent(string ShipSymbol, TradeSymbol Good, int Units, long Revenue, long NewAgentCredits);

public sealed record ShipArrivedAtWaypointEvent(string ShipSymbol, WaypointSymbol Waypoint);

public sealed record ShipFuelLowEvent(string ShipSymbol, int CurrentFuel, int Capacity);

public sealed record ContractAcceptedEvent(string ContractId);

public sealed record ContractDeadlineApproachingEvent(string ContractId, TimeSpan Remaining);

public sealed record ContractFulfilledEvent(string ContractId, long Payment);

public sealed record MarketDataRefreshedEvent(WaypointSymbol Waypoint);

public sealed record ShipyardDataRefreshedEvent(WaypointSymbol Waypoint);

public sealed record NewShipPurchasedEvent(string ShipSymbol, ShipType Type, long CostPaid);

public sealed record ApiUnavailableEvent(DateTimeOffset DetectedAt);

public sealed record ApiAvailableEvent(DateTimeOffset RestoredAt);
