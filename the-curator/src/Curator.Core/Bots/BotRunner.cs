using Curator.Core.Commands;
using Curator.Core.Content;
using Curator.Core.Events;
using Curator.Core.Game;

namespace Curator.Core.Bots;

/// <summary>Plays a whole week (or some days) with a bot, through commands only.</summary>
public static class BotRunner
{
    private const int MaxCommandsPerVisit = 60;

    /// <summary>The bots, by command-line name.</summary>
    public static readonly IReadOnlyDictionary<string, Func<IStrategyBot>> Bots = new Dictionary<string, Func<IStrategyBot>>(StringComparer.Ordinal)
    {
        ["lend-all"] = () => new LendAllBot(),
        ["decline-all"] = () => new DeclineAllBot(),
        ["careful"] = () => new CarefulBot(),
    };

    /// <summary>Plays from a new game.</summary>
    /// <param name="content">The content.</param>
    /// <param name="bot">The bot.</param>
    /// <param name="seed">The seed.</param>
    /// <param name="days">How many days to play; the whole week by default.</param>
    /// <returns>How it went.</returns>
    public static BotResult Play(ContentSet content, IStrategyBot bot, long seed, int days = int.MaxValue)
    {
        var session = new GameSession(content);
        BotMoves.Must(session, new NewGame(seed));
        while (PlayStep(session, bot, days))
        {
        }

        return Summarize(session, bot.Name, seed);
    }

    /// <summary>
    /// Takes one step of a bot's game: rings the bell and plays the visit, or closes for the day.
    /// Godot's autoplay calls this once per frame.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="bot">The bot.</param>
    /// <param name="days">The last day to play.</param>
    /// <returns>False when there's nothing more to do.</returns>
    public static bool PlayStep(GameSession session, IStrategyBot bot, int days = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(bot);
        if (session.State.Status == DayStatus.WeekOver || session.State.Day > days)
        {
            return false;
        }

        if (!session.Views.Hud().SlotsLeft)
        {
            if (session.State.Day >= days)
            {
                return false;
            }

            BotMoves.Must(session, new CloseForTheDay());
            return true;
        }

        BotMoves.Must(session, new RingBell());
        if (session.State.InVisit)
        {
            var before = session.Events.Count;
            bot.PlayVisit(session);
            if (session.State.InVisit || session.Events.Count - before > MaxCommandsPerVisit * 10)
            {
                throw new InvalidOperationException($"{bot.Name} left visit {session.State.Visit.VisitId} undecided.");
            }
        }

        return true;
    }

    /// <summary>Summarizes a finished session.</summary>
    /// <param name="session">The session.</param>
    /// <param name="bot">The bot's name.</param>
    /// <param name="seed">The seed.</param>
    /// <returns>The result.</returns>
    public static BotResult Summarize(GameSession session, string bot, long seed)
    {
        ArgumentNullException.ThrowIfNull(session);
        var grants = session.Events.OfType<ManaGranted>().ToList();
        var outcomes = session.State.Resolutions
            .Where(r => r.LoanId.Length > 0)
            .GroupBy(r => r.Category)
            .ToDictionary(g => g.Key, g => g.Count());
        return new BotResult(
            bot,
            seed,
            session,
            session.State.Money,
            grants.Sum(g => g.AttentiveBonus + g.GoodBonus),
            grants.Sum(g => g.Total),
            session.State.Loans.Count,
            session.Events.OfType<VisitDeclined>().Count(),
            session.Events.OfType<PatronWalkedOut>().Count(),
            outcomes,
            session.Views.WeekSummary().StoryPatrons);
    }
}
