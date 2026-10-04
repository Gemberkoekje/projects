using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Naming;

/// <summary>
/// The fleet's names as last worked out (slice 2.14, D72, <see cref="ShipNames"/>), for what can't read the fleet itself:
/// every log line about a ship carries its name beside its symbol (the API's <c>ShipNameEnricher</c>).
/// </summary>
public interface IShipNameBook
{
    /// <summary>
    /// Works out the names of the fleet for the active agent's reset, and keeps them. Whatever reads the whole fleet tells it:
    /// the metrics every 10 seconds, startup recovery, a ship purchase and the ship list of the internal API.
    /// </summary>
    /// <param name="fleet">Every ship of the agent: a ship's number counts the ships of its type before it.</param>
    /// <returns>Each ship's name, by symbol; none before agent bootstrap has picked the agent.</returns>
    IReadOnlyDictionary<string, string> Know(IReadOnlyCollection<ShipModel> fleet);

    /// <summary>A ship's name, as last worked out.</summary>
    /// <param name="shipSymbol">The ship's symbol, such as <c>SPECTER-4</c>.</param>
    /// <returns>Its name, such as <c>SPUTNIK-2</c>; empty for a ship the book hasn't been told of.</returns>
    string NameOf(string shipSymbol);
}

/// <summary>
/// The fleet's names, in memory. A name follows from the fleet and the reset date alone, so telling it the fleet again
/// changes nothing but for a ship that joined since; a fleet read before a purchase, told after it, keeps the new ship's
/// name. Read from any thread: each <see cref="Know"/> swaps in a new set.
/// </summary>
/// <param name="reset">The active agent's reset, which picks each type's name.</param>
public sealed class ShipNameBook(IActiveReset reset) : IShipNameBook
{
    private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();

    private readonly Lock _gate = new();
    private volatile IReadOnlyDictionary<string, string> _names = None;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Know(IReadOnlyCollection<ShipModel> fleet)
    {
        ArgumentNullException.ThrowIfNull(fleet);

        // Before bootstrap there is no reset to pick the names by, and no ship of the agent's yet.
        var resetDate = reset.ResetDate;
        if (resetDate.Length == 0)
        {
            return None;
        }

        var names = ShipNames.For(fleet, resetDate);
        lock (_gate)
        {
            var known = new Dictionary<string, string>(_names, StringComparer.OrdinalIgnoreCase);
            foreach (var (ship, name) in names)
            {
                known[ship] = name;
            }

            _names = known;
        }

        return names;
    }

    /// <inheritdoc />
    public string NameOf(string shipSymbol)
    {
        ArgumentNullException.ThrowIfNull(shipSymbol);
        return _names.TryGetValue(shipSymbol, out var name) ? name : string.Empty;
    }
}
