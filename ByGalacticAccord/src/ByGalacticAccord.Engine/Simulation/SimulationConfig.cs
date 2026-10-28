using System.Collections.Generic;
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

    /// <summary>The Accord's tax on a contract's price, paid by the payer when it is performed in Accord space (§4.2.3).</summary>
    public decimal TaxRate { get; init; } = 0.04m;

    public Credits DockingFee { get; init; } = Credits.Of(3m);

    /// <summary>The most counter-offers a haggle runs to before the side that would make the next walks away (design §16.5).</summary>
    public int NegotiationMaxRounds { get; init; } = 6;

    /// <summary>
    /// Whether the player's posts reach the AI actors that can see them, who answer over game time (design §3.6). Tests of
    /// other rules turn it off, as they turn off heartbeats, so only the actors they drive act.
    /// </summary>
    public bool PostsDrawAnswers { get; init; } = true;

    /// <summary>Scales route danger into a per-haul incident probability (§6.3).</summary>
    public decimal IncidentProbabilityScale { get; init; } = 0.6m;

    /// <summary>Reputation earned for a clean, on-time fulfilment of a unit-value contract.</summary>
    public decimal ReputationGainBase { get; init; } = 0.06m;

    /// <summary>Reputation lost for a flagrant breach, before value/lateness scaling.</summary>
    public decimal ReputationLossBase { get; init; } = 0.18m;

    /// <summary>How many contracts The Daily Accord reports a day (design §7.4; first-pass, an open question in design A.2). Zero prints no news.</summary>
    public int NewsItemsPerDay { get; init; } = 3;

    /// <summary>How much likelier a contract with a place outside the Accord is to make the news (plan risk R1: the frontier must hear of it).</summary>
    public decimal NewsFrontierWeight { get; init; } = 3m;

    /// <summary>
    /// How many jumps out from its place a post is shown free of charge (design §7.1, D38). At 0 it is shown in its own system
    /// only; further out, its poster pays to show it (<see cref="Economy.Advertising"/>).
    /// </summary>
    public int AdFreeJumps { get; init; } = 0;

    /// <summary>The fee to show a post in one system a jump away, as a share of its price (plan S6b's experiments: 0.05%).</summary>
    public decimal AdFeeShare { get; init; } = 0.0005m;

    /// <summary>The least a post costs to show in one system a jump away.</summary>
    public Credits AdFeeFloor { get; init; } = Credits.Of(0.20m);

    /// <summary>How much dearer each further jump is, per system shown (×4: a jump costs four times the one before).</summary>
    public decimal AdFeeGrowth { get; init; } = 4m;

    /// <summary>How many hours after it is paid for a post is shown at a distance, by jumps; past the last, a day more a jump.</summary>
    public IReadOnlyList<long> AdDelayHours { get; init; } = new long[] { 0, 4, 24, 48 };

    /// <summary>How long an AI actor's post sits unsold, since it was posted or last shown further, before it pays to show it a jump further.</summary>
    public long AdWaitHours { get; init; } = 24;

    /// <summary>The most an AI actor pays to show a post one jump further, as a share of the post's price.</summary>
    public decimal AdBudgetShare { get; init; } = 0.05m;

    /// <summary>The most a consumer keeps in hand, in days of income; the rest goes on things outside the sector (a sink).</summary>
    public decimal ConsumerSavingsDays { get; init; } = 90m;

    /// <summary>The working capital a producer keeps, in full warehouses at production cost; profit above it is paid out (a sink).</summary>
    public decimal ProducerReserveWarehouses { get; init; } = 4m;

    /// <summary>The working capital a trader keeps, as a multiple of its fleet's value; profit above it is paid out (a sink).</summary>
    public decimal TraderReserveFleets { get; init; } = 1.5m;
}
