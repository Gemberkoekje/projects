using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.App;
using SpaceTraders.App.Services;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.App.Pages.Maintenance;

public sealed class IndexModel : PageModel
{
    private readonly SpaceTradersDbContext _dbContext;
    private readonly IShipCommandClient _commands;

    public IndexModel(SpaceTradersDbContext dbContext, IShipCommandClient commands)
    {
        _dbContext = dbContext;
        _commands = commands;
    }

    public IReadOnlyList<MaintenanceShipViewModel> Ships { get; private set; } = [];

    [BindProperty]
    public string ShipSymbol { get; set; } = string.Empty;

    [BindProperty]
    public string EquipmentSymbol { get; set; } = string.Empty;

    [TempData]
    public string StatusMessage { get; set; } = string.Empty;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var selectedToken = await AgentViewSelection.ResolveSelectedTokenAsync(_dbContext, HttpContext, cancellationToken);

        var ships = await _dbContext.Ships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.AgentToken == selectedToken)
            .OrderBy(s => s.Symbol)
            .ToListAsync(cancellationToken);

        Ships = ships.Select(Map).ToList();
    }

    public Task<IActionResult> OnPostRepairAsync(CancellationToken cancellationToken)
        => QueueAsync(() => _commands.EnqueueRepairAsync(ShipSymbol, cancellationToken), "Repair command queued.");

    public Task<IActionResult> OnPostScrapAsync(CancellationToken cancellationToken)
        => QueueAsync(() => _commands.EnqueueScrapAsync(ShipSymbol, cancellationToken), "Scrap command queued.");

    public Task<IActionResult> OnPostInstallMountAsync(CancellationToken cancellationToken)
        => QueueWithSymbolAsync(symbol => _commands.EnqueueInstallMountAsync(ShipSymbol, symbol, cancellationToken), "Install mount command queued.");

    public Task<IActionResult> OnPostRemoveMountAsync(CancellationToken cancellationToken)
        => QueueWithSymbolAsync(symbol => _commands.EnqueueRemoveMountAsync(ShipSymbol, symbol, cancellationToken), "Remove mount command queued.");

    public Task<IActionResult> OnPostInstallModuleAsync(CancellationToken cancellationToken)
        => QueueWithSymbolAsync(symbol => _commands.EnqueueInstallModuleAsync(ShipSymbol, symbol, cancellationToken), "Install module command queued.");

    public Task<IActionResult> OnPostRemoveModuleAsync(CancellationToken cancellationToken)
        => QueueWithSymbolAsync(symbol => _commands.EnqueueRemoveModuleAsync(ShipSymbol, symbol, cancellationToken), "Remove module command queued.");

    private Task<IActionResult> QueueWithSymbolAsync(Func<string, Task> queue, string successMessage)
    {
        if (string.IsNullOrWhiteSpace(EquipmentSymbol))
        {
            StatusMessage = "Equipment symbol is required.";
            return Task.FromResult<IActionResult>(RedirectToPage());
        }

        return QueueAsync(() => queue(EquipmentSymbol.Trim().ToUpperInvariant()), successMessage);
    }

    private async Task<IActionResult> QueueAsync(Func<Task> queue, string successMessage)
    {
        if (string.IsNullOrWhiteSpace(ShipSymbol))
        {
            StatusMessage = "Ship symbol is required.";
            return RedirectToPage();
        }

        try
        {
            await queue();
            StatusMessage = successMessage;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Command failed: {ex.Message}";
        }

        return RedirectToPage();
    }

    private static MaintenanceShipViewModel Map(CachedShip ship)
    {
        var mounts = ParseStringArray(ship.MountsJson);
        var modules = ParseSymbolArray(ship.ModulesJson);

        return new MaintenanceShipViewModel(
            ship.Symbol,
            ship.Status ?? string.Empty,
            ship.SystemSymbol ?? string.Empty,
            ship.WaypointSymbol ?? string.Empty,
            mounts,
            modules,
            ship.ModulesJson ?? string.Empty,
            ship.FrameJson ?? string.Empty,
            ship.ReactorJson ?? string.Empty,
            ship.EngineJson ?? string.Empty,
            ship.LastSyncedAt);
    }

    private static IReadOnlyList<string> ParseStringArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ParseSymbolArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var elements = JsonSerializer.Deserialize<List<SymbolJson>>(json) ?? [];
            return elements
                .Select(e => e.Symbol)
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Select(symbol => symbol.Trim().ToUpperInvariant())
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public sealed record MaintenanceShipViewModel(
        string Symbol,
        string Status,
        string SystemSymbol,
        string WaypointSymbol,
        IReadOnlyList<string> Mounts,
        IReadOnlyList<string> Modules,
        string ModulesJson,
        string FrameJson,
        string ReactorJson,
        string EngineJson,
        DateTimeOffset LastSyncedAt);

    private sealed class SymbolJson
    {
        public string Symbol { get; init; } = string.Empty;
    }
}
