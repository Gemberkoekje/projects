namespace ByGalacticAccord.Engine.Simulation;

/// <summary>Why <see cref="SimulationContext.RunTo"/> handed control back.</summary>
public enum RunStop
{
    /// <summary>Reached the target tick with events still pending.</summary>
    ReachedTarget,

    /// <summary>The event queue emptied (only happens if heartbeats are not running).</summary>
    Drained,

    /// <summary>A player decision/alert hard-paused the loop (§3.4).</summary>
    PlayerAlert,
}
