using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Queries;

public record GetAgentQuery;

public sealed class GetAgentQueryHandler(IAgentRepository agents)
{
    public async Task<AgentDto?> Handle(GetAgentQuery query, CancellationToken cancellationToken)
    {
        var agent = await agents.GetAsync(cancellationToken);
        return agent is null ? null : new AgentDto(agent.Symbol, agent.Credits, agent.StartingFaction, agent.ShipCount, agent.HeadquartersSymbol);
    }
}

public record GetAllShipsQuery;

public sealed class GetAllShipsQueryHandler(IShipRepository ships)
{
    public async Task<IReadOnlyList<ShipDto>> Handle(GetAllShipsQuery query, CancellationToken cancellationToken)
    {
        var all = await ships.GetAllAsync(cancellationToken);
        return all.Select(s => new ShipDto(
            s.Symbol, s.SystemSymbol, s.WaypointSymbol, s.Status, s.FlightMode,
            s.FuelCurrent, s.FuelCapacity, s.CargoCurrent, s.CargoCapacity,
            s.ArrivesAt, s.ArrivesAt.HasValue && s.ArrivesAt > DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow)).ToList();
    }
}

public record GetActiveContractsQuery;

public sealed class GetActiveContractsQueryHandler(IContractRepository contracts)
{
    public async Task<IReadOnlyList<ContractDto>> Handle(GetActiveContractsQuery query, CancellationToken cancellationToken)
        => await contracts.GetActiveAsync(cancellationToken);
}

public record GetRateLimitStatusQuery;

public sealed class GetRateLimitStatusQueryHandler(IRateLimitStatus status)
{
    public Task<RateLimitStatusDto> Handle(GetRateLimitStatusQuery query, CancellationToken cancellationToken)
        => Task.FromResult(new RateLimitStatusDto(
            status.Remaining, status.Limit, status.BurstRemaining, status.BurstLimit,
            status.ResetAt, status.LimitType, status.TotalRequests, status.ThrottledCount));
}

public record GetSettingsQuery;

public sealed class GetSettingsQueryHandler(ISettingsRepository settings)
{
    public async Task<IReadOnlyList<SettingDto>> Handle(GetSettingsQuery query, CancellationToken cancellationToken)
    {
        var all = await settings.GetAllAsync(cancellationToken);
        return all.Select(s => new SettingDto(s.Key, s.Value, s.Type, s.Description)).ToList();
    }
}

public record GetActivityLogQuery(int Page = 1, int PageSize = 50, string? ShipFilter = null);

public sealed class GetActivityLogQueryHandler(IActivityLogRepository activityLog)
{
    public async Task<IReadOnlyList<ActivityLogDto>> Handle(GetActivityLogQuery query, CancellationToken cancellationToken)
        => await activityLog.GetPagedAsync(query.Page, query.PageSize, query.ShipFilter, cancellationToken);
}

public record GetBestTradeRouteQuery(int CargoCapacity, int MinProfitPerUnit = 0, int MaxDistanceJumps = 5);

public sealed class GetBestTradeRouteQueryHandler(
    ITradeOpportunityRepository tradeOpportunities,
    ISettingsRepository settings)
{
    public async Task<TradeOpportunityDto?> Handle(GetBestTradeRouteQuery query, CancellationToken cancellationToken)
    {
        var minProfit = query.MinProfitPerUnit > 0
            ? query.MinProfitPerUnit
            : await settings.GetAsync<int>("Trade.MinProfitPerUnit", cancellationToken);

        var maxDistance = query.MaxDistanceJumps > 0
            ? query.MaxDistanceJumps
            : await settings.GetAsync<int>("Trade.MaxHaulDistance", cancellationToken);

        return await tradeOpportunities.GetBestRouteForCapacityAsync(
            query.CargoCapacity, minProfit, maxDistance, cancellationToken);
    }
}

