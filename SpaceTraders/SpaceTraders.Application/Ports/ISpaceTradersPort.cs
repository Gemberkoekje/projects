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

    Task<RefuelActionResult> RefuelShipAsync(string shipSymbol, bool fromCargo = false, CancellationToken cancellationToken = default);

    Task<ExtractionActionResult> ExtractResourcesAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<PurchaseShipActionResult> PurchaseShipAsync(string shipType, string waypointSymbol, CancellationToken cancellationToken = default);

    Task<ContractActionResult> AcceptContractAsync(string contractId, CancellationToken cancellationToken = default);

    Task<ContractActionResult> DeliverContractAsync(string contractId, string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken = default);

    Task<ContractActionResult> FulfillContractAsync(string contractId, CancellationToken cancellationToken = default);

    Task<MarketDataModel> GetMarketAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken = default);

    Task<ShipyardDataModel> GetShipyardAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken = default);

    Task<RegisterResult> RegisterAsync(string agentSymbol, string faction, string? email, CancellationToken cancellationToken = default);

    // Phase 1 additions
    Task<CargoModel> GetShipCargoAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<JettisonActionResult> JettisonCargoAsync(string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken = default);

    Task<NegotiateContractActionResult> NegotiateContractAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<NavModel> PatchShipNavAsync(string shipSymbol, string flightMode, CancellationToken cancellationToken = default);

    Task<SurveyActionResult> SurveyAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<ExtractionActionResult> ExtractWithSurveyAsync(string shipSymbol, SurveyModel survey, CancellationToken cancellationToken = default);

    Task<SiphonActionResult> SiphonResourcesAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<WarpActionResult> WarpShipAsync(string shipSymbol, string waypointSymbol, CancellationToken cancellationToken = default);

    Task<JumpActionResult> JumpShipAsync(string shipSymbol, string systemSymbol, CancellationToken cancellationToken = default);

    Task<ChartActionResult> CreateChartAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<JumpGateConnectionModel> GetJumpGateConnectionsAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken = default);

    // Phase 7 additions
    Task<ShipRepairQuoteModel> GetRepairQuoteAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<ShipRepairActionResult> RepairShipAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<ShipScrapQuoteModel> GetScrapQuoteAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<ShipScrapActionResult> ScrapShipAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<ShipOutfitActionResult> InstallMountAsync(string shipSymbol, string mountSymbol, CancellationToken cancellationToken = default);

    Task<ShipOutfitActionResult> RemoveMountAsync(string shipSymbol, string mountSymbol, CancellationToken cancellationToken = default);

    Task<ShipOutfitActionResult> InstallModuleAsync(string shipSymbol, string moduleSymbol, CancellationToken cancellationToken = default);

    Task<ShipOutfitActionResult> RemoveModuleAsync(string shipSymbol, string moduleSymbol, CancellationToken cancellationToken = default);
}
