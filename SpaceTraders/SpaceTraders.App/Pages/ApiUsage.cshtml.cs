using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.App;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.App.Pages;

public sealed class ApiUsageModel : PageModel
{
    private readonly SpaceTradersDbContext _db;

    public ApiUsageModel(SpaceTradersDbContext db)
    {
        _db = db;
    }

    public IReadOnlyList<ApiEndpointUsage> Entries { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var selectedToken = await AgentViewSelection.ResolveSelectedTokenAsync(_db, HttpContext, cancellationToken);

        Entries = await _db.ApiEndpointUsages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.AgentToken == selectedToken)
            .OrderByDescending(x => x.Calls)
            .ThenBy(x => x.HttpMethod)
            .ThenBy(x => x.Endpoint)
            .ToListAsync(cancellationToken);
    }
}
