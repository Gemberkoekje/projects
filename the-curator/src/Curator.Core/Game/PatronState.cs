using Curator.Core.Content;

namespace Curator.Core.Game;

/// <summary>What the game knows about a patron the curator has met.</summary>
public sealed class PatronState
{
    private readonly HashSet<string> usedVisits = new(StringComparer.Ordinal);
    private readonly HashSet<int> usedSteps = [];
    private readonly List<string> loanIds = [];

    internal PatronState(string id) => Id = id;

    public string Id { get; }

    /// <summary>Trust, 0 (Guarded) to 3 (Confiding).</summary>
    public int Trust { get; internal set; }

    public int FirstVisitDay { get; internal set; }

    /// <summary>The sequence number of their first visit, for ordering patrons as they were met.</summary>
    public long FirstMetSequence { get; internal set; }

    public int VisitCount { get; internal set; }

    public int LastVisitDay { get; internal set; }

    /// <summary>How this patron's last visit ended.</summary>
    public DecisionKind LastDecision { get; internal set; }

    /// <summary>The true outcome category of this patron's last lend.</summary>
    public OutcomeCategory LastLendCategory { get; internal set; }

    public IReadOnlySet<string> UsedVisits => usedVisits;

    public IReadOnlySet<int> UsedSteps => usedSteps;

    /// <summary>In-game loans, oldest first.</summary>
    public IReadOnlyList<string> LoanIds => loanIds;

    internal void UseVisit(string visitId, int step)
    {
        usedVisits.Add(visitId);
        usedSteps.Add(step);
    }

    internal void AddLoan(string loanId) => loanIds.Add(loanId);
}
