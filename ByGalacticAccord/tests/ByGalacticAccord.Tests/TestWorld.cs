using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>Shared helpers for building and running a headless world in tests.</summary>
internal static class TestWorld
{
    public static SimulationContext RunHeadless(long seed, long ticks, SimulationConfig? config = null)
    {
        SimulationContext context = WorldGenerator.Generate(seed, withPlayer: false, config).Context;
        context.RunTo(ticks);
        return context;
    }

    /// <summary>A compact, order-independent fingerprint of world state — used to prove deterministic replay (§5.5).</summary>
    public static string Signature(SimulationContext context)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("t=").Append(context.CurrentTick);
        sb.Append(";draws=").Append(context.Rng.DrawCount);
        sb.Append(";total=").Append(context.TotalCredits().Amount);
        sb.Append(";ful=").Append(context.ContractsFulfilled);
        sb.Append(";brc=").Append(context.ContractsBreached);
        sb.Append(";inc=").Append(context.IncidentsOccurred);
        sb.Append(";cargo=").Append(context.CargoDelivered);

        foreach (var actor in context.ActorsOrdered)
        {
            sb.Append('|').Append(actor.Id.Value).Append(':').Append(actor.Credits.Amount);
        }

        return sb.ToString();
    }
}
