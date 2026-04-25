using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SpaceTraders.Infrastructure.Persistence;
using SpaceTraders.Infrastructure.Persistence.Entities;

namespace SpaceTraders.App.Pages;

public class SettingsModel : PageModel
{
    private readonly SpaceTradersDbContext _db;

    public SettingsModel(SpaceTradersDbContext db)
    {
        _db = db;
    }

    public IReadOnlyList<AgentSetting> Settings { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        Settings = await _db.Settings
            .AsNoTracking()
            .OrderBy(s => s.Key)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostAsync(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return RedirectToPage();

        var setting = await _db.Settings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting is not null)
        {
            setting.Value = value;
            await _db.SaveChangesAsync();
            StatusMessage = $"Setting '{key}' updated.";
        }

        return RedirectToPage();
    }
}
