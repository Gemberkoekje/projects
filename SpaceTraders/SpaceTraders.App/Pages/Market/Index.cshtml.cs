using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.App;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.App.Pages.Market;

public sealed class IndexModel : PageModel
{
    private readonly SpaceTradersDbContext _dbContext;

    public IndexModel(SpaceTradersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IReadOnlyList<TradeOpportunity> TopRoutes { get; private set; } = [];

    public IReadOnlyList<CachedMarket> Markets { get; private set; } = [];

    public static string GetProfitPerUnitClass(TradeOpportunity route)
    {
        if (route.ProfitPerUnit >= 500)
        {
            return "text-success";
        }

        return route.ProfitPerUnit >= 200 ? string.Empty : "text-danger";
    }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var selectedToken = await AgentViewSelection.ResolveSelectedTokenAsync(_dbContext, HttpContext, cancellationToken);

        TopRoutes = await _dbContext.TradeOpportunities
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.AgentToken == selectedToken)
            .OrderByDescending(t => t.SupportsSupplyChain)
            .ThenByDescending(t => t.SupplyChainDepth)
            .ThenByDescending(t => t.ProfitPerJump)
            .Take(20)
            .ToListAsync(cancellationToken);

        Markets = await _dbContext.Markets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.AgentToken == selectedToken)
            .OrderBy(m => m.SystemSymbol)
            .ThenBy(m => m.WaypointSymbol)
            .ToListAsync(cancellationToken);
    }
}
