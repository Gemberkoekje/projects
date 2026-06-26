using System;
using System.Globalization;
using ByGalacticAccord.Cli;

string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "play";
long seed = ArgValue(args, "--seed", 20260619L, s => long.Parse(s, CultureInfo.InvariantCulture));

switch (mode)
{
    case "play":
        Game.Run(seed);
        return 0;

    case "soak":
        long ticks = args.Length > 1 && long.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long t)
            ? t
            : 200_000L;
        return Soak.Run(seed, ticks);

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
    Console.WriteLine("By Galactic Accord — vertical slice (Slice 0)");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  play  [--seed N]            Play the interactive console game (default).");
    Console.WriteLine("  soak  [ticks] [--seed N]    Run headless, print economy telemetry, assert invariants.");
    Console.WriteLine("  help                        Show this help.");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dotnet run --project src/ByGalacticAccord.Cli -- play");
    Console.WriteLine("  dotnet run --project src/ByGalacticAccord.Cli -- soak 500000 --seed 42");
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
