using ByGalacticAccord.Engine.Domain;
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

/// <summary>
/// Marker for an event that, when it concerns the player, must hard-pause the loop and hand
/// control to the UI (§3.4, §4.7.3). In headless/soak runs there is no player, so it never pauses.
/// </summary>
public interface IPlayerPrompt
{
    ActorId PlayerActor { get; }

    string PromptText { get; }
}
