namespace SpaceTraders.Application.Trading;

/// <summary>
/// The judgement per good that got furthest through the checks (<see cref="TradeRouteJudgement.FurthestPerGood"/>, D76), kept
/// as the judgements of one ship after another come in. A pass holds one judgement per good this way, not every route its
/// ships checked: with predictions across every market (D132) those came to about 1.4 GB a pass (B88).
/// </summary>
/// <remarks>Not thread-safe: one pass adds to it at a time.</remarks>
public sealed class FurthestJudgements
{
    private readonly Dictionary<string, TradeRouteJudgement> _perGood = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The goods kept, one judgement each.</summary>
    public int Count => _perGood.Count;

    /// <summary>Keeps, for each good, whichever of the kept judgement and <paramref name="judgements"/> got furthest.</summary>
    /// <param name="judgements">One ship's judgements, or several ships'.</param>
    public void Add(IEnumerable<TradeRouteJudgement> judgements)
    {
        ArgumentNullException.ThrowIfNull(judgements);

        foreach (var judgement in judgements)
        {
            var good = judgement.Route.TradeSymbol;
            if (!_perGood.TryGetValue(good, out var kept) || TradeRouteJudgement.FurthestFirst.Compare(judgement, kept) < 0)
            {
                _perGood[good] = judgement;
            }
        }
    }

    /// <summary>The kept judgements in <see cref="TradeRouteJudgement.FurthestPerGood"/>'s order: the furthest first, then by good.</summary>
    /// <returns>One judgement per good.</returns>
    public IReadOnlyList<TradeRouteJudgement> ToList() => TradeRouteJudgement.FurthestPerGood(_perGood.Values);
}
