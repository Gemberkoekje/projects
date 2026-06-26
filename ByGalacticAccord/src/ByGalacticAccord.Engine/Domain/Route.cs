namespace ByGalacticAccord.Engine.Domain;

/// <summary>A static danger band for colour-coding a route on the map (§5.7).</summary>
public enum DangerBand
{
    Green,
    Amber,
    Red,
}

/// <summary>
/// A weighted edge in the location graph (§4.7.2). Danger here is the *geographic*,
/// slow-changing baseline the Transit Incident table reads (§6.3.1) — the predation
/// overlay that Slice 3 adds is a separate channel, deliberately not folded into this number.
/// </summary>
public sealed class Route
{
    public Route(LocationId a, LocationId b, long distanceTicks, decimal dangerRating, AccordReach lawCoverage)
    {
        A = a;
        B = b;
        DistanceTicks = distanceTicks;
        DangerRating = dangerRating;
        LawCoverage = lawCoverage;
    }

    public LocationId A { get; }

    public LocationId B { get; }

    /// <summary>Travel time at standard speed; scaled by a ship's speed multiplier when committed.</summary>
    public long DistanceTicks { get; }

    /// <summary>Static geographic hazard, 0..1. Drives Transit Incidents and (later) insurance pricing.</summary>
    public decimal DangerRating { get; }

    public AccordReach LawCoverage { get; }

    public DangerBand Band => DangerRating switch
    {
        < 0.12m => DangerBand.Green,
        < 0.30m => DangerBand.Amber,
        _ => DangerBand.Red,
    };

    public bool Connects(LocationId x, LocationId y) => (A == x && B == y) || (A == y && B == x);

    public LocationId Other(LocationId from) => from == A ? B : A;
}

/// <summary>
/// A multi-hop haul plan between two locations. In the discrete-event model a multi-leg journey is
/// still just one departure→arrival (§4.7.3); the legs collapse into a total travel time, and the
/// single most dangerous leg drives the Transit Incident check (§6.3). <see cref="Path"/> is the
/// ordered node sequence (origin..destination inclusive) — used by the UI to animate ships along the graph.
/// </summary>
public sealed record RoutePlan(long DistanceTicks, decimal MaxDanger, int Hops, System.Collections.Generic.IReadOnlyList<LocationId> Path)
{
    public DangerBand Band => MaxDanger switch
    {
        < 0.12m => DangerBand.Green,
        < 0.30m => DangerBand.Amber,
        _ => DangerBand.Red,
    };
}
