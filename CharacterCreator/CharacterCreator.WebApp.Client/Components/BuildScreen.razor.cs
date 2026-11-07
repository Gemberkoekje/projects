using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
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
using Microsoft.AspNetCore.Components.Web;
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
    private bool showPlans;
    private bool showMore;
    private IReadOnlyList<PresetContent> starters = [];
    private bool printing;
    private Notice notice = Notice.None;

    private Toast toast = Toast.None;
    private int toastSerial;
    private bool toastHeld;
    private bool toastExpired;
    private bool focusQuestion;
    private SkillKey restoreRow;
    private IReadOnlySet<SkillKey> fresh = new HashSet<SkillKey>();

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
        starters = PresetFacts.Starters(data.Content, data.Rules);
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
        fresh = new HashSet<SkillKey>();
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

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // The removal question takes focus, so a keyboard user lands on its answers; once answered,
        // focus goes back to the row it came from if the answer's buttons took it along.
        if (focusQuestion)
        {
            focusQuestion = false;
            await Browser.FocusAsync(".cc-dock [role=alertdialog] button");
        }
        else if (!restoreRow.IsEmpty)
        {
            var row = $"#cc-pick-{restoreRow}";
            restoreRow = default;
            await Browser.RestoreFocusAsync($"{row} [data-row-action]", $"{row} .cc-skill-name", "#cc-search");
        }
    }

    private async Task AddAsync(Holdings purchases)
    {
        var before = Snapshot();
        await ChangeAsync(plan with { Planned = plan.Planned.Plus(purchases) });
        fresh = purchases.Skills.ToHashSet();
        var cost = Ui.Number(SkillRules.Spent(purchases, data.Rules));
        var text = purchases.Skills.Count == 1
            ? Ui.T($"{words.Name(purchases.Skills[0])} toegevoegd · {cost} XP", $"Added {words.Name(purchases.Skills[0])} · {cost} XP")
            : Ui.T($"Toegevoegd: {Names(purchases.Skills)} · {cost} XP", $"Added: {Names(purchases.Skills)} · {cost} XP");
        ShowToast(NoticeKind.Success, text, before);
    }

    private Task MoreAsync(SkillKey skill) => AddAsync(Holdings.Of((skill, 1)));

    /// <summary>One purchase less; when other planned skills need it, ask first.</summary>
    private Task LessAsync(SkillKey skill) => RemoveAsync(skill, all: false);

    /// <summary>Every purchase of the skill; when other planned skills need it, ask first.</summary>
    private Task RemoveAllAsync(SkillKey skill) => RemoveAsync(skill, all: true);

    /// <summary>
    /// A removal nothing depends on happens at once, with Undo (UX-REVIEW.md P4); one that others
    /// need asks whether they go too, because that is a real choice, not a safety net.
    /// </summary>
    private async Task RemoveAsync(SkillKey skill, bool all)
    {
        // Dependants looks at one purchase less; for all of them, look from a plan with one left.
        var times = plan.Planned[skill];
        var from = all && times > 1 ? plan with { Planned = plan.Planned.Add(skill, 1 - times) } : plan;
        var dependants = PlanEvaluator.Dependants(data.Rules, from, skill);
        if (dependants.Count == 0)
        {
            var before = Snapshot();
            await ChangeAsync(plan with { Planned = plan.Planned.Add(skill, all ? -times : -1) });
            ShowToast(NoticeKind.Success, Removed(skill, all || times == 1, []), before);
            return;
        }

        pending = new Removal(skill, all, dependants);
        focusQuestion = true;
    }

    private async Task ConfirmRemovalAsync(bool withDependants)
    {
        var removal = pending;
        var before = Snapshot();
        var planned = plan.Planned.Add(removal.Skill, removal.All ? -plan.Planned[removal.Skill] : -1);
        if (withDependants)
        {
            planned = removal.Dependants.Aggregate(planned, (current, dependant) => current.Without(dependant));
        }

        await ChangeAsync(plan with { Planned = planned });
        restoreRow = removal.Skill;
        ShowToast(NoticeKind.Success, Removed(removal.Skill, removal.All || before.Plan.Planned[removal.Skill] == 1, withDependants ? removal.Dependants : []), before);
    }

    private void CancelRemoval()
    {
        restoreRow = pending.Skill;
        pending = Removal.None;
    }

    private string Removed(SkillKey skill, bool entirely, IReadOnlyList<SkillKey> along)
    {
        var skillName = words.Name(skill);
        var text = entirely ? Ui.T($"{skillName} verwijderd", $"Removed {skillName}") : Ui.T($"{skillName}: een minder", $"{skillName}: one less");
        return along.Count == 0 ? text : $"{text}, {Ui.T("met", "with")} {Names(along)}";
    }

    private string Names(IEnumerable<SkillKey> skills)
    {
        var names = skills.Select(words.Name).ToList();
        return names.Count <= 1
            ? string.Join(string.Empty, names)
            : $"{string.Join(", ", names.SkipLast(1))} {Ui.T("en", "and")} {names[^1]}";
    }

    /// <summary>The plan as it is, for Undo.</summary>
    private PlanSnapshot Snapshot() => new (name, plan, planId, madeOn);

    /// <summary>
    /// A toast above the bottom of the screen, where the eye is after a click (UX-REVIEW.md P4). It
    /// leaves after five seconds, but never while the pointer or focus is on it; an error stays until
    /// closed.
    /// </summary>
    private void ShowToast(NoticeKind kind, string text, PlanSnapshot undo)
    {
        toast = new Toast(kind, text, undo);
        toastExpired = false;
        var serial = ++toastSerial;
        if (kind != NoticeKind.Error)
        {
            _ = HideToastLaterAsync(serial);
        }
    }

    private async Task HideToastLaterAsync(int serial)
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        if (serial != toastSerial)
        {
            return;
        }

        if (toastHeld)
        {
            toastExpired = true;
            return;
        }

        toast = Toast.None;
        await InvokeAsync(StateHasChanged);
    }

    private void HoldToast(bool held)
    {
        toastHeld = held;
        if (!held && toastExpired)
        {
            toast = Toast.None;
            toastExpired = false;
        }
    }

    private void CloseToast()
    {
        toast = Toast.None;
        toastHeld = false;
        toastExpired = false;
    }

    private async Task UndoAsync()
    {
        var before = toast.Undo;
        name = before.Name;
        plan = before.Plan;
        planId = before.PlanId;
        madeOn = before.MadeOn;
        pending = Removal.None;
        notice = Notice.None;
        Evaluate();
        await KeepAsync();
        toastHeld = false;
        ShowToast(NoticeKind.Success, Ui.T("Ongedaan gemaakt.", "Undone."), PlanSnapshot.None);
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

    /// <summary>Replaces the plan with a preset, with Undo; from the presets page, the first visit panel, or the offer banner.</summary>
    private async Task StartFromAsync(PresetContent preset)
    {
        presetOffered = false;
        var before = Snapshot();
        var holdings = PresetFacts.Holdings(preset);
        name = Ui.T(preset.Name);
        planId = string.Empty;
        await ChangeAsync(preset.NewCharacter ? Plan.NewCharacter(holdings) : Plan.Free(preset.Xp, holdings));
        ShowToast(NoticeKind.Success, Ui.T($"Begonnen met het voorbeeld {Ui.T(preset.Name)}.", $"Started from the preset {Ui.T(preset.Name)}."), Undoable(before));
        Navigation.NavigateTo(Navigation.GetUriWithQueryParameter("voorbeeld", (string?)null), replace: true);
    }

    /// <summary>Undo only where there is something to go back to: an empty, unnamed plan needs none.</summary>
    private static PlanSnapshot Undoable(PlanSnapshot before)
        => before.Plan.Planned.IsEmpty && before.Name.Length == 0 ? PlanSnapshot.None : before;

    private void TogglePlans()
    {
        showPlans = !showPlans;
        showMore = false;
    }

    private void ToggleMore()
    {
        showMore = !showMore;
        showPlans = false;
    }

    private void CloseMenus()
    {
        showPlans = false;
        showMore = false;
    }

    private void MenuKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
        {
            CloseMenus();
        }
    }

    private async Task ExportFromMenuAsync()
    {
        CloseMenus();
        await ExportAsync();
    }

    private async Task NewFromMenuAsync()
    {
        CloseMenus();
        await NewAsync();
    }

    /// <summary>"Zelf kiezen": to the picker's search box.</summary>
    private async Task ToTheListAsync() => await Browser.ShowAndFocusAsync("#cc-search");

    private async Task PrintFromMenuAsync()
    {
        CloseMenus();
        await PrintAsync();
    }

    /// <summary>
    /// The XP bar's status jumps to the first thing that isn't right (UX-REVIEW.md P1): the skill's row
    /// in the chosen table, or the plan's general notes in the head.
    /// </summary>
    private async Task JumpToProblemAsync()
    {
        var first = FindingWords.Open(evaluation).FirstOrDefault();
        if (first is null)
        {
            return;
        }

        var target = first.Skill.IsEmpty ? "#cc-general" : $"#cc-chosen-{first.Skill}";
        await Browser.ShowAndFocusAsync(target, "#cc-general", ".cc-chosen");
    }

    /// <summary>The kind of plan, for the XP bar.</summary>
    private string ModeName() => plan.Mode switch
    {
        PlanMode.NewCharacter => Ui.T("nieuw personage", "new character"),
        PlanMode.Free => Ui.T("vrij plannen", "free planning"),
        _ => Ui.T("bestaand personage", "existing character"),
    };

    /// <summary>An amount of XP as a share of the plan's total, for the budget track's CSS.</summary>
    private string Percent(decimal xp)
    {
        var share = Math.Clamp(xp, 0m, 1m);
        if (evaluation.Xp > 0)
        {
            share = Math.Clamp(xp / evaluation.Xp, 0m, 1m);
        }

        return (share * 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

    private void KeepMyPlan()
    {
        presetOffered = false;
        Navigation.NavigateTo(Navigation.GetUriWithQueryParameter("voorbeeld", (string?)null), replace: true);
    }

    /// <summary>A new plan at once, with Undo instead of "are you sure?" (UX-REVIEW.md P4).</summary>
    private async Task NewAsync()
    {
        var before = Snapshot();
        name = string.Empty;
        planId = string.Empty;
        await ChangeAsync(Plan.NewCharacter(Holdings.None));
        ShowToast(NoticeKind.Success, Ui.T("Nieuw plan begonnen.", "Started a new plan."), Undoable(before));
    }

    private async Task SaveAsync()
    {
        var (stored, file) = await Plans.SaveAsync(Current());
        if (stored)
        {
            planId = file.Id;
            saved = await Plans.ListAsync();
            await KeepAsync();
            ShowToast(NoticeKind.Success, Ui.T("Opgeslagen in deze browser.", "Saved in this browser."), PlanSnapshot.None);
        }
        else
        {
            ShowToast(NoticeKind.Error, Ui.T("Opslaan lukte niet: deze browser bewaart niets voor deze site. Exporteer het plan als bestand.", "Saving failed: this browser keeps nothing for this site. Export the plan as a file."), PlanSnapshot.None);
        }
    }

    private async Task OpenSavedAsync(PlanFile file)
    {
        var before = Snapshot();
        Open(file);
        CloseMenus();
        notice = Notice.None;
        await KeepAsync();
        ShowToast(NoticeKind.Success, Ui.T($"{Title(file)} geopend.", $"Opened {Title(file)}."), Undoable(before));
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

    /// <summary>The character sheet as a PDF, made on the server from the plan as it is now.</summary>
    private async Task PrintAsync()
    {
        printing = true;
        try
        {
            using var response = await Http.PostAsJsonAsync(new Uri("api/plans/sheet", UriKind.Relative), Current(), RulesetJsonContext.Default.PlanFile);
            if (!response.IsSuccessStatusCode)
            {
                var message = response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                    ? Ui.T("Even geduld: er zijn net veel PDF's gemaakt. Probeer het zo nog eens.", "Hold on: many PDFs were just made. Try again in a moment.")
                    : Ui.T("De PDF kon niet gemaakt worden. Probeer het later nog eens.", "The PDF could not be made. Try again later.");
                ShowToast(NoticeKind.Error, message, PlanSnapshot.None);
                return;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync();
            await Browser.DownloadBytesAsync(Path.ChangeExtension(FileName(), ".pdf"), "application/pdf", bytes);
        }
        catch (System.Net.Http.HttpRequestException)
        {
            ShowToast(NoticeKind.Error, Ui.T("De server is niet bereikbaar; de PDF kon niet gemaakt worden.", "The server can't be reached; the PDF could not be made."), PlanSnapshot.None);
        }
        finally
        {
            printing = false;
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
        var before = Snapshot();
        Open(result.File with { Id = string.Empty });
        await KeepAsync();

        // The old builder's save needs its story told (the rules changed, skills were left out), so
        // that stays a banner; every import says it in a toast, with Undo.
        notice = result.Source == PlanSource.OldBuilder ? OldBuilderNotice(result) : Notice.None;
        ShowToast(NoticeKind.Success, Ui.T($"{Title(result.File)} ingelezen.", $"Read {Title(result.File)}."), Undoable(before));
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

    /// <summary>A message above the screen, after opening, importing or starting from a preset.</summary>
    private sealed record Notice(NoticeKind Kind, string Text, IReadOnlyList<string> Items)
    {
        public static Notice None { get; } = new (NoticeKind.None, string.Empty, []);
    }

    /// <summary>A toast at the bottom of the screen, with what Undo goes back to.</summary>
    private sealed record Toast(NoticeKind Kind, string Text, PlanSnapshot Undo)
    {
        public static Toast None { get; } = new (NoticeKind.None, string.Empty, PlanSnapshot.None);

        public bool CanUndo => Undo.IsSet;
    }

    /// <summary>The plan as it was before a change, for Undo.</summary>
    private sealed record PlanSnapshot(string Name, Plan Plan, string PlanId, string MadeOn)
    {
        public static PlanSnapshot None { get; } = new (string.Empty, Plan.NewCharacter(Holdings.None), string.Empty, string.Empty) { IsSet = false };

        public bool IsSet { get; init; } = true;
    }
}
