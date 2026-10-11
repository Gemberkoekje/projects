using System.Globalization;

namespace Curator.Presentation;

/// <summary>The user arguments after <c>--</c> on Godot's command line (BUILD_BRIEF §7.11).</summary>
public sealed class CommandLine
{
    /// <summary>The seed for a new game; 0 means pick one.</summary>
    public long Seed { get; private set; }

    /// <summary>Ignore the save and start a new game.</summary>
    public bool NewGame { get; private set; }

    /// <summary>Where screenshot mode writes its PNGs, or empty when not in screenshot mode.</summary>
    public string ShotsDir { get; private set; } = "";

    /// <summary>The strategy bot to drive, or empty.</summary>
    public string Autoplay { get; private set; } = "";

    /// <summary>How many days the bot plays.</summary>
    public int Days { get; private set; } = int.MaxValue;

    /// <summary>Parses the arguments; unknown ones are ignored.</summary>
    /// <param name="args">The arguments after <c>--</c>.</param>
    /// <returns>The parsed options.</returns>
    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var line = new CommandLine();
        for (var i = 0; i < args.Count; i++)
        {
            var next = i + 1 < args.Count ? args[i + 1] : "";
            switch (args[i])
            {
                case "--seed" when long.TryParse(next, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed):
                    line.Seed = seed;
                    i++;
                    break;
                case "--new":
                    line.NewGame = true;
                    break;
                case "--shots" when next.Length > 0:
                    line.ShotsDir = next;
                    i++;
                    break;
                case "--autoplay" when next.Length > 0:
                    line.Autoplay = next;
                    i++;
                    break;
                case "--days" when int.TryParse(next, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days):
                    line.Days = days;
                    i++;
                    break;
                default:
                    break;
            }
        }

        return line;
    }
}
