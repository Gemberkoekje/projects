using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.App;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.App.Pages;

public sealed class SettingsModel : PageModel
{
    private readonly SpaceTradersDbContext _db;

    public SettingsModel(SpaceTradersDbContext db)
    {
        _db = db;
    }

    public IReadOnlyList<AgentSetting> Settings { get; private set; } = [];

    public IReadOnlyList<AgentViewOption> AgentOptions { get; private set; } = [];

    public string SelectedAgentToken { get; private set; } = string.Empty;

    public string ActiveAgentToken { get; private set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string Search { get; set; } = string.Empty;

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadPageAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(string key, string value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return RedirectToPage(new { search = Search });
        }

        var selectedToken = await AgentViewSelection.ResolveSelectedTokenAsync(_db, HttpContext, cancellationToken);

        var setting = await _db.Settings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.AgentToken == selectedToken && s.Key == key, cancellationToken);

        if (setting is not null)
        {
            _db.Entry(setting).CurrentValues.SetValues(new AgentSetting
            {
                AgentToken = setting.AgentToken,
                Key = setting.Key,
                Value = value,
                Type = setting.Type,
                Description = setting.Description,
            });

            await _db.SaveChangesAsync(cancellationToken);
            StatusMessage = $"Setting '{key}' updated for selected agent.";
        }
        else
        {
            StatusMessage = $"Setting '{key}' was not found for selected agent.";
        }

        return RedirectToPage(new { search = Search });
    }

    public async Task<IActionResult> OnPostSwitchViewAsync(string agentToken, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(agentToken))
        {
            AgentViewSelection.SetCookie(HttpContext, agentToken);
            StatusMessage = "Agent view switched.";
        }

        await LoadPageAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSetActiveTokenAsync(string agentToken, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(agentToken))
        {
            await AgentTokenSelection.SetActiveTokenAsync(_db, agentToken, cancellationToken);
            AgentViewSelection.SetCookie(HttpContext, agentToken);
            StatusMessage = "Active agent token updated. API will pick this token on startup.";
        }

        await LoadPageAsync(cancellationToken);
        return Page();
    }

    private async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        SelectedAgentToken = await AgentViewSelection.ResolveSelectedTokenAsync(_db, HttpContext, cancellationToken);
        ActiveAgentToken = await AgentTokenSelection.GetActiveTokenAsync(_db, cancellationToken);

        AgentOptions = await AgentViewSelection.GetAgentOptionsAsync(_db, Search, cancellationToken);

        Settings = await _db.Settings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.AgentToken == SelectedAgentToken)
            .OrderBy(s => s.Key)
            .ToListAsync(cancellationToken);
    }
}
