using System.IO;
using System.Text.Json;
using CharacterCreator.core.Content;
using CharacterCreator.core.Plans;
using CharacterCreator.core.Rules;
using CharacterCreator.core.Rulesets;
using CharacterCreator.core.Skills;
using CharacterCreator.WebApp.Client.Services;
using CharacterCreator.WebApp.Client.Words;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Qowaiv;

namespace CharacterCreator.WebApp.Client.Components;

/// <summary>
/// The build screen (DESIGN.md §5): it holds the plan, asks the engine about it after every change,
/// keeps it in the browser, and hands the picker, the chosen table and the summary what they show.
/// </summary>
public sealed partial class BuildScreen
{
    private const long MaxFileSize = 1024 * 1024;

    private LoadedRuleset data = default!;
    private FindingWords words = default!;
    private PlanEvaluation evaluation = default!;
    private IReadOnlyDictionary<SkillKey, SkillOption> options = new Dictionary<SkillKey, SkillOption>();
    private IReadOnlyList<PlanFile> saved = [];
    private bool ready;
    private string problem = string.Empty;

    private string name = string.Empty;
    private Plan plan = Plan.NewCharacter(Holdings.None);
    private string planId = string.Empty;
    private string madeOn = string.Empty;
    private int events;

    private Removal pending = Removal.None;
    private PresetContent offeredPreset = default!;
    private bool presetOffered;
    private bool confirmNew;
    private bool showSaved;
    private Notice notice = Notice.None;

    private bool MadeOnAnotherRuleset => madeOn.Length > 0 && madeOn != data.Ruleset.Id;

    private IEnumerable<Finding> General => evaluation.Findings.Where(finding => finding.Skill.IsEmpty);

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        var loaded = await Rulesets.LoadAsync(CancellationToken.None);
        if (!loaded.IsValid)
        {
            problem = string.Join(" ", loaded.Messages.Select(message => message.Message));
            return;
        }

        data = loaded.Value;
        words = new FindingWords(Ui, data.Content);
        Open(await Plans.LoadWorkAsync());
        saved = await Plans.ListAsync();
        ready = true;

        var preset = data.Content.Presets.FirstOrDefault(candidate => string.Equals(candidate.Key, Preset, StringComparison.OrdinalIgnoreCase));
        if (preset is not null)
        {
            if (plan.Planned.IsEmpty)
            {
                await StartFromAsync(preset);
            }
            else
            {
                offeredPreset = preset;
                presetOffered = true;
            }
        }
    }

    private void Open(PlanFile file)
    {
        name = file.Name;
        plan = file.ToPlan();
        planId = file.Id;
        madeOn = file.Ruleset;
        pending = Removal.None;
        Evaluate();
    }

    private void Evaluate()
    {
        evaluation = PlanEvaluator.Evaluate(data.Rules, plan);
        options = PlanEvaluator.Options(data.Rules, plan, data.Offered);
    }

    /// <summary>A change to the plan: evaluate it, and keep it in the browser.</summary>
    private async Task ChangeAsync(Plan next)
    {
        plan = next;
        madeOn = data.Ruleset.Id;
        pending = Removal.None;
        notice = Notice.None;
        Evaluate();
        await KeepAsync();
    }

    private Task KeepAsync() => Plans.KeepWorkAsync(Current());

    private PlanFile Current() => PlanFile.From(name, plan, madeOn.Length > 0 ? madeOn : data.Ruleset.Id, Now()) with { Id = planId };

    private static DateTimeOffset Now() => new (Clock.UtcNow(), TimeSpan.Zero);

    private Task AddAsync(Holdings purchases) => ChangeAsync(plan with { Planned = plan.Planned.Plus(purchases) });

    private Task MoreAsync(SkillKey skill) => ChangeAsync(plan.WithPurchase(skill));

    /// <summary>One purchase less; when other planned skills need it, ask first.</summary>
    private Task LessAsync(SkillKey skill) => RemoveAsync(skill, all: false);

    /// <summary>Every purchase of the skill; when other planned skills need it, ask first.</summary>
    private Task RemoveAllAsync(SkillKey skill) => RemoveAsync(skill, all: true);

    private async Task RemoveAsync(SkillKey skill, bool all)
    {
        // Dependants looks at one purchase less; for all of them, look from a plan with one left.
        var times = plan.Planned[skill];
        var from = all && times > 1 ? plan with { Planned = plan.Planned.Add(skill, 1 - times) } : plan;
        var dependants = PlanEvaluator.Dependants(data.Rules, from, skill);
        if (dependants.Count == 0)
        {
            await ChangeAsync(plan with { Planned = plan.Planned.Add(skill, all ? -times : -1) });
            return;
        }

        pending = new Removal(skill, all, dependants);
    }

    private async Task ConfirmRemovalAsync(bool withDependants)
    {
        var removal = pending;
        var planned = plan.Planned.Add(removal.Skill, removal.All ? -plan.Planned[removal.Skill] : -1);
        if (withDependants)
        {
            planned = removal.Dependants.Aggregate(planned, (current, dependant) => current.Without(dependant));
        }

        await ChangeAsync(plan with { Planned = planned });
    }

    private async Task NameChangedAsync(ChangeEventArgs args)
    {
        name = (args.Value as string ?? string.Empty).Trim();
        await KeepAsync();
    }

    /// <summary>Switches between a new character and free planning, keeping the skills and the free XP.</summary>
    private Task ModeChangedAsync(PlanMode mode)
    {
        if (mode == plan.Mode)
        {
            return Task.CompletedTask;
        }

        if (mode == PlanMode.NewCharacter)
        {
            return ChangeAsync(Plan.NewCharacter(plan.Planned) with { Xp = plan.Xp });
        }

        var xp = plan.Xp > 0 ? plan.Xp : data.Rules.Creation.StartXp;
        return ChangeAsync(Plan.Free(xp, plan.Planned));
    }

    private Task XpChangedAsync(ChangeEventArgs args)
        => decimal.TryParse(Convert.ToString(args.Value, System.Globalization.CultureInfo.InvariantCulture)?.Replace(',', '.'), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var xp)
            ? ChangeAsync(plan with { Xp = Math.Max(xp, 0m) })
            : Task.CompletedTask;

    /// <summary>"XP after a number of events": the starting XP with each event's award (DESIGN.md §3.3).</summary>
    private Task EventsChangedAsync(ChangeEventArgs args)
    {
        if (!int.TryParse(Convert.ToString(args.Value, System.Globalization.CultureInfo.InvariantCulture), out var count))
        {
            return Task.CompletedTask;
        }

        events = Math.Clamp(count, 0, 200);
        return ChangeAsync(plan with { Xp = data.Rules.Xp.After(data.Rules.Creation.StartXp, events) });
    }

    private async Task StartFromAsync(PresetContent preset)
    {
        presetOffered = false;
        var holdings = Holdings.Of(preset.Skills.Select(skill => new KeyValuePair<SkillKey, int>(skill.Key, skill.Times)));
        name = Ui.T(preset.Name);
        planId = string.Empty;
        await ChangeAsync(preset.NewCharacter ? Plan.NewCharacter(holdings) : Plan.Free(preset.Xp, holdings));
        notice = new Notice(NoticeKind.Success, Ui.T($"Begonnen met het voorbeeld {Ui.T(preset.Name)}.", $"Started from the preset {Ui.T(preset.Name)}."), []);
        Navigation.NavigateTo(Navigation.GetUriWithQueryParameter("voorbeeld", (string?)null), replace: true);
    }

    private void KeepMyPlan()
    {
        presetOffered = false;
        Navigation.NavigateTo(Navigation.GetUriWithQueryParameter("voorbeeld", (string?)null), replace: true);
    }

    private async Task NewAsync()
    {
        if (!plan.Planned.IsEmpty && !confirmNew)
        {
            confirmNew = true;
            return;
        }

        confirmNew = false;
        name = string.Empty;
        planId = string.Empty;
        await ChangeAsync(Plan.NewCharacter(Holdings.None));
    }

    private async Task SaveAsync()
    {
        var (stored, file) = await Plans.SaveAsync(Current());
        if (stored)
        {
            planId = file.Id;
            saved = await Plans.ListAsync();
            await KeepAsync();
            notice = new Notice(NoticeKind.Success, Ui.T("Opgeslagen in deze browser.", "Saved in this browser."), []);
        }
        else
        {
            notice = new Notice(NoticeKind.Error, Ui.T("Opslaan lukte niet: deze browser bewaart niets voor deze site. Exporteer het plan als bestand.", "Saving failed: this browser keeps nothing for this site. Export the plan as a file."), []);
        }
    }

    private async Task OpenSavedAsync(PlanFile file)
    {
        Open(file);
        showSaved = false;
        await KeepAsync();
        notice = new Notice(NoticeKind.Success, Ui.T($"{Title(file)} geopend.", $"Opened {Title(file)}."), []);
    }

    private async Task DeleteSavedAsync(PlanFile file)
    {
        await Plans.DeleteAsync(file.Id);
        saved = await Plans.ListAsync();
        if (planId == file.Id)
        {
            planId = string.Empty;
            await KeepAsync();
        }
    }

    private async Task ExportAsync()
    {
        var json = JsonSerializer.Serialize(Current(), RulesetJsonContext.Default.PlanFile);
        await Browser.DownloadAsync(FileName(), "application/json", json);
    }

    private async Task ImportAsync(InputFileChangeEventArgs args)
    {
        string text;
        try
        {
            await using var stream = args.File.OpenReadStream(MaxFileSize);
            using var reader = new StreamReader(stream);
            text = await reader.ReadToEndAsync();
        }
        catch (IOException)
        {
            notice = new Notice(NoticeKind.Error, Ui.T("Dat bestand is te groot of niet te lezen.", "That file is too big or cannot be read."), []);
            return;
        }

        var imported = PlanImport.Read(text, data.Content.Legacy);
        if (!imported.IsValid)
        {
            notice = new Notice(NoticeKind.Error, Ui.T("Dit is geen bestand van de Character Creator, en ook geen opgeslagen personage van de oude builder.", "This is not a Character Creator file, nor a character saved by the old builder."), []);
            return;
        }

        var result = imported.Value;
        Open(result.File with { Id = string.Empty });
        await KeepAsync();
        notice = result.Source == PlanSource.OldBuilder ? OldBuilderNotice(result) : new Notice(NoticeKind.Success, Ui.T($"{Title(result.File)} ingelezen.", $"Read {Title(result.File)}."), []);
    }

    /// <summary>What an old builder's save brought along, and what it left behind.</summary>
    private Notice OldBuilderNotice(ImportedPlan result)
    {
        var text = Ui.T(
            $"Ingelezen uit de oude builder ({result.OldVersion}). Nu gelden VOID's regels van {data.Ruleset.ContentRuleset}; de opmerkingen tonen wat daardoor niet meer klopt.",
            $"Read from the old builder ({result.OldVersion}). VOID's {data.Ruleset.ContentRuleset} rules apply now; the notes show what no longer fits.");
        if (result.LeftOut.Count == 0)
        {
            return new Notice(NoticeKind.Success, text, []);
        }

        var leftOut = string.Join(", ", result.LeftOut);
        return new Notice(NoticeKind.Warning, text, [Ui.T($"Niet meegekomen (staat niet meer in het boek): {leftOut}", $"Left out (no longer in the book): {leftOut}")]);
    }

    private string Title(PlanFile file) => file.Name.Length > 0 ? file.Name : Ui.T("Naamloos personage", "Unnamed character");

    private string FileName()
    {
        var safe = new string([.. name.Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-')]).Trim('-');
        return $"{(safe.Length > 0 ? safe : Ui.T("personage", "character"))}.json";
    }

    private string When(DateTimeOffset moment) => moment.ToOffset(TimeSpan.FromHours(1)).ToString(Ui.IsEnglish ? "d MMM yyyy HH:mm" : "d-M-yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A removal waiting for an answer, because other planned skills depend on the skill.</summary>
    private sealed record Removal(SkillKey Skill, bool All, IReadOnlyList<SkillKey> Dependants)
    {
        public static Removal None { get; } = new (default, false, []);

        public bool IsWaiting => !Skill.IsEmpty;
    }

    /// <summary>What a notice is about.</summary>
    private enum NoticeKind
    {
        None = 0,
        Success,
        Warning,
        Error,
    }

    /// <summary>A message above the screen, after saving, opening, importing or starting from a preset.</summary>
    private sealed record Notice(NoticeKind Kind, string Text, IReadOnlyList<string> Items)
    {
        public static Notice None { get; } = new (NoticeKind.None, string.Empty, []);
    }
}
