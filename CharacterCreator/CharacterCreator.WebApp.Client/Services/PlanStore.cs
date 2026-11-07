using System.Text.Json;
using CharacterCreator.core.Plans;
using CharacterCreator.core.Rulesets;

namespace CharacterCreator.WebApp.Client.Services;

/// <summary>
/// Plans in the browser's local storage (DESIGN.md decision 9): the plan being worked on, kept after
/// every change, and a list of saved plans.
/// </summary>
/// <remarks>One per browser tab. Unreadable storage is treated as empty.</remarks>
/// <param name="browser">The browser.</param>
public sealed class PlanStore(Browser browser)
{
    /// <summary>The key of the plan being worked on.</summary>
    public const string WorkKey = "charactercreator.werk";

    /// <summary>The key of the saved plans.</summary>
    public const string SavedKey = "charactercreator.opgeslagen";

    private readonly Browser browser = browser ?? throw new ArgumentNullException(nameof(browser));

    /// <summary>The plan being worked on.</summary>
    /// <returns>It; an empty new character when there is none.</returns>
    public async Task<PlanFile> LoadWorkAsync()
        => Read(await browser.LoadAsync(WorkKey), RulesetJsonContext.Default.PlanFile) ?? new PlanFile();

    /// <summary>Keeps the plan being worked on.</summary>
    /// <param name="plan">The plan.</param>
    /// <returns>Whether it was stored.</returns>
    public async Task<bool> KeepWorkAsync(PlanFile plan)
        => await browser.StoreAsync(WorkKey, JsonSerializer.Serialize(plan, RulesetJsonContext.Default.PlanFile));

    /// <summary>The saved plans, newest first.</summary>
    /// <returns>Them.</returns>
    public async Task<IReadOnlyList<PlanFile>> ListAsync()
    {
        var saved = Read(await browser.LoadAsync(SavedKey), RulesetJsonContext.Default.ListPlanFile) ?? [];
        return [.. saved.OrderByDescending(plan => plan.SavedAt)];
    }

    /// <summary>Saves a plan in the list, replacing the one with its id; a plan without an id gets one.</summary>
    /// <param name="plan">The plan.</param>
    /// <returns>The plan as saved, with its id; or why it was not.</returns>
    public Task<(bool Saved, PlanFile Plan)> SaveAsync(PlanFile plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return StoreAsync(plan.Id.Length > 0 ? plan : plan with { Id = Guid.NewGuid().ToString("N") });
    }

    /// <summary>Takes a plan off the list.</summary>
    /// <param name="id">The plan's id.</param>
    /// <returns>Whether the list was stored.</returns>
    public async Task<bool> DeleteAsync(string id)
    {
        var list = (await ListAsync()).Where(saved => saved.Id != id).ToList();
        return await browser.StoreAsync(SavedKey, JsonSerializer.Serialize(list, RulesetJsonContext.Default.ListPlanFile));
    }

    private async Task<(bool Saved, PlanFile Plan)> StoreAsync(PlanFile withId)
    {
        var list = (await ListAsync()).Where(saved => saved.Id != withId.Id).Prepend(withId).ToList();
        var stored = await browser.StoreAsync(SavedKey, JsonSerializer.Serialize(list, RulesetJsonContext.Default.ListPlanFile));
        return (stored, withId);
    }

    private static T? Read<T>(string json, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, type);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
