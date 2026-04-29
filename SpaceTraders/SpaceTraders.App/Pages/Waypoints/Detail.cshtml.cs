using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.App;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.App.Pages.Waypoints;

public sealed class DetailModel : PageModel
{
    private readonly SpaceTradersDbContext _dbContext;

    public DetailModel(SpaceTradersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public CachedWaypoint Waypoint { get; private set; } = new() { Symbol = string.Empty, SystemSymbol = string.Empty, Type = string.Empty };

    public CachedMarket Market { get; private set; } = new() { WaypointSymbol = string.Empty, SystemSymbol = string.Empty };

    public CachedShipyard Shipyard { get; private set; } = new() { WaypointSymbol = string.Empty, SystemSymbol = string.Empty };

    public bool NotFound { get; private set; }

    public async Task<IActionResult> OnGetAsync(string symbol, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return RedirectToPage("/Systems/Map");
        }

        var selectedToken = await AgentViewSelection.ResolveSelectedTokenAsync(_dbContext, HttpContext, cancellationToken);

        var waypoint = await _dbContext.Waypoints
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.AgentToken == selectedToken && w.Symbol == symbol, cancellationToken);

        if (waypoint is null)
        {
            NotFound = true;
            return Page();
        }

        Waypoint = waypoint;

        var market = await _dbContext.Markets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.AgentToken == selectedToken && m.WaypointSymbol == symbol, cancellationToken);

        if (market is not null)
        {
            Market = market;
        }

        var shipyard = await _dbContext.Shipyards
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.AgentToken == selectedToken && s.WaypointSymbol == symbol, cancellationToken);

        if (shipyard is not null)
        {
            Shipyard = shipyard;
        }

        return Page();
    }
}
