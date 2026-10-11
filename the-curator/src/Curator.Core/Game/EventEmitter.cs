using Curator.Core.Events;

namespace Curator.Core.Game;

/// <summary>Stamps events with their sequence number and day, folds them into the state, and keeps them.</summary>
internal sealed class EventEmitter
{
    private readonly GameState state;
    private readonly List<GameEvent> emitted = [];

    public EventEmitter(GameState state) => this.state = state;

    /// <summary>The events emitted so far by this command.</summary>
    public IReadOnlyList<GameEvent> Emitted => emitted;

    /// <summary>The sequence number the next event will get; draws are keyed to it.</summary>
    public long NextSequence => state.EventCount;

    /// <summary>Appends an event. A <see cref="DayStarted"/> carries its own day; all others get today's.</summary>
    /// <param name="e">The event.</param>
    public void Emit(GameEvent e)
    {
        var day = e is DayStarted ? e.Day : state.Day;
        var stamped = e with { Sequence = state.EventCount, Day = day };
        state.Apply(stamped);
        emitted.Add(stamped);
    }
}
