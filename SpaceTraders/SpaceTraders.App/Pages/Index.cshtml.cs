using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.App;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.App.Pages;

public sealed class IndexModel : PageModel
{
    private readonly SpaceTradersDbContext _dbContext;

    public IndexModel(SpaceTradersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public CachedAgent? Agent { get; private set; }

    public IReadOnlyList<CachedShip> Ships { get; private set; } = new List<CachedShip>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var selectedToken = await AgentViewSelection.ResolveSelectedTokenAsync(_dbContext, HttpContext, cancellationToken);

        Agent = await _dbContext.Agents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(agent => agent.AgentToken == selectedToken)
            .OrderBy(agent => agent.Symbol)
            .FirstOrDefaultAsync(cancellationToken);

        Ships = await _dbContext.Ships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(ship => ship.AgentToken == selectedToken)
            .OrderBy(ship => ship.Symbol)
            .ToListAsync(cancellationToken);
    }
}
