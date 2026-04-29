using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.App;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.App.Pages.Systems;

public sealed class MapModel : PageModel
{
    private readonly SpaceTradersDbContext _dbContext;

    public MapModel(SpaceTradersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IReadOnlyList<SystemMapNode> Systems { get; private set; } = [];

    public IReadOnlyList<WaypointMapNode> Waypoints { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var selectedToken = await AgentViewSelection.ResolveSelectedTokenAsync(_dbContext, HttpContext, cancellationToken);

        var systems = await _dbContext.Systems
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.AgentToken == selectedToken)
            .OrderBy(s => s.Symbol)
            .ToListAsync(cancellationToken);

        var waypoints = await _dbContext.Waypoints
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(w => w.AgentToken == selectedToken)
            .OrderBy(w => w.SystemSymbol)
            .ThenBy(w => w.Symbol)
            .ToListAsync(cancellationToken);

        var waypointCount = waypoints
            .GroupBy(w => w.SystemSymbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        Systems = systems
            .Select(s => new SystemMapNode(
                s.Symbol,
                s.SectorSymbol,
                s.Type,
                s.X,
                s.Y,
                waypointCount.TryGetValue(s.Symbol, out var count) ? count : 0,
                s.LastObservedAt))
            .ToList();

        Waypoints = waypoints
            .Select(w => new WaypointMapNode(
                w.Symbol,
                w.SystemSymbol,
                w.Type,
                w.X,
                w.Y,
                w.HasMarket,
                w.HasShipyard,
                w.IsUnderConstruction,
                w.LastObservedAt))
            .ToList();
    }

    public sealed record SystemMapNode(
        string Symbol,
        string SectorSymbol,
        string Type,
        int X,
        int Y,
        int WaypointCount,
        DateTimeOffset LastObservedAt);

    public sealed record WaypointMapNode(
        string Symbol,
        string SystemSymbol,
        string Type,
        int X,
        int Y,
        bool HasMarket,
        bool HasShipyard,
        bool IsUnderConstruction,
        DateTimeOffset LastObservedAt);
}
