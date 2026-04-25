using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.SpaceTradersAPI.RateLimiting;

namespace SpaceTraders.Application.Queries;

public record GetAgentQuery;

public sealed class GetAgentQueryHandler(SpaceTradersDbContext db)
{
    public async Task<AgentDto?> Handle(GetAgentQuery query, CancellationToken cancellationToken)
    {
        var agent = await db.Agents
            .AsNoTracking()
            .OrderBy(a => a.Symbol)
            .FirstOrDefaultAsync(cancellationToken);

        return agent is null ? null : new AgentDto(
            agent.Symbol,
            agent.Credits,
            agent.StartingFaction ?? string.Empty,
            agent.ShipCount,
            agent.HeadquartersSymbol);
    }
}

public record GetAllShipsQuery;

public sealed class GetAllShipsQueryHandler(SpaceTradersDbContext db)
{
    public async Task<IReadOnlyList<ShipDto>> Handle(GetAllShipsQuery query, CancellationToken cancellationToken)
    {
        var ships = await db.Ships
            .AsNoTracking()
            .OrderBy(s => s.Symbol)
            .ToListAsync(cancellationToken);

        return ships.Select(s =>
        {
            s.ApplyArrivalIfDue();
            return new ShipDto(
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
                s.IsInTransit,
                s.LastSyncedAt);
        }).ToList();
    }
}

public record GetActiveContractsQuery;

public sealed class GetActiveContractsQueryHandler(SpaceTradersDbContext db)
{
    public async Task<IReadOnlyList<ContractDto>> Handle(GetActiveContractsQuery query, CancellationToken cancellationToken)
    {
        var contracts = await db.Contracts
            .AsNoTracking()
            .Where(c => !c.IsFulfilled)
            .OrderBy(c => c.Expiration)
            .ToListAsync(cancellationToken);

        return contracts.Select(c => new ContractDto(
            c.Id,
            c.FactionSymbol,
            c.Type,
            c.IsAccepted,
            c.IsFulfilled,
            c.Expiration,
            c.DeadlineToAccept)).ToList();
    }
}

public record GetRateLimitStatusQuery;

public sealed class GetRateLimitStatusQueryHandler(RateLimitStatus status)
{
    public Task<RateLimitStatusDto> Handle(GetRateLimitStatusQuery query, CancellationToken cancellationToken)
        => Task.FromResult(new RateLimitStatusDto(
            status.Remaining,
            status.Limit,
            status.BurstRemaining,
            status.BurstLimit,
            status.ResetAt,
            status.LimitType,
            status.TotalRequests,
            status.ThrottledCount));
}
