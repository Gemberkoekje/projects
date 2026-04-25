using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
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

    public async Task OnGetAsync()
    {
        TopRoutes = await _dbContext.TradeOpportunities
            .AsNoTracking()
            .OrderByDescending(t => t.SupportsSupplyChain)
            .ThenByDescending(t => t.SupplyChainDepth)
            .ThenByDescending(t => t.ProfitPerJump)
            .Take(20)
            .ToListAsync();

        Markets = await _dbContext.Markets
            .AsNoTracking()
            .OrderBy(m => m.SystemSymbol)
            .ThenBy(m => m.WaypointSymbol)
            .ToListAsync();
    }
}
