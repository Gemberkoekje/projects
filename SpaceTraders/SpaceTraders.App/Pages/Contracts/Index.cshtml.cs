using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.App;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.App.Pages.Contracts;

public sealed class IndexModel : PageModel
{
    private readonly SpaceTradersDbContext _dbContext;

    public IndexModel(SpaceTradersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IReadOnlyList<CachedContract> Contracts { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var selectedToken = await AgentViewSelection.ResolveSelectedTokenAsync(_dbContext, HttpContext, cancellationToken);

        Contracts = await _dbContext.Contracts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.AgentToken == selectedToken)
            .OrderBy(c => c.IsFulfilled)
            .ThenBy(c => c.Expiration)
            .ToListAsync(cancellationToken);
    }
}
