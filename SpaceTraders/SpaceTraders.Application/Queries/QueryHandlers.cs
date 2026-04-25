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
            s.FuelCurrent, s.FuelCapacity, 0, 0,
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
