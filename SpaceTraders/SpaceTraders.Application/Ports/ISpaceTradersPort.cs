namespace SpaceTraders.Application.Ports;

public interface ISpaceTradersPort
{
    Task<AgentModel> GetMyAgentAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<ShipModel>> GetMyShipsAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default);

    Task<PagedResult<ContractModel>> GetMyContractsAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default);

    Task<SystemDataModel> GetSystemAsync(string systemSymbol, CancellationToken cancellationToken = default);

    Task<PagedResult<WaypointDataModel>> GetWaypointsAsync(string systemSymbol, int page = 1, int limit = 20, CancellationToken cancellationToken = default);

    Task<NavigateActionResult> NavigateShipAsync(string shipSymbol, string waypointSymbol, CancellationToken cancellationToken = default);

    Task<NavModel> DockShipAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<NavModel> OrbitShipAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<TradeActionResult> SellCargoAsync(string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken = default);

    Task<TradeActionResult> BuyCargoAsync(string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken = default);

    Task<RefuelActionResult> RefuelShipAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<ExtractionActionResult> ExtractResourcesAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<PurchaseShipActionResult> PurchaseShipAsync(string shipType, string waypointSymbol, CancellationToken cancellationToken = default);

    Task<ContractActionResult> AcceptContractAsync(string contractId, CancellationToken cancellationToken = default);

    Task<ContractActionResult> DeliverContractAsync(string contractId, string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken = default);

    Task<ContractActionResult> FulfillContractAsync(string contractId, CancellationToken cancellationToken = default);

    Task<MarketDataModel> GetMarketAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken = default);

    Task<ShipyardDataModel> GetShipyardAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken = default);

    Task<RegisterResult> RegisterAsync(string agentSymbol, string faction, string? email, CancellationToken cancellationToken = default);
}
