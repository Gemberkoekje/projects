using CharacterCreator.core.Skills;

namespace CharacterCreator.core.Rules;

/// <summary>How much a <see cref="Finding"/> matters (DESIGN.md §6).</summary>
public enum FindingSeverity
{
    /// <summary>Not set.</summary>
    None = 0,

    /// <summary>Something to know, such as "needs a teacher". Never stops anything.</summary>
    Info,

    /// <summary>A rule VOID only warns about, such as "too much XP left over". Never stops anything.</summary>
    Warning,

    /// <summary>
    /// A rule VOID enforces is broken. Planning goes on regardless ("add anyway"), but a push into
    /// VOID (v2, v3) is refused while a problem remains.
    /// </summary>
    Problem,
}

/// <summary>
/// One fact the engine found about a plan. Facts, not sentences: the build screen and the PDF word
/// them in Dutch or English (DESIGN.md §6).
/// </summary>
/// <remarks>Immutable and thread-safe.</remarks>
public abstract record Finding
{
    /// <summary>The skill the finding is about; <see cref="SkillKey.Empty"/> for the plan as a whole.</summary>
    public SkillKey Skill { get; init; }

    /// <summary>How much it matters.</summary>
    public abstract FindingSeverity Severity { get; }
}

/// <summary>A prerequisite group of <see cref="Finding.Skill"/> does not hold.</summary>
public sealed record MissingPrerequisite : Finding
{
    /// <summary>The group that does not hold.</summary>
    required public RequirementGroup Group { get; init; }

    /// <inheritdoc />
    public override FindingSeverity Severity => FindingSeverity.Problem;
}

/// <summary>The plan needs more XP than there is.</summary>
public sealed record NotEnoughXp : Finding
{
    /// <summary>The XP the purchases need.</summary>
    required public decimal Needed { get; init; }

    /// <summary>The XP there is to spend on them.</summary>
    required public decimal Available { get; init; }

    /// <inheritdoc />
    public override FindingSeverity Severity => FindingSeverity.Problem;
}

/// <summary>A skill is bought more often than its <c>times_max</c> allows.</summary>
public sealed record AboveMaximum : Finding
{
    /// <summary>How often it is bought.</summary>
    required public int Times { get; init; }

    /// <summary>How often it may be.</summary>
    required public int Maximum { get; init; }

    /// <inheritdoc />
    public override FindingSeverity Severity => FindingSeverity.Problem;
}

/// <summary>A new character buys a skill more often than its <c>base_max</c> allows at creation.</summary>
public sealed record AboveCreationMaximum : Finding
{
    /// <summary>How often it is bought.</summary>
    required public int Times { get; init; }

    /// <summary>How often it may be at creation.</summary>
    required public int Maximum { get; init; }

    /// <inheritdoc />
    public override FindingSeverity Severity => FindingSeverity.Problem;
}

/// <summary>A new character buys an extra skill, which can only be learned later, through a teacher.</summary>
public sealed record NotAtCreation : Finding
{
    /// <inheritdoc />
    public override FindingSeverity Severity => FindingSeverity.Problem;
}

/// <summary>VOID has retired the skill; it cannot be bought any more.</summary>
public sealed record DeprecatedSkill : Finding
{
    /// <inheritdoc />
    public override FindingSeverity Severity => FindingSeverity.Problem;
}

/// <summary>The rule book does not know the skill.</summary>
public sealed record UnknownSkill : Finding
{
    /// <summary>
    /// Whether the character already has it: then it is only something to know (an SL-only or rank
    /// skill shown by its VOID name). A planned skill the book does not know is a problem.
    /// </summary>
    required public bool Held { get; init; }

    /// <inheritdoc />
    public override FindingSeverity Severity => Held ? FindingSeverity.Info : FindingSeverity.Problem;
}

/// <summary>An extra skill bought the first time: it goes through a teacher.</summary>
public sealed record NeedsTeacher : Finding
{
    /// <inheritdoc />
    public override FindingSeverity Severity => FindingSeverity.Info;
}

/// <summary>
/// A teaching that the whole XP available does not pay off yet: it starts, and the rest is paid
/// from the XP of later events (the teaching card's progress).
/// </summary>
public sealed record TeachingNotPaidOff : Finding
{
    /// <summary>The XP still to pay after this event.</summary>
    required public decimal Owed { get; init; }

    /// <inheritdoc />
    public override FindingSeverity Severity => FindingSeverity.Info;
}

/// <summary>
/// More than one extra skill through a teacher at once. The rulebook allows one per event, so the
/// checkout would hold the rest back.
/// </summary>
public sealed record MoreThanOneTeaching : Finding
{
    /// <summary>The extra skills planned for a first purchase.</summary>
    required public IReadOnlyList<SkillKey> Skills { get; init; }

    /// <inheritdoc />
    public override FindingSeverity Severity => FindingSeverity.Warning;
}

/// <summary>A new character leaves more XP unspent than the rules keep; the rest is lost.</summary>
public sealed record TooMuchLeftOver : Finding
{
    /// <summary>The XP left over.</summary>
    required public decimal Left { get; init; }

    /// <summary>The most that may be left over.</summary>
    required public decimal Limit { get; init; }

    /// <inheritdoc />
    public override FindingSeverity Severity => FindingSeverity.Warning;
}
