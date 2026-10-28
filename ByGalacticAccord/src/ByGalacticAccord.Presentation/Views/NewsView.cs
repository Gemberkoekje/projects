using System;
using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Text;

namespace ByGalacticAccord.Presentation.Views;

/// <summary>An item of The Daily Accord: its sentence, and the contract it is about (selecting it opens the contract).</summary>
public sealed record NewsLine(ContractId Contract, string Text, Tone Tone);

/// <summary>
/// A signal (design §11.2, "what's about to change?"): an idle producer, a consumer running short, a price well off the
/// player's readings; where it is, and how old the knowledge behind it is.
/// </summary>
public sealed record SignalLine(SignalKind Kind, LocationId At, string Text, string AgeText, Age Age);

public enum SignalKind
{
    /// <summary>A producer's warehouse is full: it has stopped producing (internal state, seen only where the player is present).</summary>
    Idle,

    /// <summary>A consumer has less than it restocks at (internal state, seen only where the player is present).</summary>
    Short,

    /// <summary>A producer asks well below the player's own readings of it.</summary>
    PriceLow,

    /// <summary>A producer asks well above the player's own readings of it.</summary>
    PriceHigh,
}

/// <summary>The news panel: the latest issue of The Daily Accord, and the signals in what the player knows.</summary>
public sealed record NewsViewModel(
    string Title,
    string Note,
    IReadOnlyList<NewsLine> Items,
    string Empty,
    string SignalsTitle,
    IReadOnlyList<SignalLine> Signals,
    string SignalsEmpty);

public static class NewsView
{
    /// <summary>How far off its average a producer's ask must be, against the player's readings, to be a signal.</summary>
    public const decimal PriceGap = 0.15m;

    /// <summary>How many readings a producer's history needs before an ask can stand out from it.</summary>
    public const int ReadingsForSignal = 3;

    /// <summary>A consumer is running short below this share of the stock it keeps: where it restocks (the AI's rule).</summary>
    public const decimal ShortBelow = 0.6m;

    public static NewsViewModel Build(GameSession session)
    {
        SimulationContext context = session.Context;
        var items = new List<NewsLine>();
        string title = Templates.Get("News.Title.None");
        string note = Templates.Get("News.Note");
        if (context.News.Latest is { } issue)
        {
            title = Templates.Fill("News.Title", ("day", GameTime.Day(issue.Tick).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            note = Templates.Fill("News.Note.Issue", ("count", issue.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("candidates", issue.Candidates.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("day", GameTime.Day(issue.Tick - GameTime.HoursPerDay).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            foreach (NewsItem item in issue.Items)
            {
                Tone tone = item.Kind is NewsKind.Failed or NewsKind.Cancelled or NewsKind.Defaulted ? Tone.Warn : Tone.Plain;
                items.Add(new NewsLine(item.Contract, NewsText.Describe(context, item), tone));
            }
        }

        return new NewsViewModel(title, note, items, Templates.Get("News.Empty"), Templates.Get("Signals.Title"), Signals(session), Templates.Get("Signals.Empty"));
    }

    /// <summary>
    /// The signals in what the player knows now, in location order: where it is present, idle producers and consumers running
    /// short (internal state, design §7.1); anywhere, a producer's ask well off the player's own readings of it (design §11.5:
    /// "asking ₵8.00, 23% below your readings", not "warehouse full").
    /// </summary>
    public static IReadOnlyList<SignalLine> Signals(GameSession session)
    {
        SimulationContext context = session.Context;
        Sight sight = session.Sight;
        long now = session.Now;
        var signals = new List<SignalLine>();
        foreach (ActorState actor in context.ActorsOrdered.OrderBy(a => a.Home.Value).ThenBy(a => a.Id.Value))
        {
            string place = context.Location(actor.Home).Name;
            bool here = sight.TierOf(actor.Home) == Tier.Here;
            if (actor.Producer is { } profile)
            {
                if (here && actor.Stock(profile.Good) >= profile.StockCap)
                {
                    signals.Add(new SignalLine(
                        SignalKind.Idle, actor.Home,
                        Templates.Fill("Signal.Idle", ("producer", actor.Name), ("place", place), ("good", Templates.Good(profile.Good))),
                        Templates.Get("Age.Live"), Age.Live));
                }

                if (actor.Id != session.Player.Id && Knowledge.ReadingOf(context, session.Player, actor, sight) is { } reading && PriceSignalOf(session, actor, reading) is { } signal)
                {
                    signals.Add(signal);
                }
            }

            if (here && !actor.IsPlayer)
            {
                foreach (ConsumerDemand demand in actor.Demands)
                {
                    decimal stock = actor.Stock(demand.Good);
                    if (stock < demand.TargetStock * ShortBelow)
                    {
                        signals.Add(new SignalLine(
                            SignalKind.Short, actor.Home,
                            Templates.Fill(
                                "Signal.Short", ("consumer", actor.Name), ("place", place), ("good", Templates.Good(demand.Good)),
                                ("stock", Format.Quantity(stock)), ("target", Format.Quantity(demand.TargetStock))),
                            Templates.Get("Age.Live"), Age.Live));
                    }
                }
            }
        }

        return signals;
    }

    // A producer's ask a sixth or more off the average of the player's earlier readings of it.
    private static SignalLine? PriceSignalOf(GameSession session, ActorState producer, PriceReading reading)
    {
        IReadOnlyList<(long Tick, Credits Ask)> history = session.Player.Knowledge.History(producer.Id);
        var earlier = history.Where(h => h.Tick < reading.AskTick).ToList();
        if (earlier.Count < ReadingsForSignal)
        {
            return null;
        }

        decimal average = earlier.Average(h => h.Ask.Amount);
        decimal ratio = reading.Ask.Amount / average;
        if (Math.Abs(ratio - 1m) < PriceGap)
        {
            return null;
        }

        SignalKind kind = ratio < 1m ? SignalKind.PriceLow : SignalKind.PriceHigh;
        string text = Templates.Fill(
            "Signal." + kind, ("producer", producer.Name), ("unit", Format.MoneyCents(reading.Ask)), ("good", Templates.Good(reading.Good)),
            ("pct", Format.Percent(Math.Abs(ratio - 1m))), ("since", Format.Date(earlier[0].Tick)));
        return new SignalLine(kind, producer.Home, text, InfoText.ReadingAge(reading), InfoText.AgeOf(reading, session.Now));
    }
}
