namespace ByGalacticAccord.Engine.Simulation;

/// <summary>
/// Where destroyed credits went (design §5.1's sinks), so the soak can say what drains the money supply. Every
/// <see cref="SimulationContext.ApplySink"/>, <see cref="SimulationContext.Charge"/> and tax names one.
/// </summary>
public enum SinkKind
{
    /// <summary>Not said (tests only).</summary>
    None,

    /// <summary>A producer's cost of making goods.</summary>
    Production,

    /// <summary>A factory's labour on a part, a yard's on an assembly.</summary>
    Labour,

    /// <summary>A ship's running cost (design §6.5).</summary>
    RunningCost,

    /// <summary>A docking fee where no station operator takes it (never in the generated sector, which has one everywhere).</summary>
    Docking,

    /// <summary>An incident's unplanned cost (design §8.4).</summary>
    Incident,

    /// <summary>Interest on debt, to the Accord clearing house (design §5.4, §16.8).</summary>
    Interest,

    /// <summary>Money above an AI actor's working capital, paid to its owners outside the sector; and what a dissolved actor had left.</summary>
    PayOut,

    /// <summary>An ad's fee where no station operator takes it (never in the generated sector).</summary>
    AdFee,

    /// <summary>The Accord tax on a contract performed in its space.</summary>
    Tax,
}
