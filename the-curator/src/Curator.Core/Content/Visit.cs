using System.Text.Json.Serialization;

namespace Curator.Core.Content;

/// <summary>One visit a patron can make (CONTENT_GUIDE §6).</summary>
public sealed record Visit
{
    public required string Id { get; init; }

    /// <summary>Visits sharing a step are variants; the first that qualifies is used.</summary>
    public required int Step { get; init; }

    public int EarliestDay { get; init; } = 1;

    public int MinDaysAfterPrevious { get; init; }

    public Condition When { get; init; } = Condition.Always;

    /// <summary>Only lending is allowed.</summary>
    public bool Forced { get; init; }

    /// <summary>Starting patience; the balance default when absent.</summary>
    public int? Patience { get; init; }

    /// <summary>The greeting: a plain string in JSON, or variants of which the first whose condition holds is used.</summary>
    [JsonConverter(typeof(GreetingListConverter))]
    public required IReadOnlyList<GreetingVariant> Greeting { get; init; }

    public required VisitRequest Request { get; init; }

    public required VisitGoal Goal { get; init; }

    public IReadOnlyList<Temptation> Temptations { get; init; } = [];

    public VisitAlternatives Alternatives { get; init; } = VisitAlternatives.Default;

    public required ReadThoughtsSpec ReadThoughts { get; init; }

    /// <summary>Answers to standard questions: question id → trust level → answer.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> Answers { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<int, string>>();

    /// <summary>Specific questions that investigation unlocks.</summary>
    public IReadOnlyList<SpecificQuestion> Questions { get; init; } = [];

    public VisitDecisions Decisions { get; init; } = VisitDecisions.Empty;

    /// <summary>Days until a book lent here is due back; the balance default when absent.</summary>
    public int? LoanDays { get; init; }

    /// <summary>Outcomes by key: good, unhelpful, harm, harmMisuse, harmAccident, mixed, mixedMisuse, mixedAccident, declined, walkedOut.</summary>
    public IReadOnlyDictionary<string, Outcome> Outcomes { get; init; } = new Dictionary<string, Outcome>();

    /// <summary>Hand-written outcomes for particular books, each with its own category.</summary>
    public IReadOnlyDictionary<string, Outcome> Overrides { get; init; } = new Dictionary<string, Outcome>();
}
