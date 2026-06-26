using System;
using System.Collections.Generic;

namespace ByGalacticAccord.Engine.Domain;

/// <summary>The flavour of a reputation context (§4.3). Reputation is a map of where you can operate, not one bar.</summary>
public enum ReputationContextKind
{
    /// <summary>Game-wide standing under the Accord standard (§2.3); gates core/mid contracts.</summary>
    AccordStanding,
    Region,
    GoodCategory,
    Counterparty,
}

/// <summary>A key identifying one axis of an actor's reputation.</summary>
public readonly record struct ReputationContext(
    ReputationContextKind Kind,
    string? Region = null,
    GoodCategory? Category = null,
    ActorId? Counterparty = null)
{
    public static ReputationContext Accord { get; } = new(ReputationContextKind.AccordStanding);

    public static ReputationContext ForRegion(string region) => new(ReputationContextKind.Region, Region: region);

    public static ReputationContext ForGood(GoodCategory category) => new(ReputationContextKind.GoodCategory, Category: category);

    public static ReputationContext With(ActorId counterparty) => new(ReputationContextKind.Counterparty, Counterparty: counterparty);

    public override string ToString() => Kind switch
    {
        ReputationContextKind.AccordStanding => "Accord standing",
        ReputationContextKind.Region => $"Region:{Region}",
        ReputationContextKind.GoodCategory => $"Goods:{Category}",
        ReputationContextKind.Counterparty => $"With:{Counterparty}",
        _ => "?",
    };
}

/// <summary>
/// Contextual, bounded reputation that decays toward neutral (§4.3). Scores run -1..1, where
/// 0 is "unknown / neutral". This is the sole frontier enforcement mechanism and the trust gate.
/// </summary>
public sealed class ReputationLedger
{
    private readonly Dictionary<(ActorId Actor, ReputationContext Context), decimal> _scores = new();

    public decimal Get(ActorId actor, ReputationContext context) =>
        _scores.TryGetValue((actor, context), out decimal score) ? score : 0m;

    public void Adjust(ActorId actor, ReputationContext context, decimal delta)
    {
        decimal current = Get(actor, context);
        _scores[(actor, context)] = Math.Clamp(current + delta, -1m, 1m);
    }

    /// <summary>Slow regression toward neutral so old sins fade and a wronged reputation can be rebuilt.</summary>
    public void DecayTowardNeutral(decimal rate)
    {
        // Snapshot keys so we can mutate while iterating deterministically.
        var keys = new List<(ActorId, ReputationContext)>(_scores.Keys);
        foreach (var key in keys)
        {
            decimal value = _scores[key];
            decimal moved = value * (1m - rate);
            _scores[key] = Math.Abs(moved) < 0.0005m ? 0m : moved;
        }
    }

    public IReadOnlyDictionary<(ActorId Actor, ReputationContext Context), decimal> Snapshot => _scores;
}
