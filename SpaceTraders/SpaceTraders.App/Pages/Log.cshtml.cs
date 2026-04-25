using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.App.Pages;

public class LogModel : PageModel
{
    private const int PageSize = 50;
    private readonly SpaceTradersDbContext _db;

    public LogModel(SpaceTradersDbContext db)
    {
        _db = db;
    }

    public IReadOnlyList<ActivityLog> Entries { get; private set; } = [];
    public int CurrentPage { get; private set; } = 1;
    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => Entries.Count == PageSize;

    public async Task OnGetAsync(int page = 1)
    {
        CurrentPage = Math.Max(1, page);

        Entries = await _db.ActivityLogs
            .AsNoTracking()
            .OrderByDescending(l => l.Timestamp)
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync();
    }
}
