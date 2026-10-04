namespace SpaceTraders.Application.Construction;

/// <summary>
/// The supplies a construction site refused, by ship and material, in memory (PLAN.md slice 6.6): the construction plan
/// gives a ship that holds what a site still needs a trip to supply it on every tick, and a refusal that repeats would cost
/// a call each time. A restart forgets them.
/// </summary>
/// <remarks>A singleton; thread-safe.</remarks>
public sealed class ConstructionRetries
{
    /// <summary>How long a refused supply waits before the material is offered again.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromMinutes(10);

    private readonly Lock _gate = new();
    private readonly Dictionary<(string Ship, string Good), DateTimeOffset> _refusedAt = [];

    /// <summary>Whether the ship may supply the material now: never refused, or refused at least <see cref="Wait"/> ago.</summary>
    /// <param name="shipSymbol">The ship.</param>
    /// <param name="tradeSymbol">The material.</param>
    /// <param name="now">The time.</param>
    /// <returns>True when it may be tried.</returns>
    public bool MayTry(string shipSymbol, string tradeSymbol, DateTimeOffset now)
    {
        lock (_gate)
        {
            return !_refusedAt.TryGetValue(Key(shipSymbol, tradeSymbol), out var at) || now - at >= Wait;
        }
    }

    /// <summary>Records a refused supply.</summary>
    /// <param name="shipSymbol">The ship.</param>
    /// <param name="tradeSymbol">The material.</param>
    /// <param name="now">When the API refused it.</param>
    public void Refused(string shipSymbol, string tradeSymbol, DateTimeOffset now)
    {
        lock (_gate)
        {
            _refusedAt[Key(shipSymbol, tradeSymbol)] = now;
        }
    }

    private static (string Ship, string Good) Key(string shipSymbol, string tradeSymbol)
        => (shipSymbol.ToUpperInvariant(), tradeSymbol.ToUpperInvariant());
}
