using CharacterCreator.core.Rules;
using CharacterCreator.core.Rulesets;
using CharacterCreator.core.Skills;

namespace CharacterCreator.application.Rulesets;

/// <summary>
/// What an import from VOID found, before anything is stored: the dry run of DESIGN.md §4.2.
/// Shown to SL and EC on <c>Beheer → Rulesets</c> only; it may name SL-only skills.
/// </summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record ImportReport
{
    /// <summary>Where VOID's skills came from.</summary>
    required public string Source { get; init; }

    /// <summary>When they were read.</summary>
    required public DateTimeOffset TakenAt { get; init; }

    /// <summary>Whether they came from the synthesized offline snapshot.</summary>
    required public bool Synthesized { get; init; }

    /// <summary>How many skills the picker would offer: content and VOID rules both present.</summary>
    required public int Offered { get; init; }

    /// <summary>The rules for a new character the ruleset would hold.</summary>
    required public CreationRules Creation { get; init; }

    /// <summary>Whether VOID gave those rules, rather than the Character Creator assuming its own.</summary>
    required public bool CreationFromVoid { get; init; }

    /// <summary>Book skills VOID has no skill for, by the content's VOID name; not offered.</summary>
    required public IReadOnlyList<string> NotInVoid { get; init; }

    /// <summary>VOID skills the book does not name: the SL-only ones, and anything new in VOID.</summary>
    required public IReadOnlyList<string> NotInBook { get; init; }

    /// <summary>VOID names used by more than one VOID skill; the first one is used.</summary>
    required public IReadOnlyList<string> DuplicateVoidNames { get; init; }

    /// <summary>Skills whose VOID cost differs from what the book prints. VOID's cost is used.</summary>
    required public IReadOnlyList<CostDifference> CostDifferences { get; init; }

    /// <summary>Book skills VOID has retired; they are imported, but never offered.</summary>
    required public IReadOnlyList<SkillKey> DeprecatedOffered { get; init; }

    /// <summary>Prerequisites that name a skill outside the book; the ruleset refers to it as <c>hidden-{id}</c>.</summary>
    required public IReadOnlyList<HiddenPrerequisite> HiddenPrerequisites { get; init; }

    /// <summary>What differs from the ruleset published now; empty on the first publish.</summary>
    required public IReadOnlyList<RuleChange> Changes { get; init; }

    /// <summary>The energy and imbue types VOID's skills name.</summary>
    required public IReadOnlyList<string> EnergyTypes { get; init; }

    /// <summary>Why the rules cannot be published, if they cannot.</summary>
    required public IReadOnlyList<string> Errors { get; init; }

    /// <summary>Whether the ruleset can be published.</summary>
    public bool CanPublish => Errors.Count == 0;
}

/// <summary>A skill whose VOID cost differs from the book's.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record CostDifference
{
    /// <summary>The skill.</summary>
    required public SkillKey Skill { get; init; }

    /// <summary>VOID's cost: the one that counts.</summary>
    required public int VoidCost { get; init; }

    /// <summary>What the book prints.</summary>
    required public int BookCost { get; init; }
}

/// <summary>A book skill's prerequisite on a skill outside the book.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record HiddenPrerequisite
{
    /// <summary>The book skill.</summary>
    required public SkillKey Skill { get; init; }

    /// <summary>The skill outside the book, by VOID's name (SL eyes only).</summary>
    required public string VoidName { get; init; }
}

/// <summary>What changed in one skill's rules since the published ruleset.</summary>
public enum RuleChangeKind
{
    /// <summary>Not set.</summary>
    None = 0,

    /// <summary>Offered now, not before.</summary>
    Added,

    /// <summary>Offered before, not now.</summary>
    Removed,

    /// <summary>The cost.</summary>
    Cost,

    /// <summary>Purchases at creation.</summary>
    BaseMax,

    /// <summary>Purchases in total.</summary>
    TimesMax,

    /// <summary>Retired or brought back.</summary>
    Deprecated,

    /// <summary>The prerequisites.</summary>
    Requirements,

    /// <summary>Energy or imbue cap.</summary>
    Energy,
}

/// <summary>One change in a skill's rules since the published ruleset.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record RuleChange
{
    /// <summary>The skill.</summary>
    required public SkillKey Skill { get; init; }

    /// <summary>What changed.</summary>
    required public RuleChangeKind Kind { get; init; }

    /// <summary>The old value, as text; empty for an added skill.</summary>
    required public string Before { get; init; }

    /// <summary>The new value, as text; empty for a removed skill.</summary>
    required public string After { get; init; }
}

/// <summary>An import's outcome: the ruleset it would publish, and the report.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record ImportOutcome
{
    /// <summary>The ruleset that publishing would store.</summary>
    required public PublishedRuleset Ruleset { get; init; }

    /// <summary>What the import found.</summary>
    required public ImportReport Report { get; init; }
}
