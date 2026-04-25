using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Availability;

/// <summary>
/// Thread-safe singleton that tracks SpaceTraders API reachability.
/// Exposes "transition" flags that allow the GameLoopService to detect
/// availability changes and publish domain events exactly once per transition.
/// </summary>
public sealed class ApiAvailabilityState : IApiAvailabilityState
{
    private volatile bool _isAvailable = true;
    private volatile bool _pendingUnavailableTransition;
    private volatile bool _pendingAvailableTransition;

    public bool IsAvailable => _isAvailable;

    public void MarkUnavailable()
    {
        if (_isAvailable)
        {
            _isAvailable = false;
            _pendingUnavailableTransition = true;
        }
    }

    public void MarkAvailable()
    {
        if (!_isAvailable)
        {
            _isAvailable = true;
            _pendingAvailableTransition = true;
        }
    }

    public bool ConsumeUnavailableTransition()
    {
        if (!_pendingUnavailableTransition) return false;
        _pendingUnavailableTransition = false;
        return true;
    }

    public bool ConsumeAvailableTransition()
    {
        if (!_pendingAvailableTransition) return false;
        _pendingAvailableTransition = false;
        return true;
    }
}
