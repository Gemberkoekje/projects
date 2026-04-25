namespace SpaceTraders.Application.Ports;

public record AgentModel(string Symbol, string? AccountId, string? HeadquartersSymbol, long Credits, string StartingFaction, int ShipCount);

public record NavModel(string Status, string SystemSymbol, string WaypointSymbol, string FlightMode, string? DestWaypointSymbol, DateTimeOffset? ArrivesAt);

public record FuelModel(int Current, int Capacity);

public record CargoModel(int Units, int Capacity);

public record NavigateActionResult(NavModel Nav, FuelModel? Fuel);

public record TradeActionResult(string AgentSymbol, long AgentCredits, CargoModel Cargo, long Revenue);

public record RefuelActionResult(long AgentCredits, FuelModel Fuel, long Cost);

public record ExtractionActionResult(string YieldSymbol, int YieldUnits, CargoModel Cargo, int CooldownSeconds);

public record PurchaseShipActionResult(AgentModel Agent, string ShipSymbol, NavModel ShipNav, FuelModel ShipFuel, long Cost);

public record ContractActionResult(string ContractId, bool IsAccepted, bool IsFulfilled, string? AgentSymbol, long? AgentCredits, CargoModel? ShipCargo);

public record ShipModel(
    string Symbol,
    string? SystemSymbol,
    string? WaypointSymbol,
    string? Status,
    string? FlightMode,
    int FuelCurrent,
    int FuelCapacity,
    DateTimeOffset? ArrivesAt,
    string? DestWaypointSymbol = null,
    int CargoCurrent = 0,
    int CargoCapacity = 0,
    DateTimeOffset LastSyncedAt = default)
{
    public bool IsInTransit => ArrivesAt.HasValue && ArrivesAt.Value > DateTimeOffset.UtcNow;
}

public record ContractModel(string Id, string FactionSymbol, string Type, bool IsAccepted, bool IsFulfilled, DateTimeOffset? Expiration, DateTimeOffset? DeadlineToAccept);

public record MarketDataModel(string WaypointSymbol, string SystemSymbol, string? TradeGoodsJson, string? ImportsJson, string? ExportsJson, string? ExchangeJson);

public record ShipyardDataModel(string WaypointSymbol, string SystemSymbol, string? ShipTypesJson);

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int Limit);

public record RegisterResult(string AgentToken, string AgentSymbol);
