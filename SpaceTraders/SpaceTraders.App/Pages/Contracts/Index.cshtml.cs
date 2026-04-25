using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
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

    public async Task OnGetAsync()
    {
        Contracts = await _dbContext.Contracts
            .AsNoTracking()
            .OrderBy(c => c.IsFulfilled)
            .ThenBy(c => c.Expiration)
            .ToListAsync();
    }
}
