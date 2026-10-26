using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>Shared helpers for building and running a headless world in tests.</summary>
internal static class TestWorld
{
    /// <summary>
    /// Trait for soak-length tests (design §20.3). They are excluded by default (see the test project's
    /// VSTestTestCaseFilter); run them with <c>dotnet test --filter Category=Soak</c>.
    /// </summary>
    public const string Category = "Category";

    public const string Soak = "Soak";

    /// <summary>The horizon of a fast world run: 20,000 game hours, about 833 game days.</summary>
    public const long FastTicks = 20_000;

    public static SimulationContext RunHeadless(long seed, long ticks, SimulationConfig? config = null)
    {
        SimulationContext context = WorldGenerator.Generate(seed, withPlayer: false, config).Context;
        context.RunTo(ticks);
        return context;
    }

    /// <summary>A compact fingerprint of world state, used to prove deterministic replay (design §20.1).</summary>
    public static string Signature(SimulationContext context) => StateFingerprint.Of(context);
}
