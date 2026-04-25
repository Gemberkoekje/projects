namespace SpaceTraders.Application.DTOs;

public sealed record AgentDto(
    string Symbol,
    long Credits,
    string StartingFaction,
    int ShipCount,
    string? HeadquartersSymbol);

public sealed record ShipDto(
    string Symbol,
    string? SystemSymbol,
    string? WaypointSymbol,
    string? Status,
    string? FlightMode,
    int FuelCurrent,
    int FuelCapacity,
    int CargoCurrent,
    int CargoCapacity,
    DateTimeOffset? ArrivesAt,
    bool IsInTransit,
    DateTimeOffset LastSyncedAt);

public sealed record ContractDto(
    string Id,
    string FactionSymbol,
    string Type,
    bool IsAccepted,
    bool IsFulfilled,
    DateTimeOffset? Expiration,
    DateTimeOffset? DeadlineToAccept);

public sealed record TradeOpportunityDto(
    int Id,
    string TradeSymbol,
    string BuyWaypoint,
    string SellWaypoint,
    int BuyPrice,
    int SellPrice,
    int ProfitPerUnit,
    int DistanceJumps,
    decimal ProfitPerJump,
    DateTimeOffset ComputedAt);

public sealed record SettingDto(
    string Key,
    string Value,
    string Type,
    string Description);

public sealed record ActivityLogDto(
    long Id,
    DateTimeOffset Timestamp,
    string ShipSymbol,
    string EventType,
    string Message,
    string? JsonDetails);

public sealed record RateLimitStatusDto(
    int Remaining,
    int Limit,
    int BurstRemaining,
    int BurstLimit,
    DateTimeOffset ResetAt,
    string? LimitType,
    int TotalRequests,
    int ThrottledCount);

public record ShipAssignmentDto(
    string ShipSymbol,
    string AssignmentType,
    string? OriginWaypoint,
    string? DestWaypoint,
    string? CargoSymbol,
    string? ContractId,
    int StepIndex,
    DateTimeOffset AssignedAt,
    DateTimeOffset? CompletedAt);
