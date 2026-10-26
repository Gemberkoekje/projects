using System;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Economy;

/// <summary>
/// The pragmatic inflation safety valve (§4.2.1, §5.8): a designer's thumb that nudges the faucet
/// when total credit supply drifts from baseline. The GDD is candid that this should shrink over
/// time toward a last-resort circuit breaker as organic regulators (bankruptcy, §7.8) take over;
/// for the slice it just has to keep the first hour from breaking.
/// </summary>
public static class InflationMonitor
{
    public static void Evaluate(SimulationContext context)
    {
        decimal baseline = context.InitialCredits.Amount;
        if (baseline <= 0m)
        {
            return;
        }

        decimal total = context.TotalCredits().Amount;
        decimal drift = (total - baseline) / baseline;

        if (drift > context.Config.InflationThreshold)
        {
            context.FaucetMultiplier = Math.Clamp(context.FaucetMultiplier * (1m - context.Config.InflationCorrection), 0.02m, 2.0m);
            context.Record(new FaucetAdjustedEntry(context.CurrentTick, drift, context.FaucetMultiplier));
        }
        else if (drift < -context.Config.InflationThreshold)
        {
            context.FaucetMultiplier = Math.Clamp(context.FaucetMultiplier * (1m + context.Config.InflationCorrection), 0.02m, 2.0m);
            context.Record(new FaucetAdjustedEntry(context.CurrentTick, drift, context.FaucetMultiplier));
        }
    }
}
