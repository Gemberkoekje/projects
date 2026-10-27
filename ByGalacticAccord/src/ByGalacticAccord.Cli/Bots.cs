using System;
using System.Diagnostics;
using System.Globalization;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Bots;

namespace ByGalacticAccord.Cli;

/// <summary>
/// The bot players (design §20.3): the same world played once by each scripted strategy, each run checked against the
/// invariants, and what each earned side by side. Returns a process exit code: 1 if any run broke an invariant.
/// </summary>
internal static class Bots
{
    public static int Run(long seed, long ticks)
    {
        Console.WriteLine($"== By Galactic Accord — bot players ==  seed={seed}  ticks={ticks:N0} (to {GameTime.FormatDate(ticks)})");
        Console.WriteLine();
        Console.WriteLine($"   {"bot",-14} {"start",9} {"end",10} {"escrow",8} {"per day",8} {"deals",6} {"posts",6} {"counters",9} {"haggles",8} {"decisions",9} {"seconds",8}");
        int exit = 0;
        foreach (Bot bot in new Bot[] { new BoardTaker(), new MarketReader() })
        {
            var watch = Stopwatch.StartNew();
            BotReport report = BotRun.Run(bot, seed, ticks);
            Console.WriteLine(
                $"   {report.Bot,-14} {report.StartCredits,9} {report.EndCredits,10} {report.EndEscrow,8} {report.PerDay,8} {report.Deals,6} {report.Posts,6} {report.Counters,9} {report.Haggles,8} {report.Decisions,9} {watch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture),8}");
            foreach (InvariantViolation violation in report.Violations)
            {
                Console.WriteLine($"     INVARIANT VIOLATION [{violation.Name}] {violation.Detail}");
                exit = 1;
            }
        }

        Console.WriteLine();
        Console.WriteLine(exit == 0 ? "All invariants held for every bot. ✓" : "An invariant broke.");
        return exit;
    }
}
