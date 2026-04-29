using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.App;
using SpaceTraders.App.Services;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.App.Pages.Ships;

public sealed class CommandModel : PageModel
{
    private readonly SpaceTradersDbContext _dbContext;
    private readonly IShipCommandClient _commands;

    public CommandModel(SpaceTradersDbContext dbContext, IShipCommandClient commands)
    {
        _dbContext = dbContext;
        _commands = commands;
    }

    [BindProperty(SupportsGet = true)]
    public string Symbol { get; set; } = string.Empty;

    [BindProperty]
    public string DestinationWaypoint { get; set; } = string.Empty;

    [BindProperty]
    public string FlightMode { get; set; } = "CRUISE";

    [BindProperty]
    public bool RefuelFromCargo { get; set; }

    public CachedShip Ship { get; private set; } = new() { Symbol = string.Empty };

    [TempData]
    public string StatusMessage { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Symbol))
        {
            return RedirectToPage("/Fleet/Index");
        }

        var selectedToken = await AgentViewSelection.ResolveSelectedTokenAsync(_dbContext, HttpContext, cancellationToken);

        var ship = await _dbContext.Ships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.AgentToken == selectedToken && s.Symbol == Symbol, cancellationToken);

        if (ship is null)
        {
            return RedirectToPage("/Fleet/Index");
        }

        Ship = ship;
        DestinationWaypoint = ship.WaypointSymbol ?? string.Empty;
        FlightMode = string.IsNullOrWhiteSpace(ship.FlightMode) ? "CRUISE" : ship.FlightMode;
        return Page();
    }

    public Task<IActionResult> OnPostDockAsync(CancellationToken cancellationToken)
        => QueueAsync(() => _commands.EnqueueDockAsync(Symbol, cancellationToken), "Dock command queued.");

    public Task<IActionResult> OnPostOrbitAsync(CancellationToken cancellationToken)
        => QueueAsync(() => _commands.EnqueueOrbitAsync(Symbol, cancellationToken), "Orbit command queued.");

    public Task<IActionResult> OnPostRefuelAsync(CancellationToken cancellationToken)
        => QueueAsync(() => _commands.EnqueueRefuelAsync(Symbol, RefuelFromCargo, cancellationToken), "Refuel command queued.");

    public Task<IActionResult> OnPostNavigateAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(DestinationWaypoint))
        {
            StatusMessage = "Destination waypoint is required.";
            return Task.FromResult<IActionResult>(RedirectToPage(new { symbol = Symbol }));
        }

        return QueueAsync(() => _commands.EnqueueNavigateAsync(Symbol, DestinationWaypoint.Trim(), cancellationToken), "Navigate command queued.");
    }

    public Task<IActionResult> OnPostFlightModeAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(FlightMode))
        {
            StatusMessage = "Flight mode is required.";
            return Task.FromResult<IActionResult>(RedirectToPage(new { symbol = Symbol }));
        }

        return QueueAsync(() => _commands.EnqueueSetFlightModeAsync(Symbol, FlightMode.Trim().ToUpperInvariant(), cancellationToken), "Flight-mode command queued.");
    }

    private async Task<IActionResult> QueueAsync(Func<Task> queue, string message)
    {
        if (string.IsNullOrWhiteSpace(Symbol))
        {
            return RedirectToPage("/Fleet/Index");
        }

        try
        {
            await queue();
            StatusMessage = message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Command failed: {ex.Message}";
        }

        return RedirectToPage(new { symbol = Symbol });
    }
}
