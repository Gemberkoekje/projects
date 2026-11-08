using System.Collections.Concurrent;

namespace SpaceTraders.Application.Probes;

/// <summary>
/// What each system's predicted markets promise a probe (PLAN.md slice 6.47, D134), reckoned at most once a <see cref="Lifetime"/>
/// per system: weighing every route through every market costs more than a probe pass should, and the predictions drift slowly.
/// </summary>
/// <remarks>Thread-safe; registered once for the process.</remarks>
public sealed class ProbePromiseCache
{
    /// <summary>How long promises reckoned once serve.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, (DateTimeOffset At, IReadOnlyDictionary<string, double> Promises)> _systems
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The system's promises, reckoned with <paramref name="reckon"/> when the last are older than <see cref="Lifetime"/>.</summary>
    /// <param name="systemSymbol">The system.</param>
    /// <param name="now">The time.</param>
    /// <param name="reckon">Reckons them.</param>
    /// <param name="cancellationToken">Stops the reckoning.</param>
    /// <returns>Each predicted market's promise.</returns>
    public async Task<IReadOnlyDictionary<string, double>> GetOrReadAsync(
        string systemSymbol,
        DateTimeOffset now,
        Func<CancellationToken, Task<IReadOnlyDictionary<string, double>>> reckon,
        CancellationToken cancellationToken)
    {
        if (_systems.TryGetValue(systemSymbol, out var last) && now - last.At < Lifetime && now >= last.At)
        {
            return last.Promises;
        }

        var promises = await reckon(cancellationToken);
        _systems[systemSymbol] = (now, promises);
        return promises;
    }
}
