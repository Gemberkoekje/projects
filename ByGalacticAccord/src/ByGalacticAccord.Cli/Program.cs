using System;
using System.Globalization;
using ByGalacticAccord.Cli;

// The soak harness (design §18, §20.3). The game itself is played in the browser client; this
// console app runs the world headless, prints telemetry and asserts the invariants, and plays it with bot players.
string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "soak";
long seed = ArgValue(args, "--seed", 20260619L, s => long.Parse(s, CultureInfo.InvariantCulture));

switch (mode)
{
    case "soak":
        long ticks = args.Length > 1 && long.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long t)
            ? t
            : 200_000L;
        // --keep traders|stations|both: those actors keep what they hold above their working capital (plan S8's payout measurement).
        string keep = ArgValue(args, "--keep", "none", s => s.ToLowerInvariant());
        var config = new ByGalacticAccord.Engine.Simulation.SimulationConfig
        {
            TradersPayOut = keep is not ("traders" or "both"),
            StationsPayOut = keep is not ("stations" or "both"),
            BondsOnCredit = !System.Array.Exists(args, a => string.Equals(a, "--cash-bonds", StringComparison.OrdinalIgnoreCase)),
        };
        return Soak.Run(seed, ticks, config);

    case "bots":
        long botTicks = args.Length > 1 && long.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long b)
            ? b
            : 200_000L;
        return Bots.Run(seed, botTicks);

    case "fingerprint":
        long until = args.Length > 1 && long.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long f)
            ? f
            : 20_000L;
        Console.WriteLine(Fingerprint.Of(seed, until));
        return 0;

    case "help":
    case "--help":
    case "-h":
        PrintHelp();
        return 0;

    default:
        Console.WriteLine($"Unknown command '{mode}'.");
        PrintHelp();
        return 1;
}

static void PrintHelp()
{
    Console.WriteLine("By Galactic Accord — soak harness");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  soak  [ticks] [--seed N] [--keep traders|stations|both]");
    Console.WriteLine("                              Run headless, print economy telemetry, assert invariants (default); --keep: those actors keep their surplus.");
    Console.WriteLine("        [--cash-bonds]        Bonds in cash only: the clearing house lends none (S8's measurement).");
    Console.WriteLine("  bots  [ticks] [--seed N]    Play the world with each bot as its player, assert invariants, compare them (default 200,000).");
    Console.WriteLine("  fingerprint [ticks] [--seed N]  Print the state fingerprint after a headless run (default 20,000).");
    Console.WriteLine("  help                        Show this help.");
    Console.WriteLine();
    Console.WriteLine("One tick is one game hour. Examples:");
    Console.WriteLine("  dotnet run --project src/ByGalacticAccord.Cli -- soak");
    Console.WriteLine("  dotnet run --project src/ByGalacticAccord.Cli -- soak 1000000 --seed 42");
}

static T ArgValue<T>(string[] arguments, string flag, T fallback, Func<string, T> parse)
{
    for (int i = 0; i < arguments.Length - 1; i++)
    {
        if (string.Equals(arguments[i], flag, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return parse(arguments[i + 1]);
            }
            catch (FormatException)
            {
                return fallback;
            }
        }
    }

    return fallback;
}
