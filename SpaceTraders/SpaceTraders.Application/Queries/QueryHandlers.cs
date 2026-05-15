using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Queries;

public sealed record GetAgentQuery;

public sealed class GetAgentQueryHandler(IAgentRepository agents)
{
    public async Task<AgentDto?> Handle(GetAgentQuery query, CancellationToken cancellationToken)
    {
        var agent = await agents.GetAsync(cancellationToken);
        return agent is null ? null : new AgentDto(agent.Symbol, agent.Credits, agent.StartingFaction, agent.ShipCount, agent.HeadquartersSymbol);
    }
}

public sealed record GetAllShipsQuery;

public sealed class GetAllShipsQueryHandler(IShipRepository ships)
{
    public async Task<IReadOnlyList<ShipDto>> Handle(GetAllShipsQuery query, CancellationToken cancellationToken)
    {
        var all = await ships.GetAllAsync(cancellationToken);
        var now = TimeProvider.System.GetUtcNow();
        return all.Select(s => new ShipDto(
            s.Symbol,
            s.SystemSymbol,
            s.WaypointSymbol,
            s.Status,
            s.FlightMode,
            s.FuelCurrent,
            s.FuelCapacity,
            s.CargoCurrent,
            s.CargoCapacity,
            s.ArrivesAt,
            s.ArrivesAt.HasValue && s.ArrivesAt > now,
            now,
            s.ShipType,
            s.MountSymbols ?? [],
            s.HasMiningEquipment,
            s.HasSurveyEquipment,
            s.HasGasSiphonEquipment,
            s.HasGasProcessor,
            s.HasMineralProcessor)).ToList();
    }
}

public sealed record GetActiveContractsQuery;

public sealed class GetActiveContractsQueryHandler(IContractRepository contracts)
{
    public async Task<IReadOnlyList<ContractDto>> Handle(GetActiveContractsQuery query, CancellationToken cancellationToken)
        => await contracts.GetActiveAsync(cancellationToken);
}

public sealed record GetRateLimitStatusQuery;

public sealed class GetRateLimitStatusQueryHandler(IRateLimitStatus status)
{
    public Task<RateLimitStatusDto> Handle(GetRateLimitStatusQuery query, CancellationToken cancellationToken)
        => Task.FromResult(new RateLimitStatusDto(
            status.Remaining, status.Limit, status.BurstRemaining, status.BurstLimit,
            status.ResetAt, status.LimitType, status.TotalRequests, status.ThrottledCount));
}

public sealed record GetSettingsQuery;

public sealed class GetSettingsQueryHandler(ISettingsRepository settings)
{
    public async Task<IReadOnlyList<SettingDto>> Handle(GetSettingsQuery query, CancellationToken cancellationToken)
    {
        var all = await settings.GetAllAsync(cancellationToken);
        return all.Select(s => new SettingDto(s.Key, s.Value, s.Type, s.Description)).ToList();
    }
}

public sealed record GetActivityLogQuery
{
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;

    public string? ShipFilter { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public GetActivityLogQuery(int Page = 1, int PageSize = 50, string? ShipFilter = null)
    {
        this.Page = Page;
        this.PageSize = PageSize;
        this.ShipFilter = ShipFilter;
    }
}

public sealed class GetActivityLogQueryHandler(IActivityLogRepository activityLogs)
{
    public async Task<IReadOnlyList<ActivityLogDto>> Handle(GetActivityLogQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize < 1 ? 50 : query.PageSize;

        return await activityLogs.GetPagedAsync(page, pageSize, query.ShipFilter, cancellationToken);
    }
}

public sealed record GetBestTradeRouteQuery
{
    public required int CargoCapacity { get; init; }

    public int MinProfitPerUnit { get; init; } = 0;

    public int MaxDistanceJumps { get; init; } = 5;

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public GetBestTradeRouteQuery(int CargoCapacity, int MinProfitPerUnit = 0, int MaxDistanceJumps = 5)
    {
        this.CargoCapacity = CargoCapacity;
        this.MinProfitPerUnit = MinProfitPerUnit;
        this.MaxDistanceJumps = MaxDistanceJumps;
    }
}

public sealed class GetBestTradeRouteQueryHandler(
    ITradeOpportunityRepository tradeOpportunities,
    ISettingsRepository settings)
{
    private const int DefaultMinProfitPerUnit = 200;
    private const int DefaultMaxHaulDistance = 5;

    public async Task<TradeOpportunityDto?> Handle(GetBestTradeRouteQuery query, CancellationToken cancellationToken)
    {
        var minProfit = query.MinProfitPerUnit > 0
            ? query.MinProfitPerUnit
            : (await settings.GetAsync<int>("Trade.MinProfitPerUnit", cancellationToken) is var m and > 0 ? m : DefaultMinProfitPerUnit);

        var maxDistance = query.MaxDistanceJumps > 0
            ? query.MaxDistanceJumps
            : (await settings.GetAsync<int>("Trade.MaxHaulDistance", cancellationToken) is var d and > 0 ? d : DefaultMaxHaulDistance);

        return await tradeOpportunities.GetBestRouteForCapacityAsync(
            query.CargoCapacity, minProfit, maxDistance, cancellationToken);
    }
}

public sealed record GetShipDiagnosticsQuery(string ShipSymbol);

public sealed class GetShipDiagnosticsQueryHandler(
    IShipRepository ships,
    IShipAssignmentRepository assignments)
{
    public async Task<ShipDiagnosticsDto?> Handle(GetShipDiagnosticsQuery query, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(query.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            return null;
        }

        var assignment = await assignments.FindAsync(query.ShipSymbol, cancellationToken);
        var hasActiveAssignment = assignment is not null && !assignment.CompletedAt.HasValue;
        var isEligibleForContractMinerSelection = !hasActiveAssignment
            && ship.HasMiningEquipment
            && ship.CargoCapacity > 0
            && ship.FuelCapacity > 0;

        return new ShipDiagnosticsDto(
            Symbol: ship.Symbol,
            ShipType: ship.ShipType,
            SystemSymbol: ship.SystemSymbol,
            WaypointSymbol: ship.WaypointSymbol,
            Status: ship.Status,
            FlightMode: ship.FlightMode,
            FuelCurrent: ship.FuelCurrent,
            FuelCapacity: ship.FuelCapacity,
            CargoCurrent: ship.CargoCurrent,
            CargoCapacity: ship.CargoCapacity,
            IsInTransit: ship.IsInTransit,
            ArrivesAt: ship.ArrivesAt,
            LastSyncedAt: ship.LastSyncedAt,
            MountSymbols: ship.MountSymbols ?? [],
            HasMiningEquipment: ship.HasMiningEquipment,
            HasSurveyEquipment: ship.HasSurveyEquipment,
            HasGasSiphonEquipment: ship.HasGasSiphonEquipment,
            HasGasProcessor: ship.HasGasProcessor,
            HasMineralProcessor: ship.HasMineralProcessor,
            HasCargoSpace: ship.CargoCapacity > 0,
            HasFuelCapacity: ship.FuelCapacity > 0,
            IsOccupied: hasActiveAssignment,
            ActiveAssignmentType: hasActiveAssignment ? assignment!.AssignmentType : null,
            ActiveAssignmentContractId: hasActiveAssignment ? assignment!.ContractId : null,
            ActiveAssignmentSourceWaypoint: hasActiveAssignment ? assignment!.OriginWaypoint : null,
            ActiveAssignmentDestinationWaypoint: hasActiveAssignment ? assignment!.DestWaypoint : null,
            ActiveAssignmentAssignedAt: hasActiveAssignment ? assignment!.AssignedAt : null,
            ActiveAssignmentStepIndex: hasActiveAssignment ? assignment!.StepIndex : null,
            IsEligibleForContractMinerSelection: isEligibleForContractMinerSelection,
            ContractMinerEligibilityReason: BuildContractMinerEligibilityReason(
                hasActiveAssignment,
                assignment?.AssignmentType,
                ship.HasMiningEquipment,
                ship.CargoCapacity,
                ship.FuelCapacity));
    }

    private static string BuildContractMinerEligibilityReason(
        bool hasActiveAssignment,
        string? assignmentType,
        bool hasMiningEquipment,
        int cargoCapacity,
        int fuelCapacity)
    {
        if (hasActiveAssignment)
        {
            return string.IsNullOrWhiteSpace(assignmentType)
                ? "Occupied by active assignment"
                : $"Occupied by {assignmentType} assignment";
        }

        if (!hasMiningEquipment)
        {
            return "Missing mining equipment";
        }

        if (cargoCapacity <= 0)
        {
            return "No cargo capacity";
        }

        if (fuelCapacity <= 0)
        {
            return "No fuel capacity";
        }

        return "Eligible";
    }
}

