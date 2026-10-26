namespace ByGalacticAccord.Engine.Simulation;

/// <summary>Why <see cref="SimulationContext.RunTo"/> handed control back.</summary>
public enum RunStop
{
    /// <summary>Reached the target tick with events still pending.</summary>
    ReachedTarget,

    /// <summary>The event queue emptied (only happens if heartbeats are not running).</summary>
    Drained,

    /// <summary>Something the player should see stopped the clock (an incident on its ship).</summary>
    PlayerAlert,

    /// <summary>A decision landed on the player: the hard pause (design §8.3). Nothing moves until it is made.</summary>
    PlayerDecision,
}
