using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Cli;

/// <summary>
/// The state fingerprint after a headless run, for comparing with the same run elsewhere: the browser
/// check compares it with the client's run in WebAssembly (plan risk R6).
/// </summary>
internal static class Fingerprint
{
    public static string Of(long seed, long ticks)
    {
        SimulationContext context = WorldGenerator.Generate(seed, withPlayer: false).Context;
        context.RunTo(ticks);
        return StateFingerprint.Of(context);
    }
}
