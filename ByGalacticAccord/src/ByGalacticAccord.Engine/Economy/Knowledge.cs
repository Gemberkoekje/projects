using System;
using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Economy;

/// <summary>Where a reading came from: how it was learnt, which is also how the interface words its age (design §7.2).</summary>
public enum ReadingSource
{
    /// <summary>Seen live, where the actor is present.</summary>
    Live,

    /// <summary>Seen where the actor was present, and remembered since.</summary>
    Seen,

    /// <summary>The daily price bulletin, for places one jump from the actor's presence: the ask only (design §7.1).</summary>
    Daily,

    /// <summary>A producer's lot reported in The Daily Accord: its price per unit, as posted.</summary>
    News,

    /// <summary>What everyone knew of the sector when the game began.</summary>
    Start,
}

/// <summary>
/// What an actor knows of one producer (design §7.1, §7.2): the producer's ask and when it was read, and its stock free to
/// sell (internal state, seen only where the actor is present) and when. A stock never seen is null.
/// </summary>
public readonly record struct PriceReading(
    ActorId Producer, GoodType Good, LocationId At, Credits Ask, long AskTick, ReadingSource Source, decimal? Stock, long? StockTick)
{
    /// <summary>Read just now, where the actor is present.</summary>
    public bool IsLive => Source == ReadingSource.Live;

    /// <summary>
    /// The stock as this reading knows it: seen with the ask. A later reading of the ask alone (the daily bulletin, the news)
    /// supersedes an older sight of the stock: the actor last saw a price, not a warehouse, so it doesn't know the stock.
    /// </summary>
    public decimal? CurrentStock => StockTick is { } seen && seen >= AskTick ? Stock : null;
}

/// <summary>
/// One actor's memory of the sector (design §7.2, D32): its last reading of every producer it has known, a short history of
/// its daily readings of each, when it was last present at each location, and the contracts it heard of in the news and then
/// found closed. It trusts what it last saw. Written only by events (<see cref="Knowledge"/>), so a replay knows the same.
/// </summary>
public sealed class ActorKnowledge
{
    /// <summary>How many days of readings the history keeps per producer.</summary>
    public const int HistoryDays = 14;

    private readonly Dictionary<ActorId, PriceReading> _readings = new();
    private readonly Dictionary<ActorId, List<(long Tick, Credits Ask)>> _history = new();
    private readonly Dictionary<LocationId, long> _lastHere = new();
    private readonly HashSet<ContractId> _closed = new();
    private readonly Dictionary<ActorId, long> _refused = new();

    /// <summary>Its last reading of a producer, as remembered (not live): null if it has never known it.</summary>
    public PriceReading? Stored(ActorId producer) => _readings.TryGetValue(producer, out PriceReading reading) ? reading : null;

    /// <summary>Its readings of a producer's ask, at most one a day, oldest first.</summary>
    public IReadOnlyList<(long Tick, Credits Ask)> History(ActorId producer)
    {
        if (!_history.TryGetValue(producer, out List<(long Tick, Credits Ask)>? list))
        {
            return Array.Empty<(long, Credits)>();
        }

        if (list.Count > HistoryDays)
        {
            list.RemoveRange(0, list.Count - HistoryDays);
        }

        return list;
    }

    /// <summary>When it last had a presence at a location (as of its last look there); null if never since the game began.</summary>
    public long? LastHere(LocationId location) => _lastHere.TryGetValue(location, out long tick) ? tick : null;

    /// <summary>Whether a contract it knew of from the news turned out to be closed when it tried it.</summary>
    public bool FoundClosed(ContractId contract) => _closed.Contains(contract);

    /// <summary>When a producer last turned down its order at the door, if ever.</summary>
    public long? RefusedAt(ActorId producer) => _refused.TryGetValue(producer, out long tick) ? tick : null;

    internal void Note(PriceReading reading)
    {
        bool known = _readings.TryGetValue(reading.Producer, out PriceReading before);
        if (known && before.AskTick > reading.AskTick)
        {
            return; // it already knows better
        }

        // A reading without stock keeps the stock it last saw, with that reading's age; a live one is remembered as seen.
        bool keepStock = reading.Stock is null && known;
        _readings[reading.Producer] = new PriceReading(
            reading.Producer, reading.Good, reading.At, reading.Ask, reading.AskTick,
            reading.Source == ReadingSource.Live ? ReadingSource.Seen : reading.Source,
            keepStock ? before.Stock : reading.Stock, keepStock ? before.StockTick : reading.StockTick);

        if (!_history.TryGetValue(reading.Producer, out List<(long Tick, Credits Ask)>? list))
        {
            list = new List<(long Tick, Credits Ask)>(2 * HistoryDays);
            _history[reading.Producer] = list;
        }

        int last = list.Count - 1;
        if (last >= 0 && list[last].Tick / GameTime.HoursPerDay == reading.AskTick / GameTime.HoursPerDay)
        {
            list[last] = (reading.AskTick, reading.Ask);
        }
        else if (last < 0 || list[last].Tick < reading.AskTick)
        {
            if (list.Count >= 2 * HistoryDays)
            {
                list.RemoveRange(0, list.Count - HistoryDays + 1); // trimmed a batch at a time; History shows the last HistoryDays
            }

            list.Add((reading.AskTick, reading.Ask));
        }
    }

    internal void NotePresent(LocationId location, long tick) => _lastHere[location] = tick;

    internal void NoteClosed(ContractId contract) => _closed.Add(contract);

    internal void NoteRefused(ActorId producer, long tick) => _refused[producer] = tick;
}

/// <summary>
/// The daily price bulletin (design §7.1: one jump from your presence, "prices updated daily"): every producer's ask, posted
/// each morning at <see cref="Hour"/>. What an actor near a producer knows of its ask is this.
/// </summary>
public sealed class PriceBulletin
{
    /// <summary>The hour of the day the bulletin is posted, with The Daily Accord.</summary>
    public const int Hour = 6;

    private readonly Dictionary<ActorId, Credits> _asks = new();

    /// <summary>When it was last posted; null before the first.</summary>
    public long? Tick { get; private set; }

    public Credits? AskOf(ActorId producer) => _asks.TryGetValue(producer, out Credits ask) ? ask : null;

    internal void Post(SimulationContext context)
    {
        foreach (ActorState actor in context.ActorsOrdered)
        {
            if (actor.Producer is not null)
            {
                _asks[actor.Id] = DecisionEngine.Ask(actor);
            }
        }

        Tick = context.CurrentTick;
    }
}

/// <summary>
/// What actors know (design §7, D32): readings of producers' asks and stocks, by tier, and the contracts they believe open.
/// Where an actor is present it reads live; one jump away it has the daily bulletin's asks; elsewhere it remembers what it
/// last read, however old. It trusts what it last saw, the player as much as any AI. Readings are written only by events:
/// world generation (the sector as everyone knew it at the start), each actor's heartbeat and each ship's arrival (what is
/// seen where it is present), the morning bulletin (what is read one jump away), the news (a producer's lot), and a
/// producer turning down an order at its door.
/// </summary>
public static class Knowledge
{
    /// <summary>How long after a producer turns down an order at its door an actor stops counting on it.</summary>
    public const long RefusalHours = 24;

    /// <summary>At world generation: everyone knows every producer's ask and stock as they stand at the start.</summary>
    internal static void Seed(SimulationContext context)
    {
        foreach (ActorState actor in context.ActorsOrdered)
        {
            foreach (ActorState producer in context.ActorsOrdered)
            {
                if (producer.Producer is { } profile)
                {
                    actor.Knowledge.Note(new PriceReading(
                        producer.Id, profile.Good, producer.Home, DecisionEngine.Ask(producer), context.CurrentTick, ReadingSource.Start,
                        producer.Available(profile.Good), context.CurrentTick));
                }
            }
        }
    }

    /// <summary>The actor reads what it sees where it is present: every producer's ask and stock there, live.</summary>
    internal static void Look(SimulationContext context, ActorState actor) => Look(context, actor, Visibility.Of(context, actor));

    /// <summary>Only one place: where a ship of the actor has just arrived.</summary>
    internal static void LookAt(SimulationContext context, ActorState actor, LocationId location)
    {
        actor.Knowledge.NotePresent(location, context.CurrentTick);
        foreach (ActorId id in context.Location(location).Residents)
        {
            if (context.Actor(id) is { Producer: { } profile } producer)
            {
                actor.Knowledge.Note(Live(context, producer, profile));
            }
        }
    }

    /// <summary>
    /// The morning bulletin: every actor reads the posted asks of places one jump from its presence. (What it sees where it is
    /// present it reads on its own heartbeat.)
    /// </summary>
    internal static void ReadBulletin(SimulationContext context)
    {
        if (context.Bulletin is not { Tick: { } posted } list)
        {
            return;
        }

        foreach (ActorState actor in context.ActorsOrdered)
        {
            Sight sight = Visibility.Of(context, actor);
            foreach (ActorState producer in context.ActorsOrdered)
            {
                if (producer.Producer is { } profile && sight.TierOf(producer.Home) == Tier.Near && list.AskOf(producer.Id) is { } ask)
                {
                    actor.Knowledge.Note(new PriceReading(producer.Id, profile.Good, producer.Home, ask, posted, ReadingSource.Daily, null, null));
                }
            }
        }
    }

    private static void Look(SimulationContext context, ActorState actor, Sight sight)
    {
        foreach (LocationId here in sight.Presence)
        {
            actor.Knowledge.NotePresent(here, context.CurrentTick);
        }

        foreach (ActorState producer in context.ActorsOrdered)
        {
            if (producer.Producer is { } profile && sight.TierOf(producer.Home) == Tier.Here)
            {
                actor.Knowledge.Note(Live(context, producer, profile));
            }
        }
    }

    /// <summary>A producer's lot in the news: whoever reads it learns the price it asks a unit, as of its posting.</summary>
    internal static void ReadLot(SimulationContext context, SaleContract lot)
    {
        if (context.Actor(lot.PostedBy) is not { Producer: { } profile } producer || profile.Good != lot.Good || lot.Quantity <= 0m)
        {
            return;
        }

        var reading = new PriceReading(producer.Id, lot.Good, lot.Place, Credits.Of(Math.Round(lot.OfferedPrice.Amount / lot.Quantity, 2)), lot.PostedTick, ReadingSource.News, null, null);
        foreach (ActorState reader in context.ActorsOrdered)
        {
            if (reader.Id != producer.Id && (reader.Knowledge.Stored(producer.Id)?.AskTick ?? long.MinValue) < lot.PostedTick)
            {
                reader.Knowledge.Note(reading);
            }
        }
    }

    /// <summary>
    /// What <paramref name="actor"/> knows of a producer now: its live ask and stock where the actor is present; one jump away,
    /// the latest bulletin's ask (and the stock it last saw); further, what it remembers. Null if it has never known it.
    /// Reading it changes nothing.
    /// </summary>
    public static PriceReading? ReadingOf(SimulationContext context, ActorState actor, ActorState producer, Sight sight)
    {
        if (producer.Producer is not { } profile)
        {
            return null;
        }

        if (producer.Id == actor.Id || sight.TierOf(producer.Home) == Tier.Here)
        {
            return Live(context, producer, profile);
        }

        PriceReading? stored = actor.Knowledge.Stored(producer.Id);
        if (sight.TierOf(producer.Home) == Tier.Near && context.Bulletin is { Tick: { } posted } list && list.AskOf(producer.Id) is { } ask
            && (stored is null || stored.Value.AskTick < posted))
        {
            return new PriceReading(producer.Id, profile.Good, producer.Home, ask, posted, ReadingSource.Daily, stored?.Stock, stored?.StockTick);
        }

        return stored;
    }

    /// <summary>
    /// What an actor counts on buying from a producer at its door, as it knows it (D32: it trusts what it last saw): the ask it
    /// knows, unless its latest reading saw too little stock (<see cref="PriceReading.CurrentStock"/>) or the producer turned it
    /// down in the last day. Null if not.
    /// </summary>
    public static Credits? KnownAsk(SimulationContext context, ActorState actor, ActorState producer, decimal quantity, Sight sight)
    {
        if (ReadingOf(context, actor, producer, sight) is not { } reading || reading.CurrentStock is { } stock && stock < quantity)
        {
            return null;
        }

        if (!reading.IsLive && actor.Knowledge.RefusedAt(producer.Id) is { } refused && context.CurrentTick - refused < RefusalHours)
        {
            return null;
        }

        return reading.Ask;
    }

    /// <summary>
    /// The contracts <paramref name="actor"/> believes open, in id order (design §7.1, D31): those it sees live and are open, and
    /// those The Daily Accord reported open that it can't see live, hasn't found closed and whose deadline hasn't passed. It
    /// can't tell whether one of those has since been taken: it finds out by trying (<see cref="NoteClosed"/>). Its own posts are
    /// among them.
    /// </summary>
    public static List<Contract> OpenContracts(SimulationContext context, ActorState actor, Sight sight)
    {
        var known = new List<Contract>();
        foreach (Contract contract in context.OpenContracts)
        {
            if (sight.CanSee(contract))
            {
                known.Add(contract);
            }
        }

        bool merged = false;
        foreach (ContractId id in context.News.ReportedOpen)
        {
            Contract contract = context.Contract(id);
            if (!sight.CanSee(contract) && FromNews(context, actor, contract))
            {
                known.Add(contract);
                merged = true;
            }
        }

        if (merged)
        {
            known.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
        }

        return known;
    }

    /// <summary>Whether the actor believes a contract open: seen live, as it stands; else as the news left it.</summary>
    public static bool BelievesOpen(SimulationContext context, ActorState actor, Sight sight, Contract contract) =>
        sight.CanSee(contract) ? contract.IsOpen : FromNews(context, actor, contract);

    /// <summary>
    /// Whether the actor knows of a contract only from the news: reported open in The Daily Accord, its deadline not passed, and
    /// not found closed since. Says nothing of whether it is still open.
    /// </summary>
    public static bool FromNews(SimulationContext context, ActorState actor, Contract contract) =>
        context.News.WasReportedOpen(contract.Id) && contract.DeadlineTick > context.CurrentTick && !actor.Knowledge.FoundClosed(contract.Id);

    /// <summary>An actor that tried to take a contract it believed open found it closed: it won't count on it again.</summary>
    internal static void NoteClosed(SimulationContext context, ActorState actor, Contract contract)
    {
        if (context.News.WasReportedOpen(contract.Id))
        {
            actor.Knowledge.NoteClosed(contract.Id);
        }
    }

    /// <summary>A producer turned down the actor's order at its door: the actor doesn't count on it for a day.</summary>
    internal static void NoteRefused(SimulationContext context, ActorState actor, ActorId producer) => actor.Knowledge.NoteRefused(producer, context.CurrentTick);

    private static PriceReading Live(SimulationContext context, ActorState producer, ProducerProfile profile) =>
        new(producer.Id, profile.Good, producer.Home, DecisionEngine.Ask(producer), context.CurrentTick, ReadingSource.Live,
            producer.Available(profile.Good), context.CurrentTick);
}
