namespace SpaceTraders.Application.Ports;

public interface ISpaceTradersPort
{
    Task<ServerStatusModel> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<AgentModel> GetMyAgentAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<ShipModel>> GetMyShipsAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default);

    Task<PagedResult<ContractModel>> GetMyContractsAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default);

    Task<SystemDataModel> GetSystemAsync(string systemSymbol, CancellationToken cancellationToken = default);

    Task<PagedResult<WaypointDataModel>> GetWaypointsAsync(string systemSymbol, int page = 1, int limit = 20, CancellationToken cancellationToken = default);

    /// <summary>One waypoint, as anyone can see it: its traits (once charted), and whether it is still under construction.</summary>
    Task<WaypointDataModel> GetWaypointAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken = default);

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

    /// <summary>The game's production chains: each exported good, with the goods it is made from.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetSupplyChainAsync(CancellationToken cancellationToken = default);

    Task<RegisterResult> RegisterAsync(string agentSymbol, string faction, string? email, CancellationToken cancellationToken = default);

    // Phase 1 additions
    Task<CargoModel> GetShipCargoAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<JettisonActionResult> JettisonCargoAsync(string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves cargo from one of our ships to another at the same waypoint (slice 6.18, D83): both docked or both in orbit,
    /// the receiver with room for it.
    /// </summary>
    /// <param name="shipSymbol">The ship that hands the cargo over.</param>
    /// <param name="targetShipSymbol">The ship that takes it.</param>
    /// <param name="tradeSymbol">The good.</param>
    /// <param name="units">How many units.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>The transferring ship's cargo after the transfer; the API answers with no more.</returns>
    Task<CargoModel> TransferCargoAsync(string shipSymbol, string targetShipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken = default);

    Task<NegotiateContractActionResult> NegotiateContractAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<NavModel> PatchShipNavAsync(string shipSymbol, string flightMode, CancellationToken cancellationToken = default);

    Task<SurveyActionResult> SurveyAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<ExtractionActionResult> ExtractWithSurveyAsync(string shipSymbol, SurveyModel survey, CancellationToken cancellationToken = default);

    Task<SiphonActionResult> SiphonResourcesAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<WarpActionResult> WarpShipAsync(string shipSymbol, string waypointSymbol, CancellationToken cancellationToken = default);

    /// <summary>
    /// Jumps a ship in orbit at a jump gate to <paramref name="waypointSymbol"/>, a gate the first connects to; the jump buys
    /// one ANTIMATTER at the gate's market.
    /// </summary>
    Task<JumpActionResult> JumpShipAsync(string shipSymbol, string waypointSymbol, CancellationToken cancellationToken = default);

    Task<ChartActionResult> CreateChartAsync(string shipSymbol, CancellationToken cancellationToken = default);

    /// <summary>The gates a jump gate connects to, by their waypoints.</summary>
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

    // Phase 10 additions
    Task<ConstructionSiteModel> GetConstructionSiteAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken = default);

    Task<SupplyConstructionActionResult> SupplyConstructionAsync(string systemSymbol, string waypointSymbol, string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken = default);
}
