namespace SpaceTraders.Application.Services;

/// <summary>
/// When the game loop's ticks began (<see cref="Automation.GameLoopService"/>): every tick, a paused one or one with automation
/// switched off too. What a plan says on its pass (<see cref="PurchaseNeeds"/>, <see cref="ShipyardCalls"/>) counts until its
/// next pass, which comes a tick later, however long that tick takes (B79): a tick that runs the plans and the ships' goal
/// steps takes minutes, while a paused one takes no time, so after a pause in which no plan ran the plans' words are old.
/// </summary>
/// <remarks>In memory, a singleton. Thread-safe: a purchase through the control endpoint can come at any time.</remarks>
public sealed class GameTicks
{
    private readonly Lock _gate = new();
    private DateTimeOffset _current = DateTimeOffset.MinValue;
    private DateTimeOffset _previous = DateTimeOffset.MinValue;

    /// <summary>Records that a tick began.</summary>
    /// <param name="at">When it began.</param>
    public void Begin(DateTimeOffset at)
    {
        lock (_gate)
        {
            _previous = _current;
            _current = at;
        }
    }

    /// <summary>
    /// Whether what a plan said at <paramref name="saidAt"/> still counts at <paramref name="now"/>: it is at most
    /// <paramref name="lifetime"/> old, or it was said in this tick or the one before, so the plan hasn't had its next pass yet.
    /// </summary>
    /// <param name="saidAt">When the plan said it.</param>
    /// <param name="now">The time to judge by.</param>
    /// <param name="lifetime">How long it counts, however the ticks went.</param>
    /// <returns>True while it counts.</returns>
    public bool Counts(DateTimeOffset saidAt, DateTimeOffset now, TimeSpan lifetime)
    {
        if (now - saidAt <= lifetime)
        {
            return true;
        }

        lock (_gate)
        {
            return _previous != DateTimeOffset.MinValue && saidAt >= _previous;
        }
    }
}
