using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.App.Pages.Ships;

public class DetailModel(SpaceTradersDbContext db) : PageModel
{
    public CachedShip? Ship { get; private set; }

    public ShipAssignmentDto? Assignment { get; private set; }

    public int FuelPercent => Ship is { FuelCapacity: > 0 }
        ? (int)Math.Round((double)Ship.FuelCurrent / Ship.FuelCapacity * 100)
        : 0;

    public int CargoPercent => Ship is { CargoCapacity: > 0 }
        ? (int)Math.Round((double)Ship.CargoCurrent / Ship.CargoCapacity * 100)
        : 0;

    public async Task<IActionResult> OnGetAsync(string symbol, CancellationToken cancellationToken)
    {
        Ship = await db.Ships
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Symbol == symbol, cancellationToken);

        if (Ship is null)
            return Page();

        var record = await db.ShipAssignments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.ShipSymbol == symbol, cancellationToken);

        if (record is not null)
        {
            Assignment = new ShipAssignmentDto(
                record.ShipSymbol,
                record.Type,
                record.OriginWaypoint,
                record.DestWaypoint,
                record.CargoSymbol,
                record.ContractId,
                record.StepIndex,
                record.AssignedAt,
                record.CompletedAt);
        }

        return Page();
    }
}
