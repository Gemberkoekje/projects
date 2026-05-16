using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Accounts;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Agents;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Common;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Contracts;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Factions;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Markets;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Shipyards;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Status;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Systems;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;

public interface ISpaceTradersApiClient
{
    Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<PagedApiResponse<Faction>> GetFactionsAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default);

    Task<Faction> GetFactionAsync(string factionSymbol, CancellationToken cancellationToken = default);

    Task<PagedApiResponse<PublicAgent>> GetAgentsAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default);

    Task<PublicAgent> GetAgentAsync(string agentSymbol, CancellationToken cancellationToken = default);

    Task<PagedApiResponse<SystemInfo>> GetSystemsAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default);

    Task<SystemInfo> GetSystemAsync(string systemSymbol, CancellationToken cancellationToken = default);

    Task<PagedApiResponse<Waypoint>> GetWaypointsAsync(string systemSymbol, int page = 1, int limit = 20, CancellationToken cancellationToken = default);

    Task<Waypoint> GetWaypointAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken = default);

    Task<RegisterResponseData> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    Task<Agent> GetMyAgentAsync(CancellationToken cancellationToken = default);

    Task<PagedApiResponse<Ship>> GetMyShipsAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default);

    Task<Ship> GetMyShipAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<PagedApiResponse<Contract>> GetMyContractsAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default);

    // Fleet actions
    Task<NavigateResult> NavigateShipAsync(string shipSymbol, string waypointSymbol, CancellationToken cancellationToken = default);

    Task<ShipNavResult> DockShipAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<ShipNavResult> OrbitShipAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<ExtractResult> ExtractResourcesAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<SellCargoResult> SellCargoAsync(string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken = default);

    Task<BuyCargoResult> BuyCargoAsync(string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken = default);

    Task<RefuelResult> RefuelShipAsync(string shipSymbol, bool fromCargo = false, CancellationToken cancellationToken = default);

    Task<PurchaseShipResult> PurchaseShipAsync(string shipType, string waypointSymbol, CancellationToken cancellationToken = default);

    Task<AcceptContractResult> AcceptContractAsync(string contractId, CancellationToken cancellationToken = default);

    Task<DeliverContractResult> DeliverContractAsync(string contractId, DeliverContractRequest request, CancellationToken cancellationToken = default);

    Task<FulfillContractResult> FulfillContractAsync(string contractId, CancellationToken cancellationToken = default);

    Task<Market> GetMarketAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken = default);

    Task<Shipyard> GetShipyardAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken = default);

    // Phase 1 additions
    Task<ShipCargo> GetShipCargoAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<JettisonResult> JettisonCargoAsync(string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken = default);

    Task<NegotiateContractResult> NegotiateContractAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<PatchShipNavResult> PatchShipNavAsync(string shipSymbol, string flightMode, CancellationToken cancellationToken = default);

    Task<SurveyResult> SurveyAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<ExtractResult> ExtractWithSurveyAsync(string shipSymbol, Survey survey, CancellationToken cancellationToken = default);

    Task<SiphonResult> SiphonResourcesAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<WarpResult> WarpShipAsync(string shipSymbol, string waypointSymbol, CancellationToken cancellationToken = default);

    Task<JumpResult> JumpShipAsync(string shipSymbol, string systemSymbol, CancellationToken cancellationToken = default);

    Task<ChartResult> CreateChartAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<JumpGate> GetJumpGateAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken = default);

    // Phase 7 additions
    Task<RepairQuoteResult> GetRepairQuoteAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<RepairResult> RepairShipAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<ScrapQuoteResult> GetScrapQuoteAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<ScrapResult> ScrapShipAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task<InstallMountResult> InstallMountAsync(string shipSymbol, string mountSymbol, CancellationToken cancellationToken = default);

    Task<RemoveMountResult> RemoveMountAsync(string shipSymbol, string mountSymbol, CancellationToken cancellationToken = default);

    Task<InstallModuleResult> InstallModuleAsync(string shipSymbol, string moduleSymbol, CancellationToken cancellationToken = default);

    Task<RemoveModuleResult> RemoveModuleAsync(string shipSymbol, string moduleSymbol, CancellationToken cancellationToken = default);

    // Phase 10 additions
    Task<ConstructionSite> GetConstructionSiteAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken = default);

    Task<SupplyConstructionData> SupplyConstructionAsync(string systemSymbol, string waypointSymbol, SupplyConstructionRequest request, CancellationToken cancellationToken = default);
}
