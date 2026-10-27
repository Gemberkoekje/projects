using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;

namespace ByGalacticAccord.Presentation.Bots;

/// <summary>What a bot did over a run: what it started and ended with, what it signed, posted and countered, and any invariant broken.</summary>
public sealed record BotReport(
    string Bot,
    long Seed,
    long Ticks,
    Credits StartCredits,
    Credits EndCredits,
    Credits EndEscrow,
    int Deals,
    int Posts,
    int Counters,
    int Haggles,
    int Decisions,
    IReadOnlyList<InvariantViolation> Violations)
{
    /// <summary>What it gained, money in hand and its share of escrow together, a game day on average.</summary>
    public Credits PerDay => Credits.Of((EndCredits.Amount + EndEscrow.Amount - StartCredits.Amount) * GameTime.HoursPerDay / System.Math.Max(1, Ticks));
}

/// <summary>
/// Runs a world with a bot as its player (design §20.3): the clock runs a few game hours at a time, as a player glances at
/// it; any decision the terms hand the player is answered by the AI's rule, alerts are acknowledged, and the bot acts. The
/// invariants are checked as the soak harness checks them.
/// </summary>
public static class BotRun
{
    /// <summary>How many game hours pass between the bot's looks.</summary>
    public const long Glance = 4;

    public static BotReport Run(Bot bot, long seed, long ticks, long checkEvery = 5_000)
    {
        var session = new GameSession(seed);
        SimulationContext context = session.Context;
        Credits start = session.Player.Credits;
        int decisions = 0;
        var violations = new List<InvariantViolation>();
        long nextCheck = checkEvery;
        while (context.CurrentTick < ticks && violations.Count == 0)
        {
            RunStop stop = context.RunTo(System.Math.Min(ticks, context.CurrentTick + Glance));
            foreach (PlayerDecision decision in context.PlayerDecisions.ToList())
            {
                session.Decide(decision, Bot.Answer(session, decision));
                decisions++;
            }

            context.ClearPlayerAlerts();
            if (stop is RunStop.ReachedTarget or RunStop.Drained)
            {
                bot.Act(session);
            }

            if (context.CurrentTick >= nextCheck)
            {
                violations.AddRange(Invariants.Check(context));
                nextCheck += checkEvery;
            }
        }

        violations.AddRange(Invariants.Check(context));
        decimal escrow = session.PlayerActiveContracts.Sum(c => c.Payer == session.Player.Id ? c.EscrowHeld.Amount - c.LockedBond.Amount : c.LockedBond.Amount);
        return new BotReport(
            bot.Name, seed, context.CurrentTick, start, session.Player.Credits, Credits.Of(escrow), bot.Deals, bot.Posts, bot.Counters,
            session.PlayerNegotiations.Count(), decisions, violations);
    }
}
