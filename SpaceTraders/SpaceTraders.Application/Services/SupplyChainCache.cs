using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Services;

/// <summary>
/// The game's production chains (<c>GET market/supply-chain</c>): for each good, the goods it is made
/// from. They don't change during a reset, so they are fetched once per process and shared by the
/// trading plan (slice 6.5) and the markets dashboard (slice 2.8).
/// </summary>
public interface ISupplyChainCache
{
    /// <summary>
    /// The production chains, fetched on the first call. After a failed fetch the next one waits an
    /// hour, so an API that is down costs one call an hour and can't raise <c>RepeatingError</c>.
    /// </summary>
    /// <param name="port">The game API, from the caller's scope.</param>
    /// <param name="now">The time of the call; it decides whether a failed fetch is tried again.</param>
    /// <param name="cancellationToken">Stops the fetch.</param>
    /// <returns>For each good the goods it is made from; empty while they aren't known.</returns>
    Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetAsync(
        ISpaceTradersPort port,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

/// <inheritdoc cref="ISupplyChainCache" />
/// <remarks>A singleton; thread-safe. At most one fetch runs at a time.</remarks>
public sealed class SupplyChainCache(ILogger<SupplyChainCache> logger) : ISupplyChainCache, IDisposable
{
    /// <summary>How long to wait before asking again after a failure.</summary>
    internal static readonly TimeSpan Retry = TimeSpan.FromHours(1);

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Unknown =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyDictionary<string, IReadOnlyList<string>> _chains = Unknown;
    private bool _known;
    private DateTimeOffset _dueAt = DateTimeOffset.MinValue;

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetAsync(
        ISpaceTradersPort port,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(port);

        return Volatile.Read(ref _known) ? Task.FromResult(_chains) : FetchAsync(port, now, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> FetchAsync(
        ISpaceTradersPort port,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_known || now < _dueAt)
            {
                return _chains;
            }

            try
            {
                var chains = await port.GetSupplyChainAsync(cancellationToken);
                _chains = new Dictionary<string, IReadOnlyList<string>>(chains, StringComparer.OrdinalIgnoreCase);
                Volatile.Write(ref _known, true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _dueAt = now + Retry;
                logger.LogWarning(ex, "Couldn't fetch the game's production chains; trying again at {RetryAt}.", _dueAt);
            }

            return _chains;
        }
        finally
        {
            _gate.Release();
        }
    }
}
