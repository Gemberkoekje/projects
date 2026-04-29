using System.Text.Json;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Accounts;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Contracts;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Adapters;

public sealed class SpaceTradersPortAdapter(ISpaceTradersApiClient client) : ISpaceTradersPort
{
    public async Task<AgentModel> GetMyAgentAsync(CancellationToken cancellationToken = default)
    {
        var agent = await client.GetMyAgentAsync(cancellationToken);
        return MapAgent(agent);
    }

    public async Task<PagedResult<ShipModel>> GetMyShipsAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default)
    {
        var response = await client.GetMyShipsAsync(page, limit, cancellationToken);
        return new PagedResult<ShipModel>(
            response.Data.Select(MapShip).ToList(),
            response.Meta.Total,
            response.Meta.Page,
            response.Meta.Limit);
    }

    public async Task<PagedResult<ContractModel>> GetMyContractsAsync(int page = 1, int limit = 20, CancellationToken cancellationToken = default)
    {
        var response = await client.GetMyContractsAsync(page, limit, cancellationToken);
        return new PagedResult<ContractModel>(
            response.Data.Select(MapContract).ToList(),
            response.Meta.Total,
            response.Meta.Page,
            response.Meta.Limit);
    }

    public async Task<SystemDataModel> GetSystemAsync(string systemSymbol, CancellationToken cancellationToken = default)
    {
        var system = await client.GetSystemAsync(systemSymbol, cancellationToken);
        return new SystemDataModel(system.Symbol, system.SectorSymbol, system.Type, system.X, system.Y);
    }

    public async Task<PagedResult<WaypointDataModel>> GetWaypointsAsync(string systemSymbol, int page = 1, int limit = 20, CancellationToken cancellationToken = default)
    {
        var response = await client.GetWaypointsAsync(systemSymbol, page, limit, cancellationToken);
        return new PagedResult<WaypointDataModel>(
            response.Data.Select(MapWaypoint).ToList(),
            response.Meta.Total,
            response.Meta.Page,
            response.Meta.Limit);
    }

    public async Task<NavigateActionResult> NavigateShipAsync(string shipSymbol, string waypointSymbol, CancellationToken cancellationToken = default)
    {
        var result = await client.NavigateShipAsync(shipSymbol, waypointSymbol, cancellationToken);
        return new NavigateActionResult(MapNav(result.Nav), result.Fuel is not null ? MapFuel(result.Fuel) : null);
    }

    public async Task<NavModel> DockShipAsync(string shipSymbol, CancellationToken cancellationToken = default)
    {
        var result = await client.DockShipAsync(shipSymbol, cancellationToken);
        return MapNav(result.Nav);
    }

    public async Task<NavModel> OrbitShipAsync(string shipSymbol, CancellationToken cancellationToken = default)
    {
        var result = await client.OrbitShipAsync(shipSymbol, cancellationToken);
        return MapNav(result.Nav);
    }

    public async Task<TradeActionResult> SellCargoAsync(string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken = default)
    {
        var result = await client.SellCargoAsync(shipSymbol, tradeSymbol, units, cancellationToken);
        return new TradeActionResult(result.Agent.Symbol, result.Agent.Credits, MapCargo(result.Cargo), result.Transaction.TotalPrice);
    }

    public async Task<TradeActionResult> BuyCargoAsync(string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken = default)
    {
        var result = await client.BuyCargoAsync(shipSymbol, tradeSymbol, units, cancellationToken);
        return new TradeActionResult(result.Agent.Symbol, result.Agent.Credits, MapCargo(result.Cargo), result.Transaction.TotalPrice);
    }

    public async Task<RefuelActionResult> RefuelShipAsync(string shipSymbol, CancellationToken cancellationToken = default)
    {
        var result = await client.RefuelShipAsync(shipSymbol, cancellationToken);
        return new RefuelActionResult(result.Agent.Credits, MapFuel(result.Fuel), result.Transaction.TotalPrice);
    }

    public async Task<ExtractionActionResult> ExtractResourcesAsync(string shipSymbol, CancellationToken cancellationToken = default)
    {
        var result = await client.ExtractResourcesAsync(shipSymbol, cancellationToken);
        return new ExtractionActionResult(
            result.Extraction.Yield.Symbol,
            result.Extraction.Yield.Units,
            MapCargo(result.Cargo),
            result.Cooldown.TotalSeconds);
    }

    public async Task<PurchaseShipActionResult> PurchaseShipAsync(string shipType, string waypointSymbol, CancellationToken cancellationToken = default)
    {
        var result = await client.PurchaseShipAsync(shipType, waypointSymbol, cancellationToken);
        return new PurchaseShipActionResult(
            MapAgent(result.Agent),
            result.Ship.Symbol,
            MapNav(result.Ship.Nav!),
            MapFuel(result.Ship.Fuel!),
            result.Transaction.Price);
    }

    public async Task<ContractActionResult> AcceptContractAsync(string contractId, CancellationToken cancellationToken = default)
    {
        var result = await client.AcceptContractAsync(contractId, cancellationToken);
        return new ContractActionResult(
            result.Contract.Id,
            result.Contract.FactionSymbol,
            result.Contract.Type,
            result.Contract.Accepted,
            result.Contract.Fulfilled,
            result.Contract.Expiration,
            result.Contract.DeadlineToAccept,
            result.Contract.Terms?.Deadline,
            MapDeliverables(result.Contract),
            result.Agent.Symbol,
            result.Agent.Credits,
            null);
    }

    public async Task<ContractActionResult> DeliverContractAsync(string contractId, string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken = default)
    {
        var result = await client.DeliverContractAsync(
            contractId,
            new DeliverContractRequest
            {
                ShipSymbol = shipSymbol,
                TradeSymbol = tradeSymbol,
                Units = units,
            },
            cancellationToken);
        return new ContractActionResult(
            result.Contract.Id,
            result.Contract.FactionSymbol,
            result.Contract.Type,
            result.Contract.Accepted,
            result.Contract.Fulfilled,
            result.Contract.Expiration,
            result.Contract.DeadlineToAccept,
            result.Contract.Terms?.Deadline,
            MapDeliverables(result.Contract),
            null,
            null,
            MapCargo(result.Cargo));
    }

    public async Task<ContractActionResult> FulfillContractAsync(string contractId, CancellationToken cancellationToken = default)
    {
        var result = await client.FulfillContractAsync(contractId, cancellationToken);
        return new ContractActionResult(
            result.Contract.Id,
            result.Contract.FactionSymbol,
            result.Contract.Type,
            result.Contract.Accepted,
            result.Contract.Fulfilled,
            result.Contract.Expiration,
            result.Contract.DeadlineToAccept,
            result.Contract.Terms?.Deadline,
            MapDeliverables(result.Contract),
            result.Agent.Symbol,
            result.Agent.Credits,
            null);
    }

    public async Task<MarketDataModel> GetMarketAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken = default)
    {
        var market = await client.GetMarketAsync(systemSymbol, waypointSymbol, cancellationToken);
        return new MarketDataModel(
            market.Symbol,
            systemSymbol,
            market.TradeGoods is not null ? JsonSerializer.Serialize(market.TradeGoods) : null,
            market.Imports is not null ? JsonSerializer.Serialize(market.Imports) : null,
            market.Exports is not null ? JsonSerializer.Serialize(market.Exports) : null,
            market.Exchange is not null ? JsonSerializer.Serialize(market.Exchange) : null);
    }

    public async Task<ShipyardDataModel> GetShipyardAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken = default)
    {
        var shipyard = await client.GetShipyardAsync(systemSymbol, waypointSymbol, cancellationToken);
        return new ShipyardDataModel(
            shipyard.Symbol,
            systemSymbol,
            shipyard.ShipTypes is not null ? JsonSerializer.Serialize(shipyard.ShipTypes) : null,
            shipyard.Ships is not null ? JsonSerializer.Serialize(shipyard.Ships) : null);
    }

    public async Task<RegisterResult> RegisterAsync(string agentSymbol, string faction, string? email, CancellationToken cancellationToken = default)
    {
        var result = await client.RegisterAsync(
            new RegisterRequest
            {
                Symbol = agentSymbol,
                Faction = faction,
            },
            cancellationToken);
        return new RegisterResult(result.Token, result.Agent.Symbol);
    }

    public async Task<CargoModel> GetShipCargoAsync(string shipSymbol, CancellationToken cancellationToken = default)
    {
        var cargo = await client.GetShipCargoAsync(shipSymbol, cancellationToken);
        return MapCargo(cargo);
    }

    public async Task<JettisonActionResult> JettisonCargoAsync(string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken = default)
    {
        var result = await client.JettisonCargoAsync(shipSymbol, tradeSymbol, units, cancellationToken);
        return new JettisonActionResult(MapCargo(result.Cargo));
    }

    public async Task<NegotiateContractActionResult> NegotiateContractAsync(string shipSymbol, CancellationToken cancellationToken = default)
    {
        var result = await client.NegotiateContractAsync(shipSymbol, cancellationToken);
        return new NegotiateContractActionResult(MapContract(result.Contract));
    }

    public async Task<NavModel> PatchShipNavAsync(string shipSymbol, string flightMode, CancellationToken cancellationToken = default)
    {
        var result = await client.PatchShipNavAsync(shipSymbol, flightMode, cancellationToken);
        return MapNav(result.Nav);
    }

    public async Task<SurveyActionResult> SurveyAsync(string shipSymbol, CancellationToken cancellationToken = default)
    {
        var result = await client.SurveyAsync(shipSymbol, cancellationToken);
        var surveys = result.Surveys.Select(s => new SurveyModel(
            s.Signature,
            s.Symbol,
            s.Deposits.Select(d => new SurveyDepositModel(d.Symbol)).ToList(),
            s.Expiration,
            s.Size)).ToList();
        return new SurveyActionResult(surveys, result.Cooldown.TotalSeconds);
    }

    public async Task<ExtractionActionResult> ExtractWithSurveyAsync(string shipSymbol, SurveyModel survey, CancellationToken cancellationToken = default)
    {
        var apiSurvey = new Survey
        {
            Signature = survey.Signature,
            Symbol = survey.WaypointSymbol,
            Deposits = survey.Deposits.Select(d => new SurveyDeposit { Symbol = d.Symbol }).ToList(),
            Expiration = survey.Expiration,
            Size = survey.Size,
        };
        var result = await client.ExtractWithSurveyAsync(shipSymbol, apiSurvey, cancellationToken);
        return new ExtractionActionResult(
            result.Extraction.Yield.Symbol,
            result.Extraction.Yield.Units,
            MapCargo(result.Cargo),
            result.Cooldown.TotalSeconds);
    }

    public async Task<SiphonActionResult> SiphonResourcesAsync(string shipSymbol, CancellationToken cancellationToken = default)
    {
        var result = await client.SiphonResourcesAsync(shipSymbol, cancellationToken);
        return new SiphonActionResult(
            result.Siphon.Yield.Symbol,
            result.Siphon.Yield.Units,
            MapCargo(result.Cargo),
            result.Cooldown.TotalSeconds);
    }

    public async Task<WarpActionResult> WarpShipAsync(string shipSymbol, string waypointSymbol, CancellationToken cancellationToken = default)
    {
        var result = await client.WarpShipAsync(shipSymbol, waypointSymbol, cancellationToken);
        return new WarpActionResult(MapNav(result.Nav), MapFuel(result.Fuel));
    }

    public async Task<JumpActionResult> JumpShipAsync(string shipSymbol, string systemSymbol, CancellationToken cancellationToken = default)
    {
        var result = await client.JumpShipAsync(shipSymbol, systemSymbol, cancellationToken);
        return new JumpActionResult(MapNav(result.Nav), result.Cooldown.TotalSeconds);
    }

    public async Task<ChartActionResult> CreateChartAsync(string shipSymbol, CancellationToken cancellationToken = default)
    {
        var result = await client.CreateChartAsync(shipSymbol, cancellationToken);
        return new ChartActionResult(result.Waypoint.Symbol, result.Waypoint.Type);
    }

    private static AgentModel MapAgent(Models.Agents.Agent agent) =>
        new(agent.Symbol, agent.AccountId, agent.Headquarters, agent.Credits, agent.StartingFaction, agent.ShipCount);

    private static NavModel MapNav(ShipNav nav) =>
        new(nav.Status, nav.SystemSymbol, nav.WaypointSymbol, nav.FlightMode, nav.Route?.Destination?.Symbol, nav.Route?.Arrival);

    private static FuelModel MapFuel(ShipFuel fuel) =>
        new(fuel.Current, fuel.Capacity);

    private static CargoModel MapCargo(ShipCargo cargo) =>
        new(cargo.Units, cargo.Capacity, cargo.Inventory?.Select(MapCargoItem).ToList());

    private static CargoItemModel MapCargoItem(CargoItem item) =>
        new(item.Symbol, item.Units);

    private static ShipModel MapShip(Models.Fleet.Ship ship) =>
        new(
            ship.Symbol,
            ship.Nav?.SystemSymbol,
            ship.Nav?.WaypointSymbol,
            ship.Nav?.Status,
            ship.Nav?.FlightMode,
            ship.Fuel?.Current ?? 0,
            ship.Fuel?.Capacity ?? 0,
            ship.Nav?.Route?.Arrival,
            ship.Nav?.Route?.Destination?.Symbol,
            ship.Cargo?.Units ?? 0,
            ship.Cargo?.Capacity ?? 0,
            default,
            ship.Registration?.Role ?? string.Empty,
            ship.Mounts?.Select(m => m.Symbol).ToList(),
            ship.Cargo?.Inventory?.Select(MapCargoItem).ToList(),
            ship.Modules is not null ? JsonSerializer.Serialize(ship.Modules) : null,
            ship.Frame is not null ? JsonSerializer.Serialize(ship.Frame) : null,
            ship.Reactor is not null ? JsonSerializer.Serialize(ship.Reactor) : null,
            ship.Engine is not null ? JsonSerializer.Serialize(ship.Engine) : null,
            ship.Cooldown?.Expiration);

    private static IReadOnlyList<ContractDeliverableModel> MapDeliverables(Models.Contracts.Contract contract)
        => contract.Terms?.Deliver?
            .Where(d => !string.IsNullOrWhiteSpace(d.TradeSymbol) && !string.IsNullOrWhiteSpace(d.DestinationSymbol))
            .Select(d => new ContractDeliverableModel(
                d.TradeSymbol,
                d.DestinationSymbol,
                d.UnitsRequired,
                d.UnitsFulfilled))
            .ToList() ?? [];

    private static ContractModel MapContract(Models.Contracts.Contract contract) =>
        new(
            contract.Id,
            contract.FactionSymbol,
            contract.Type,
            contract.Accepted,
            contract.Fulfilled,
            contract.Expiration,
            contract.DeadlineToAccept,
            contract.Terms?.Deadline,
            MapDeliverables(contract));

    private static WaypointDataModel MapWaypoint(Models.Systems.Waypoint waypoint)
    {
        var traits = waypoint.Traits ?? [];
        return new WaypointDataModel(
            waypoint.Symbol,
            waypoint.SystemSymbol,
            waypoint.Type,
            waypoint.X,
            waypoint.Y,
            traits.Any(t => t.Symbol.Equals("MARKETPLACE", StringComparison.OrdinalIgnoreCase)),
            traits.Any(t => t.Symbol.Equals("SHIPYARD", StringComparison.OrdinalIgnoreCase)),
            traits.Count > 0 ? JsonSerializer.Serialize(traits) : null,
            waypoint.Modifiers is not null && waypoint.Modifiers.Count > 0 ? JsonSerializer.Serialize(waypoint.Modifiers) : null,
            waypoint.Orbitals is not null && waypoint.Orbitals.Count > 0 ? JsonSerializer.Serialize(waypoint.Orbitals) : null,
            waypoint.Orbits,
            waypoint.IsUnderConstruction,
            waypoint.Chart is not null ? JsonSerializer.Serialize(waypoint.Chart) : null);
    }
}
