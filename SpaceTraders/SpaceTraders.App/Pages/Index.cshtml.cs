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

    public bool AlertApiUnavailable { get; private set; }

    public bool AlertTokenResetMismatch { get; private set; }

    public bool AlertCacheDivergence { get; private set; }

    public bool AlertAutomationDisabled { get; private set; }

    public bool AlertContractDeadlinesApproaching { get; private set; }

    public bool AlertResetUpcoming { get; private set; }

    public string NextResetDisplay { get; private set; } = string.Empty;

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

        var settings = await _dbContext.Settings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.AgentToken == selectedToken && s.Key.StartsWith("Runtime.Alert.", StringComparison.Ordinal))
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        AlertApiUnavailable = IsTrue(settings, "Runtime.Alert.ApiUnavailable");
        AlertTokenResetMismatch = IsTrue(settings, "Runtime.Alert.TokenResetMismatch");
        AlertCacheDivergence = IsTrue(settings, "Runtime.Alert.CacheDivergence");
        AlertAutomationDisabled = IsTrue(settings, "Runtime.Alert.AutomationDisabled");
        AlertContractDeadlinesApproaching = IsTrue(settings, "Runtime.Alert.ContractDeadlinesApproaching");
        AlertResetUpcoming = IsTrue(settings, "Runtime.Alert.ResetUpcoming");

        var nextResetRaw = await _dbContext.Settings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.AgentToken == selectedToken && s.Key == "Runtime.Reset.Next")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);

        NextResetDisplay = DateTimeOffset.TryParse(nextResetRaw, out var nextReset)
            ? nextReset.LocalDateTime.ToString("g")
            : string.Empty;
    }

    private static bool IsTrue(IReadOnlyDictionary<string, string> map, string key)
        => map.TryGetValue(key, out var value)
            && value.Equals("true", StringComparison.OrdinalIgnoreCase);
}
