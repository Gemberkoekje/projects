using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Events;

/// <summary>
/// A scheduled future event (§4.7.3). The world is a priority queue of these; processing one may
/// schedule more. Nothing happens between events — there is no continuous time or position.
/// </summary>
public interface ISimEvent
{
    /// <summary>The game tick at which this event fires.</summary>
    long Tick { get; }

    /// <summary>A one-line, human-readable description for the captain's-log / journal (§5.7).</summary>
    string Describe(SimulationContext context);

    /// <summary>Apply the event. May enqueue further events via <paramref name="context"/>.</summary>
    void Process(SimulationContext context);
}
