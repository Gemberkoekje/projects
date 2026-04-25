using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.App.Pages
{
    public class IndexModel : PageModel
    {
        private readonly SpaceTradersDbContext _dbContext;

        public IndexModel(SpaceTradersDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public CachedAgent? Agent { get; private set; }

        public IReadOnlyList<CachedShip> Ships { get; private set; } = new List<CachedShip>();

        public async Task OnGetAsync()
        {
            Agent = await _dbContext.Agents
                .AsNoTracking()
                .OrderBy(agent => agent.Symbol)
                .FirstOrDefaultAsync();

            Ships = await _dbContext.Ships
                .AsNoTracking()
                .OrderBy(ship => ship.Symbol)
                .ToListAsync();
        }
    }
}
