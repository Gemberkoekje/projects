namespace Curator.Core.Game;

/// <summary>
/// The visit at the counter. After the decision it stays until the next patron is called or the
/// day ends, so the "note it" moments remain open while the patron says goodbye.
/// </summary>
public sealed class VisitState
{
    private readonly List<string> gossip = [];
    private readonly List<AskedQuestion> asked = [];
    private readonly HashSet<string> unlocks = new(StringComparer.Ordinal);
    private readonly List<string> offersRefused = [];
    private readonly List<string> returnTexts = [];

    internal VisitState(string visitId, string patronId) => (VisitId, PatronId) = (visitId, patronId);

    /// <summary>A fresh "nobody at the counter" placeholder; each state gets its own.</summary>
    /// <returns>An empty, decided visit.</returns>
    internal static VisitState Nobody() => new("", "") { Decided = true };

    public string VisitId { get; }

    public string PatronId { get; }

    public int SlotIndex { get; internal set; }

    public string SlotId { get; internal set; } = "";

    public int Day { get; internal set; }

    public int Patience { get; internal set; }

    public bool Forced { get; internal set; }

    public string RequestedBookId { get; internal set; } = "";

    public string Topic { get; internal set; } = "";

    public bool FirstVisit { get; internal set; }

    public string Greeting { get; internal set; } = "";

    public string RequestText { get; internal set; } = "";

    /// <summary>Gossip told as the visit opened.</summary>
    public IReadOnlyList<string> Gossip => gossip;

    /// <summary>What this patron said about their earlier loans as they arrived (their 'return' outcomes).</summary>
    public IReadOnlyList<string> ReturnTexts => returnTexts;

    public IReadOnlyList<AskedQuestion> Asked => asked;

    public bool ReadThoughtsCast { get; internal set; }

    /// <summary>What Read Thoughts surfaced, or empty.</summary>
    public string Fragment { get; internal set; } = "";

    /// <summary>Whether a warded mind noticed Read Thoughts.</summary>
    public bool Noticed { get; internal set; }

    public IReadOnlySet<string> Unlocks => unlocks;

    public bool ThoughtNoted { get; internal set; }

    /// <summary>Whether a question, Read Thoughts or Identify happened before the decision.</summary>
    public bool Investigated { get; internal set; }

    public IReadOnlyList<string> OffersRefused => offersRefused;

    /// <summary>The patron's most recent line: an answer, a refusal or the exit line.</summary>
    public string LastLine { get; internal set; } = "";

    public bool Decided { get; internal set; }

    public Content.DecisionKind Decision { get; internal set; }

    public string ExitLine { get; internal set; } = "";

    /// <summary>The loan made at this visit, or empty.</summary>
    public string LoanId { get; internal set; } = "";

    /// <summary>Whether the patron is still deciding: commands that need a patron are allowed.</summary>
    public bool InProgress => !Decided;

    internal void AddGossip(string text) => gossip.Add(text);

    internal void AddReturnText(string text) => returnTexts.Add(text);

    internal void AddAsked(AskedQuestion question) => asked.Add(question);

    internal void AddUnlocks(IEnumerable<string> ids) => unlocks.UnionWith(ids);

    internal void AddRefusal(string bookId) => offersRefused.Add(bookId);
}
