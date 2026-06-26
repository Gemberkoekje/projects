using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>
/// First-pass balance and pacing knobs. The GDD is explicit (§3.6, §5.6) that exact values are
/// tuning, not design — these are meant to be moved while reading soak telemetry, not treated as
/// final. Grouped here so the whole economy is tunable from one place.
/// </summary>
public sealed record SimulationConfig
{
    /// <summary>How often each actor re-evaluates on its staggered heartbeat (§4.5.3).</summary>
    public long HeartbeatInterval { get; init; } = 24;

    /// <summary>How often the inflation monitor samples credit supply (§5.8).</summary>
    public long InflationCheckInterval { get; init; } = 400;

    /// <summary>Allowed drift of total credits from baseline before the monitor nudges the faucet.</summary>
    public decimal InflationThreshold { get; init; } = 0.08m;

    /// <summary>How hard the monitor corrects when it intervenes.</summary>
    public decimal InflationCorrection { get; init; } = 0.07m;

    public long ReputationDecayInterval { get; init; } = 400;

    public decimal ReputationDecayRate { get; init; } = 0.04m;

    /// <summary>Flat government skim on every fulfilled haul fee (§4.2.3).</summary>
    public decimal TaxRate { get; init; } = 0.04m;

    public Credits DockingFee { get; init; } = Credits.Of(3m);

    public int NegotiationMaxRounds { get; init; } = 6;

    /// <summary>Scales route danger into a per-haul incident probability (§6.3).</summary>
    public decimal IncidentProbabilityScale { get; init; } = 0.6m;

    /// <summary>Reputation earned for a clean, on-time fulfilment of a unit-value contract.</summary>
    public decimal ReputationGainBase { get; init; } = 0.06m;

    /// <summary>Reputation lost for a flagrant breach, before value/lateness scaling.</summary>
    public decimal ReputationLossBase { get; init; } = 0.18m;

    /// <summary>How long a scouted location's intel stays fresh (§4.6).</summary>
    public long ScoutFreshnessTicks { get; init; } = 1200;
}
