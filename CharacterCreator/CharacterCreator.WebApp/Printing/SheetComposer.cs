using System.Globalization;
using CharacterCreator.application.Sheets;
using CharacterCreator.core.Content;
using CharacterCreator.core.Plans;
using CharacterCreator.core.Rules;
using CharacterCreator.core.Skills;
using CharacterCreator.WebApp.Client.Services;
using CharacterCreator.WebApp.Client.Words;

namespace CharacterCreator.WebApp.Printing;

/// <summary>
/// Turns a plan into a character sheet in the reader's language (DESIGN.md §5, Print): the engine's
/// findings worded as on the build screen, and the summary from the same <see cref="PlanSummary"/>.
/// </summary>
/// <remarks>Stateless and thread-safe.</remarks>
internal static class SheetComposer
{
    /// <summary>How many events "XP per event" looks ahead.</summary>
    public const int Events = 10;

    /// <summary>Composes the sheet.</summary>
    /// <param name="data">The ruleset to check the plan against.</param>
    /// <param name="file">The plan, as the browser keeps it.</param>
    /// <param name="ui">The reader's language.</param>
    /// <param name="now">When the sheet is made.</param>
    /// <returns>The sheet.</returns>
    public static CharacterSheet Compose(LoadedRuleset data, PlanFile file, Ui ui, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(file);

        var plan = file.ToPlan();
        var evaluation = PlanEvaluator.Evaluate(data.Rules, plan);
        var words = new FindingWords(ui, data.Content);
        var skills = PlanSummary.Skills(data.Content, plan);

        return new CharacterSheet
        {
            Language = ui.Code,
            Title = file.Name.Trim().Length > 0 ? file.Name.Trim() : ui.T("Naamloos personage", "Unnamed character"),
            Subtitle = plan.Mode == PlanMode.NewCharacter
                ? ui.T($"Nieuw personage · {ui.Number(evaluation.Xp)} XP", $"New character · {ui.Number(evaluation.Xp)} XP")
                : ui.T($"Vrij plannen · {ui.Number(evaluation.Xp)} XP", $"Free planning · {ui.Number(evaluation.Xp)} XP"),
            Rules = $"{ui.T("Regels", "Rules")} {data.Ruleset.Id}{(data.Ruleset.Provisional ? ui.T(" (voorlopig)", " (provisional)") : string.Empty)}",
            MadeOn = ui.T($"Gemaakt op {now.UtcDateTime.ToString("d-M-yyyy", CultureInfo.InvariantCulture)}", $"Made on {now.UtcDateTime.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}"),
            Facts = [.. Numbers(ui, evaluation), .. PlanSummary.Properties(ui, words, data.Content, plan, evaluation).Select(value => Fact(value.Label, value.Value))],
            Energy = [.. evaluation.Energy.Where(total => total.Kind == EnergyKind.Energy)
                .Select(total => Fact(words.EnergyType(total.Type), total.Amount.ToString(CultureInfo.InvariantCulture)))],
            Notes = Notes(evaluation, words),
            Skills = SkillTable(ui, data, plan, evaluation, words),
            SkillTexts = [.. skills.Select(skill => SkillText(ui, data, plan, words, skill))],
            Spells = [.. PlanSummary.Spells(data.Content, plan).Select(spell => Spell(ui, spell))],
            Recipes = [.. PlanSummary.Recipes(data.Content, plan).Select(recipe => Recipe(ui, recipe))],
            Loresheets = [.. skills.Where(skill => skill.Loresheet.Exists).SelectMany(skill => Loresheets(ui, skill))],
            XpPerEvent = XpTable(ui, data.Rules, evaluation.Xp),
            Labels = new SheetLabels
            {
                Overview = ui.T("Overzicht", "Overview"),
                Energy = ui.T("Energie per dag", "Energy per day"),
                Notes = ui.T("Let op", "Please note"),
                Skills = ui.T("Vaardigheden", "Skills"),
                Spells = ui.T("Spreuken en technieken", "Spells and techniques"),
                Recipes = ui.T("Recepten", "Recipes"),
                Loresheets = ui.T("Loresheets", "Loresheets"),
                XpPerEvent = ui.T("XP per evenement", "XP per event"),
                None = ui.T("Geen.", "None."),
                Page = ui.T("Pagina", "Page"),
                Of = ui.T("van", "of"),
            },
        };
    }

    private static IEnumerable<SheetFact> Numbers(Ui ui, PlanEvaluation evaluation)
    {
        yield return Fact("XP", ui.Number(evaluation.Xp));
        yield return Fact(ui.T("Uitgegeven", "Spent"), ui.Number(evaluation.Spent));
        yield return Fact(ui.T("Over", "Left"), ui.Number(evaluation.Remaining));
    }

    /// <summary>What breaks a rule or warns, plan-wide first, then per skill; what is only good to know stays in the table.</summary>
    private static List<string> Notes(PlanEvaluation evaluation, FindingWords words)
    {
        var notes = evaluation.Findings
            .Where(finding => finding.Skill.IsEmpty && finding.Severity >= FindingSeverity.Warning)
            .Select(words.Describe)
            .ToList();
        notes.AddRange(evaluation.Findings
            .Where(finding => !finding.Skill.IsEmpty && finding.Severity >= FindingSeverity.Warning)
            .Select(finding => $"{words.Name(finding.Skill)}: {words.Describe(finding)}"));
        return [.. notes.Distinct(StringComparer.Ordinal)];
    }

    private static SheetTable SkillTable(Ui ui, LoadedRuleset data, Plan plan, PlanEvaluation evaluation, FindingWords words)
    {
        var rows = plan.Planned.Select(pair =>
        {
            var cost = data.Rules.CostOf(pair.Key);
            var page = data.Content.TryGet(pair.Key, out var skill) && skill.Page > 0 ? skill.Page.ToString(CultureInfo.InvariantCulture) : string.Empty;
            var notes = string.Join("; ", evaluation.For(pair.Key).Select(words.Describe).Distinct(StringComparer.Ordinal));
            return (IReadOnlyList<string>)[words.Name(pair.Key), $"{pair.Value}×", ui.Number(cost * pair.Value), page, notes];
        }).ToList();

        rows.Add([ui.T("Totaal", "Total"), string.Empty, ui.Number(SkillRules.Spent(plan.Planned, data.Rules)), string.Empty, string.Empty]);

        return new SheetTable
        {
            Headers = [ui.T("Vaardigheid", "Skill"), ui.T("Aantal", "Times"), "XP", ui.T("Blz.", "P."), ui.T("Opmerkingen", "Notes")],
            Widths = [5.2, 1.1, 1, 0.9, 5.8],
            Numeric = [1, 2, 3],
            Rows = rows,
        };
    }

    private static SheetEntry SkillText(Ui ui, LoadedRuleset data, Plan plan, FindingWords words, SkillContent skill)
    {
        var facts = new List<SheetFact>
        {
            Fact(ui.T("Kosten", "Cost"), $"{data.Rules.CostOf(skill.Key)} XP"),
            Fact(ui.T("Aantal", "Times"), $"{plan.All[skill.Key]}×"),
            Fact(ui.T("Categorie", "Category"), ui.T(skill.Category)),
        };

        if (data.Rules.TryGet(skill.Key, out var rule) && rule.Requirements.Count > 0)
        {
            facts.Add(Fact(ui.T("Vereist", "Needs"), string.Join("; ", rule.Requirements.Select(words.Requirement))));
        }

        if (skill.Page > 0)
        {
            facts.Add(Fact(ui.T("Regelboek", "Rulebook"), $"{string.Join(" / ", skill.Book)}, {ui.T("blz.", "p.")} {skill.Page}"));
        }

        return new SheetEntry { Title = ui.T(skill.Name), Facts = facts, Text = ui.T(skill.Description) };
    }

    private static SheetEntry Spell(Ui ui, SpellContent spell)
    {
        var facts = new List<SheetFact>
        {
            Fact(ui.T("Energie", "Energy"), $"{ui.T(spell.EnergyType)} {spell.Cost}".Trim()),
            Fact(ui.T("Incantatie", "Incantation"), ui.T(spell.Incantation)),
            Fact(ui.T("Effect", "Effect"), ui.T(spell.Effect)),
            Fact(ui.T("Duur", "Duration"), ui.T(spell.Duration)),
        };

        if (spell.Page > 0)
        {
            facts.Add(Fact(ui.T("Regelboek", "Rulebook"), $"{ui.T("Spreuken en Technieken, blz.", "Spells and Techniques, p.")} {spell.Page}"));
        }

        return new SheetEntry { Title = ui.T(spell.Name), Facts = [.. facts.Where(fact => fact.Value.Length > 0)], Text = ui.T(spell.Description) };
    }

    private static SheetEntry Recipe(Ui ui, RecipeContent recipe)
    {
        var facts = new[]
        {
            Fact(ui.T("Effect", "Effect"), ui.T(recipe.Effect)),
            Fact(ui.T("Inspiratie", "Inspiration"), recipe.Inspiration),
            Fact(ui.T("Componenten", "Components"), ui.T(recipe.Components)),
        };

        return new SheetEntry { Title = ui.T(recipe.Name), Facts = [.. facts.Where(fact => fact.Value.Length > 0)], Text = string.Empty };
    }

    private static IEnumerable<SheetLink> Loresheets(Ui ui, SkillContent skill)
    {
        yield return new SheetLink { Title = ui.T(skill.Name), Url = skill.Loresheet.Url };
        if (skill.Loresheet.Extra.Length > 0)
        {
            yield return new SheetLink { Title = $"{ui.T(skill.Name)} ({ui.T("extra", "extra")})", Url = skill.Loresheet.Extra };
        }
    }

    /// <summary>The coming events: XP before each, what it awards (DESIGN.md §3.3), and XP after.</summary>
    private static SheetTable XpTable(Ui ui, RuleBook rules, decimal start)
    {
        var rows = new List<IReadOnlyList<string>>();
        var xp = start;
        for (var number = 1; number <= Events; number++)
        {
            var award = rules.Xp.AwardFor(xp);
            rows.Add([ui.T($"Evenement {number}", $"Event {number}"), ui.Number(xp), $"+{ui.Number(award)}", ui.Number(xp + award)]);
            xp += award;
        }

        return new SheetTable
        {
            Headers = [ui.T("Evenement", "Event"), ui.T("XP ervoor", "XP before"), ui.T("Erbij", "Gained"), ui.T("XP erna", "XP after")],
            Widths = [3, 2, 2, 2],
            Numeric = [1, 2, 3],
            Rows = rows,
        };
    }

    private static SheetFact Fact(string label, string value) => new () { Label = label, Value = value ?? string.Empty };
}
