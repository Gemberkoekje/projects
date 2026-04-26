using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.App;
using SpaceTraders.Application.DTOs;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.App.Pages.Ships;

public sealed class DetailModel(SpaceTradersDbContext db) : PageModel
{
    public CachedShip? Ship { get; private set; }

    public ShipAssignmentDto? Assignment { get; private set; }

    public int FuelPercent => Ship is { FuelCapacity: > 0 }
        ? (int)Math.Round((double)Ship.FuelCurrent / Ship.FuelCapacity * 100)
        : 0;

    public int CargoPercent => Ship is { CargoCapacity: > 0 }
        ? (int)Math.Round((double)Ship.CargoCurrent / Ship.CargoCapacity * 100)
        : 0;

    public string ShipStatusClass
    {
        get
        {
            if (Ship is null)
            {
                return "info";
            }

            if (Ship.IsInTransit)
            {
                return "warning text-dark";
            }

            return Ship.Status == "DOCKED" ? "success" : "info";
        }
    }

    public string FuelProgressClass
    {
        get
        {
            if (FuelPercent < 20)
            {
                return "bg-danger";
            }

            return FuelPercent < 50 ? "bg-warning" : "bg-success";
        }
    }

    public string CargoProgressClass
    {
        get
        {
            if (CargoPercent >= 90)
            {
                return "bg-danger";
            }

            return CargoPercent >= 50 ? "bg-warning" : "bg-primary";
        }
    }

    public async Task<IActionResult> OnGetAsync(string symbol, CancellationToken cancellationToken)
    {
        var selectedToken = await AgentViewSelection.ResolveSelectedTokenAsync(db, HttpContext, cancellationToken);

        Ship = await db.Ships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.AgentToken == selectedToken && s.Symbol == symbol, cancellationToken);

        if (Ship is null)
            return Page();

        var record = await db.ShipAssignments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.AgentToken == selectedToken && a.ShipSymbol == symbol, cancellationToken);

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
