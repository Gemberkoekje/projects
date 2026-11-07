using CharacterCreator.core.Content;
using CharacterCreator.core.Rules;
using CharacterCreator.core.Skills;

namespace CharacterCreator.WebApp.Client.Words;

/// <summary>
/// The engine's findings in words (DESIGN.md §6: the engine returns facts, the UI words them), in
/// Dutch or English. A skill outside the book is never named: it is "a skill outside the book".
/// </summary>
/// <remarks>Immutable and thread-safe.</remarks>
/// <param name="ui">The language.</param>
/// <param name="content">The content, for skill names.</param>
public sealed class FindingWords(Ui ui, ContentBook content)
{
    private readonly ContentBook content = content ?? throw new ArgumentNullException(nameof(content));

    /// <summary>A skill's name in the screen's language.</summary>
    /// <param name="skill">The skill.</param>
    /// <returns>Its name; for a skill outside the book or the content, a description instead.</returns>
    public string Name(SkillKey skill)
    {
        if (content.TryGet(skill, out var known))
        {
            return ui.T(known.Name);
        }

        return skill.ToString().StartsWith("hidden-", StringComparison.Ordinal)
            ? ui.T("een vaardigheid buiten het boek", "a skill outside the book")
            : skill.ToString();
    }

    /// <summary>What a prerequisite group asks for, without a verb: "Harnas I", "one of: …", "4 XP in: …".</summary>
    /// <param name="group">The group.</param>
    /// <returns>The words.</returns>
    public string Requirement(RequirementGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var names = string.Join(", ", group.Skills.Select(Name).Distinct());
        var times = group.Amount > 1 ? $" {group.Amount}×" : string.Empty;
        return group.Kind switch
        {
            RequirementKind.Any when group.Skills.Count == 1 => $"{names}{times}",
            RequirementKind.Any => $"{ui.T("één van", "one of")}: {names}{times}",
            RequirementKind.Points => $"{ui.Number(group.Amount)} XP {ui.T("in", "in")}: {names}",
            RequirementKind.Extra when group.Amount == 1 => ui.T("een extra vaardigheid", "an extra skill"),
            RequirementKind.Extra => ui.T($"{group.Amount} extra vaardigheden", $"{group.Amount} extra skills"),
            _ => names,
        };
    }

    /// <summary>A finding in words.</summary>
    /// <param name="finding">The finding.</param>
    /// <returns>The words, such as "needs Harnas II".</returns>
    public string Describe(Finding finding) => finding switch
    {
        MissingPrerequisite missing => $"{ui.T("vereist", "needs")} {Requirement(missing.Group)}",
        NotEnoughXp xp => ui.T(
            $"niet genoeg XP: {ui.Number(xp.Needed)} nodig, {ui.Number(xp.Available)} over",
            $"not enough XP: needs {ui.Number(xp.Needed)}, {ui.Number(xp.Available)} left"),
        AboveCreationMaximum above => ui.T($"bij creatie maximaal {above.Maximum}×", $"at most {above.Maximum}× at creation"),
        AboveMaximum above => ui.T($"maximaal {above.Maximum}×", $"at most {above.Maximum}×"),
        NotAtCreation => ui.T("extra vaardigheid: niet bij creatie", "an extra skill: not at creation"),
        DeprecatedSkill => ui.T("afgeschaft in VOID", "retired in VOID"),
        UnknownSkill => ui.T("staat niet in deze regels", "not in these rules"),
        NeedsTeacher => ui.T("vereist een leraar", "needs a teacher"),
        TeachingNotPaidOff owed => ui.T($"nog {ui.Number(owed.Owed)} XP af te betalen", $"{ui.Number(owed.Owed)} XP still to pay off"),
        MoreThanOneTeaching teachings => ui.T(
            $"één leraar per evenement: {string.Join(", ", teachings.Skills.Select(Name))}",
            $"one teacher per event: {string.Join(", ", teachings.Skills.Select(Name))}"),
        TooMuchLeftOver left => ui.T(
            $"{ui.Number(left.Left)} XP over; alles boven {ui.Number(left.Limit)} vervalt",
            $"{ui.Number(left.Left)} XP left over; everything above {ui.Number(left.Limit)} is lost"),
        null => string.Empty,
        _ => finding.GetType().Name,
    };

    /// <summary>What the picker says about one more purchase.</summary>
    /// <param name="option">The skill's option.</param>
    /// <returns>"can buy", or the reasons in words.</returns>
    public string State(SkillOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        return option.Reasons.Count == 0
            ? ui.T("kan", "can buy")
            : string.Join("; ", option.Reasons.Select(Describe).Distinct());
    }

    /// <summary>An energy or imbue type, as VOID names it, in the screen's language.</summary>
    /// <param name="type">VOID's name, such as <c>Elemental ritual</c>.</param>
    /// <returns>The words.</returns>
    public string EnergyType(string type) => (type ?? string.Empty).ToUpperInvariant() switch
    {
        "ELEMENTAL" => ui.T("Elementair", "Elemental"),
        "SPIRITUAL" => ui.T("Spiritueel", "Spiritual"),
        "WILLPOWER" => ui.T("Wilskracht", "Willpower"),
        "INSPIRATION" => ui.T("Inspiratie", "Inspiration"),
        "ELEMENTAL RITUAL" => ui.T("Elementair ritueel", "Elemental ritual"),
        "SPIRITUAL RITUAL" => ui.T("Spiritueel ritueel", "Spiritual ritual"),
        _ => type ?? string.Empty,
    };
}
