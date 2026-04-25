using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Availability;

/// <summary>
/// Thread-safe singleton that tracks SpaceTraders API reachability.
/// Exposes "transition" flags that allow the GameLoopService to detect
/// availability changes and publish domain events exactly once per transition.
/// Uses Interlocked to guarantee atomic compare-and-swap across the availability
/// flag and the corresponding pending transition flag.
/// </summary>
public sealed class ApiAvailabilityState : IApiAvailabilityState
{
    // 1 = available, 0 = unavailable
    private int _isAvailable = 1;
    // 1 = pending, 0 = consumed/no transition
    private int _pendingUnavailableTransition;
    private int _pendingAvailableTransition;

    public bool IsAvailable => Interlocked.CompareExchange(ref _isAvailable, 1, 1) == 1;

    public void MarkUnavailable()
    {
        if (Interlocked.CompareExchange(ref _isAvailable, 0, 1) == 1)
        {
            // Transitioned from available → unavailable
            Interlocked.Exchange(ref _pendingUnavailableTransition, 1);
        }
    }

    public void MarkAvailable()
    {
        if (Interlocked.CompareExchange(ref _isAvailable, 1, 0) == 0)
        {
            // Transitioned from unavailable → available
            Interlocked.Exchange(ref _pendingAvailableTransition, 1);
        }
    }

    public bool ConsumeUnavailableTransition()
        => Interlocked.Exchange(ref _pendingUnavailableTransition, 0) == 1;

    public bool ConsumeAvailableTransition()
        => Interlocked.Exchange(ref _pendingAvailableTransition, 0) == 1;
}
