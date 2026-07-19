using System;

namespace Pinball.Physics;

/// <summary>
/// The tilt-bob sensor (pinball-plan §6.1.6): a leaky integrator of accumulated nudge energy. Each nudge
/// bumps the level; it leaks over time, so nudges spaced out are safe but too many too fast cross the
/// threshold and trip TILT. Deterministic (no hidden randomness) so nudge strength trades against tilt risk
/// reproducibly, exactly as on a real machine.
/// </summary>
public sealed class TiltSensor
{
    private double _level;

    /// <summary>Trip threshold — accumulated level at or above this trips <see cref="Tripped"/>.</summary>
    public double Threshold { get; }
    /// <summary>Leak rate (level units per second) — how fast accumulated nudge energy bleeds off.</summary>
    public double LeakPerSecond { get; }
    /// <summary>Current accumulated level.</summary>
    public double Level => _level;
    /// <summary>True once the threshold has been crossed (latches until <see cref="Reset"/>).</summary>
    public bool Tripped { get; private set; }

    /// <summary>Constructs a sensor with the given trip threshold and leak rate.</summary>
    public TiltSensor(double threshold = 0.10, double leakPerSecond = 0.05)
    {
        Threshold = threshold;
        LeakPerSecond = leakPerSecond;
    }

    /// <summary>Adds <paramref name="magnitude"/> to the level (a nudge); trips if it crosses the threshold.</summary>
    public void Bump(double magnitude)
    {
        if (Tripped) return;
        _level += Math.Abs(magnitude);
        if (_level >= Threshold)
            Tripped = true;
    }

    /// <summary>Leaks the accumulated level over <paramref name="dt"/> seconds.</summary>
    public void Decay(double dt) => _level = Math.Max(0.0, _level - LeakPerSecond * dt);

    /// <summary>Clears the level and the tripped latch (new ball / new game).</summary>
    public void Reset()
    {
        _level = 0;
        Tripped = false;
    }
}
