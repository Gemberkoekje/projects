using System.Globalization;
using System.Text;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>
/// A compact fingerprint of world state (design §20.1): the clock, the random draws consumed, the money
/// supply, the settlement counters, and every actor's credits. Two runs with the same seed and commands
/// must match exactly. The tests use it for replay, and the browser check compares the client's
/// WebAssembly run with a desktop one (plan risk R6).
/// </summary>
public static class StateFingerprint
{
    public static string Of(SimulationContext context)
    {
        var sb = new StringBuilder();
        sb.Append("t=").Append(context.CurrentTick.ToString(CultureInfo.InvariantCulture));
        sb.Append(";draws=").Append(context.Rng.DrawCount.ToString(CultureInfo.InvariantCulture));
        sb.Append(";total=").Append(context.TotalCredits().Amount.ToString(CultureInfo.InvariantCulture));
        sb.Append(";ful=").Append(context.ContractsFulfilled.ToString(CultureInfo.InvariantCulture));
        sb.Append(";brc=").Append(context.ContractsBreached.ToString(CultureInfo.InvariantCulture));
        sb.Append(";inc=").Append(context.IncidentsOccurred.ToString(CultureInfo.InvariantCulture));
        sb.Append(";cargo=").Append(context.CargoDelivered.ToString(CultureInfo.InvariantCulture));

        foreach (Actors.ActorState actor in context.ActorsOrdered)
        {
            sb.Append('|').Append(actor.Id.Value.ToString(CultureInfo.InvariantCulture))
              .Append(':').Append(actor.Credits.Amount.ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
