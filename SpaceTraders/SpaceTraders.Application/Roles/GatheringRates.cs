using System.Text.Json.Serialization;

namespace SpaceTraders.Application.Roles;

/// <summary>Whether a ship fills its hold by extracting at an asteroid or by siphoning at a gas giant.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GatheringKind>))]
public enum GatheringKind
{
    /// <summary>Extracting with a mining laser.</summary>
    Mining = 0,

    /// <summary>Siphoning with a gas siphon.</summary>
    Siphoning = 1,
}

/// <summary>How fast a ship fills its hold: the units one extraction (or siphon) yields, and the seconds until the next.</summary>
public sealed record GatheringRate
{
    /// <summary>Creates a rate.</summary>
    /// <param name="UnitsPerAction">Units one extraction or siphon yields, on average.</param>
    /// <param name="SecondsPerAction">Seconds of cooldown after one, on average.</param>
    /// <param name="Observed">Whether this ship's own extractions (or another ship's of the same kind) gave it; else the default.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public GatheringRate(double UnitsPerAction, double SecondsPerAction, bool Observed)
    {
        this.UnitsPerAction = UnitsPerAction;
        this.SecondsPerAction = SecondsPerAction;
        this.Observed = Observed;
    }

    /// <summary>Units one extraction or siphon yields, on average.</summary>
    public required double UnitsPerAction { get; init; }

    /// <summary>Seconds of cooldown after one, on average.</summary>
    public required double SecondsPerAction { get; init; }

    /// <summary>Whether extractions gave it; false for the default.</summary>
    public required bool Observed { get; init; }
}

/// <summary>
/// How fast each ship fills its hold, as its extractions and siphons showed (PLAN.md slice 6.9): the role board
/// reckons how long a mining or siphon trip takes from it. The game publishes no yields, so until a ship has
/// extracted, the average of the other ships of the kind stands in, and before any, a default.
/// </summary>
public interface IGatheringRates
{
    /// <summary>Records one extraction or siphon.</summary>
    /// <param name="shipSymbol">The ship.</param>
    /// <param name="kind">Extracting or siphoning.</param>
    /// <param name="units">The units it yielded.</param>
    /// <param name="cooldownSeconds">The cooldown it started; 0 when the answer gave none.</param>
    void Record(string shipSymbol, GatheringKind kind, int units, int cooldownSeconds);

    /// <summary>Takes a rate the plan state kept from before a restart, for a ship with no extraction since.</summary>
    /// <param name="shipSymbol">The ship.</param>
    /// <param name="kind">Extracting or siphoning.</param>
    /// <param name="rate">The kept rate; ignored unless it was observed.</param>
    void Seed(string shipSymbol, GatheringKind kind, GatheringRate rate);

    /// <summary>The ship's rate: its own extractions, else the other ships' of the kind, else the default.</summary>
    /// <param name="shipSymbol">The ship.</param>
    /// <param name="kind">Extracting or siphoning.</param>
    /// <returns>The rate.</returns>
    GatheringRate For(string shipSymbol, GatheringKind kind);
}

/// <inheritdoc />
/// <remarks>A singleton, in memory; thread-safe. Each ship keeps its last <see cref="Window"/> extractions of a kind.</remarks>
public sealed class GatheringRates : IGatheringRates
{
    /// <summary>How many of a ship's latest extractions (or siphons) count.</summary>
    internal const int Window = 10;

    /// <summary>
    /// The rate before any ship has extracted: 3 units every 70 seconds. The soak test (1.14) saw a drone extract every
    /// 71 seconds; the yield is a cautious guess, below the strength 5 of the command ship's Mining Laser II.
    /// </summary>
    internal static readonly GatheringRate Default = new(3, 70, Observed: false);

    private readonly Lock _gate = new();
    private readonly Dictionary<(string Ship, GatheringKind Kind), Queue<(int Units, int Seconds)>> _observed = [];

    /// <inheritdoc />
    public void Record(string shipSymbol, GatheringKind kind, int units, int cooldownSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shipSymbol);
        if (units < 0)
        {
            return;
        }

        lock (_gate)
        {
            var key = (shipSymbol.ToUpperInvariant(), kind);
            if (!_observed.TryGetValue(key, out var window))
            {
                window = new Queue<(int Units, int Seconds)>();
                _observed[key] = window;
            }

            window.Enqueue((units, Math.Max(0, cooldownSeconds)));
            while (window.Count > Window)
            {
                window.Dequeue();
            }
        }
    }

    /// <inheritdoc />
    public void Seed(string shipSymbol, GatheringKind kind, GatheringRate rate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shipSymbol);
        ArgumentNullException.ThrowIfNull(rate);
        if (!rate.Observed || rate.UnitsPerAction <= 0)
        {
            return;
        }

        lock (_gate)
        {
            var key = (shipSymbol.ToUpperInvariant(), kind);
            if (!_observed.ContainsKey(key))
            {
                var window = new Queue<(int Units, int Seconds)>();
                window.Enqueue(((int)Math.Round(rate.UnitsPerAction), (int)Math.Round(rate.SecondsPerAction)));
                _observed[key] = window;
            }
        }
    }

    /// <inheritdoc />
    public GatheringRate For(string shipSymbol, GatheringKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shipSymbol);
        lock (_gate)
        {
            if (_observed.TryGetValue((shipSymbol.ToUpperInvariant(), kind), out var own) && own.Count > 0)
            {
                return Average(own);
            }

            var others = _observed.Where(entry => entry.Key.Kind == kind).SelectMany(entry => entry.Value).ToList();
            return others.Count > 0 ? Average(others) : Default;
        }
    }

    private static GatheringRate Average(IReadOnlyCollection<(int Units, int Seconds)> observations)
    {
        var timed = observations.Where(observation => observation.Seconds > 0).ToList();
        return new GatheringRate(
            observations.Average(observation => observation.Units),
            timed.Count > 0 ? timed.Average(observation => observation.Seconds) : Default.SecondsPerAction,
            Observed: true);
    }
}
