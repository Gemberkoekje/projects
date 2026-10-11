using Curator.Core.Bots;
using Curator.Core.Game;
using Curator.Presentation.Game;
using Godot;

namespace Curator.Presentation.Checks;

/// <summary>
/// <c>--autoplay &lt;bot&gt; [--days n]</c> (BUILD_BRIEF §7.11): drives a strategy bot through the
/// presentation layer at speed — every command goes through the controller, which redraws every view —
/// one bell or one closing per frame, dismissing the summaries as a player would. Exits with 0 when
/// no errors were logged along the way.
/// </summary>
public partial class AutoplayRunner : Node
{
    private string botName = "";
    private int days = int.MaxValue;
    private int steps;
    // Unset until Setup (a justified nullable: nothing to play yet).
    private GameController? controller;
    // Null until Setup finds the bot (a justified nullable: the bot or none).
    private IStrategyBot? bot;

    private GameController Game => controller ?? throw new InvalidOperationException("Set up the autoplay first.");

    /// <summary>Prepares the run.</summary>
    /// <param name="gameController">The game.</param>
    /// <param name="name">The bot, as the command line spells it.</param>
    /// <param name="lastDay">The last day to play, through its evening.</param>
    public void Setup(GameController gameController, string name, int lastDay)
    {
        controller = gameController;
        botName = name;
        days = lastDay;
        bot = BotRunner.Bots.TryGetValue(name, out var make) ? make() : null;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        if (bot is null)
        {
            GD.PushError($"Unknown bot '{botName}'. Bots: {string.Join(", ", BotRunner.Bots.Keys)}.");
            Finish();
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (bot is null)
        {
            return;
        }

        try
        {
            if (Game.WeekSummaryShowing)
            {
                Finish();
                return;
            }

            if (Game.Modal)
            {
                Game.DismissSummary();
                return;
            }

            if (!BotRunner.PlayStep(Game.Session, Game, bot, days))
            {
                Finish();
                return;
            }

            steps++;
        }
        catch (InvalidOperationException ex)
        {
            GD.PushError($"{botName} failed: {ex.Message}");
            Finish();
        }
    }

    private void Finish()
    {
        var state = Game.Session.State;
        var result = BotRunner.Summarize(Game.Session, botName, state.Seed);
        var code = ErrorLog.Errors == 0 ? 0 : 1;
        GD.Print($"AUTOPLAY {botName}: {steps} steps, day {state.Day}{(state.Status == DayStatus.WeekOver ? " (week over)" : "")}, money {result.Money}, {result.Loans} lent, {result.Declines} declined, {ErrorLog.Errors} errors logged; exit {code}");
        bot = null;
        GetTree().Quit(code);
    }
}
