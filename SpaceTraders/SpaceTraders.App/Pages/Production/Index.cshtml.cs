using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.App;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.App.Pages.Production;

public sealed class IndexModel : PageModel
{
    private readonly SpaceTradersDbContext _dbContext;

    public IndexModel(SpaceTradersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IReadOnlyList<ProductionLinkViewModel> Links { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var selectedToken = await AgentViewSelection.ResolveSelectedTokenAsync(_dbContext, HttpContext, cancellationToken);

        var routes = await _dbContext.TradeOpportunities
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.AgentToken == selectedToken)
            .OrderByDescending(t => t.RouteScore)
            .ThenBy(t => t.TradeSymbol)
            .ToListAsync(cancellationToken);

        Links = routes
            .Select(route => new ProductionLinkViewModel(
                route.TradeSymbol,
                route.BuyWaypoint,
                route.SellWaypoint,
                route.BuyType,
                route.SellType,
                route.SupportsSupplyChain,
                route.SupplyChainDepth,
                route.RouteScore,
                route.ProfitPerUnit))
            .DistinctBy(x => new { x.TradeSymbol, x.BuyWaypoint, x.SellWaypoint })
            .Take(300)
            .ToList();
    }

    public sealed record ProductionLinkViewModel(
        string TradeSymbol,
        string BuyWaypoint,
        string SellWaypoint,
        string BuyType,
        string SellType,
        bool SupportsSupplyChain,
        int SupplyChainDepth,
        decimal RouteScore,
        int ProfitPerUnit);
}
