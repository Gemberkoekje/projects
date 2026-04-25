using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.Infrastructure.Persistence.Repositories;

public sealed class ShipAssignmentRepository(SpaceTradersDbContext db) : IShipAssignmentRepository
{
    public async Task<ShipAssignmentDto?> FindAsync(string shipSymbol, CancellationToken cancellationToken = default)
    {
        var entity = await db.ShipAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ShipSymbol == shipSymbol, cancellationToken);
        return entity is null ? null : MapToDto(entity);
    }

    public async Task<IReadOnlyList<ShipAssignmentDto>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        var entities = await db.ShipAssignments
            .AsNoTracking()
            .Where(x => x.CompletedAt == null)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDto).ToList();
    }

    public async Task UpsertAsync(ShipAssignmentDto assignment, CancellationToken cancellationToken = default)
    {
        var existing = await db.ShipAssignments
            .FirstOrDefaultAsync(x => x.ShipSymbol == assignment.ShipSymbol, cancellationToken);

        if (existing is null)
        {
            db.ShipAssignments.Add(new ShipAssignmentRecord
            {
                ShipSymbol = assignment.ShipSymbol,
                Type = assignment.AssignmentType,
                OriginWaypoint = assignment.OriginWaypoint,
                DestWaypoint = assignment.DestWaypoint,
                CargoSymbol = assignment.CargoSymbol,
                ContractId = assignment.ContractId,
                StepIndex = assignment.StepIndex,
                AssignedAt = assignment.AssignedAt,
                CompletedAt = assignment.CompletedAt
            });
        }
        else
        {
            existing.Type = assignment.AssignmentType;
            existing.OriginWaypoint = assignment.OriginWaypoint;
            existing.DestWaypoint = assignment.DestWaypoint;
            existing.CargoSymbol = assignment.CargoSymbol;
            existing.ContractId = assignment.ContractId;
            existing.StepIndex = assignment.StepIndex;
            existing.AssignedAt = assignment.AssignedAt;
            existing.CompletedAt = assignment.CompletedAt;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static ShipAssignmentDto MapToDto(ShipAssignmentRecord entity) =>
        new(
            entity.ShipSymbol,
            entity.Type,
            entity.OriginWaypoint,
            entity.DestWaypoint,
            entity.CargoSymbol,
            entity.ContractId,
            entity.StepIndex,
            entity.AssignedAt,
            entity.CompletedAt);
}
