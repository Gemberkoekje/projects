using System;
using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Economy;

/// <summary>What a news item reports of its contract: where it stood when the paper went to press.</summary>
public enum NewsKind
{
    /// <summary>Posted, and still open: someone wants goods, offers a lot, or seeks a carrier.</summary>
    Posted,

    /// <summary>Signed and being performed.</summary>
    Signed,

    /// <summary>Delivered in full, on time or late.</summary>
    Delivered,

    /// <summary>Delivered short and paid pro rata.</summary>
    Short,

    /// <summary>Its performer failed by the terms.</summary>
    Failed,

    /// <summary>Cancelled, for default or at will.</summary>
    Cancelled,

    /// <summary>Outside the Accord, its payer didn't pay.</summary>
    Defaulted,

    /// <summary>Nobody took it before its deadline.</summary>
    Expired,
}

/// <summary>
/// One item of The Daily Accord (design §7.4): a contract, where it stood at press time, and the facts its sentence needs that
/// the contract itself won't keep: how late a delivery was, why a performer failed, who cancelled, and, for a Sale, the
/// sector's average price a unit of its goods over the last week (null if none signed).
/// </summary>
public sealed record NewsItem(
    ContractId Contract, NewsKind Kind, long Tick, long TicksLate = 0, FailureReason? Reason = null, Cancellation? Cancellation = null, Credits? AverageUnit = null);

/// <summary>An issue of The Daily Accord: when it went to press, what it reports, and how many contracts it chose from.</summary>
public sealed record NewsIssue(long Tick, IReadOnlyList<NewsItem> Items, int Candidates)
{
    /// <summary>The day it reports on: the day before it went to press.</summary>
    public long Day => GameTime.Day(Tick) - 1;
}

/// <summary>
/// The Daily Accord (design §7.4, D31, D34): once a game day it reports a handful of the previous day's contracts,
/// sector-wide, each where it stood at press time: newly posted, signed, fulfilled or failed. Which contracts make it is a
/// weighted chance (<see cref="Weight"/>): large, unusual and failed contracts are likelier, and so are the frontier's (plan
/// risk R1). Everyone reads it; it is how distant markets reach an actor, a day late and incomplete. A post it reports open
/// is known to every actor from then until its deadline, whether or not it is still open (<see cref="Knowledge.OpenContracts"/>).
/// </summary>
public sealed class NewsDesk
{
    /// <summary>How many issues it keeps to read back.</summary>
    public const int IssuesKept = 14;

    /// <summary>How many days of signed Sales the sector average a unit covers.</summary>
    public const int AverageDays = 7;

    private readonly List<NewsIssue> _issues = new();
    private readonly Dictionary<ContractId, long> _reportedOpen = new();
    private readonly List<ContractId> _openOrder = new();

    // Signed Sales' value and quantity per good, a day at a time, newest last: the sector average a unit.
    private readonly List<Dictionary<GoodType, (decimal Value, decimal Quantity)>> _days = new();

    /// <summary>Issues published, the items they carried, the contracts they chose from, and the items about the frontier and about open posts: telemetry.</summary>
    public long IssuesPublished { get; private set; }

    public long ItemsPublished { get; private set; }

    public long CandidatesSeen { get; private set; }

    public long FrontierItems { get; private set; }

    public long PostedItems { get; private set; }

    /// <summary>The issues kept, oldest first.</summary>
    public IReadOnlyList<NewsIssue> Issues => _issues;

    public NewsIssue? Latest => _issues.Count == 0 ? null : _issues[^1];

    /// <summary>Whether an issue reported the contract open (at any time).</summary>
    public bool WasReportedOpen(ContractId contract) => _reportedOpen.ContainsKey(contract);

    /// <summary>When an issue first reported the contract open; null if none did.</summary>
    public long? ReportedOpenAt(ContractId contract) => _reportedOpen.TryGetValue(contract, out long tick) ? tick : null;

    /// <summary>
    /// The contracts the news has reported open, in the order it reported them, less those whose deadline had passed by the last
    /// issue. Whether one is still open the news doesn't say.
    /// </summary>
    public IReadOnlyList<ContractId> ReportedOpen => _openOrder;

    /// <summary>The sector's average price a unit of a good, over Sales signed in the last week; null if none were.</summary>
    public Credits? AverageUnit(GoodType good)
    {
        decimal value = 0m;
        decimal quantity = 0m;
        foreach (Dictionary<GoodType, (decimal Value, decimal Quantity)> day in _days)
        {
            if (day.TryGetValue(good, out (decimal Value, decimal Quantity) d))
            {
                value += d.Value;
                quantity += d.Quantity;
            }
        }

        return quantity <= 0m ? null : Credits.Of(Math.Round(value / quantity, 2));
    }

    /// <summary>
    /// Go to press (at <see cref="PriceBulletin.Hour"/>): gather every contract with an entry in the journal yesterday, word
    /// each by where it stands now, draw the day's items by weighted chance, and publish. The news makes known the lots that
    /// producers posted, and the player's posts reach the actors who couldn't see them.
    /// </summary>
    internal NewsIssue Publish(SimulationContext context)
    {
        long now = context.CurrentTick;
        long dayEnd = GameTime.At(GameTime.Day(now), 0);
        long dayStart = dayEnd - GameTime.HoursPerDay;
        _openOrder.RemoveAll(id => context.Contract(id).DeadlineTick <= now);

        // Yesterday's contracts and what the journal says of them up to now; and yesterday's signed Sales for the average.
        var touched = new SortedDictionary<long, Contract>();
        var late = new Dictionary<ContractId, long>();
        var reasons = new Dictionary<ContractId, FailureReason>();
        var signed = new Dictionary<GoodType, (decimal Value, decimal Quantity)>();
        IReadOnlyList<JournalEntry> entries = context.Journal.Entries;
        for (int i = entries.Count - 1; i >= 0 && entries[i].Tick >= dayStart; i--)
        {
            JournalEntry entry = entries[i];
            if (entry is not IContractEntry about || !context.Contracts.TryGetValue(about.Contract, out Contract? contract) || !Newsworthy(contract))
            {
                continue;
            }

            switch (entry)
            {
                case GoodsHandedOverEntry handed:
                    late.TryAdd(contract.Id, handed.TicksLate);
                    break;
                case HaulDeliveredEntry delivered:
                    late.TryAdd(contract.Id, delivered.TicksLate);
                    break;
                case DeliveryFailedEntry failed:
                    reasons.TryAdd(contract.Id, failed.Reason);
                    break;
                case ContractSignedEntry s when entry.Tick < dayEnd && contract is SaleContract sale:
                    (decimal value, decimal quantity) = signed.GetValueOrDefault(sale.Good);
                    signed[sale.Good] = (value + s.Price.Amount, quantity + sale.Quantity);
                    break;
            }

            if (entry.Tick < dayEnd)
            {
                touched.TryAdd(contract.Id.Value, contract);
            }
        }

        _days.Add(signed);
        if (_days.Count > AverageDays)
        {
            _days.RemoveAt(0);
        }

        var candidates = new List<(NewsItem Item, decimal Weight)>();
        foreach (Contract contract in touched.Values)
        {
            if (Item(context, contract, late, reasons) is { } item)
            {
                candidates.Add((item, Weight(context, contract, item)));
            }
        }

        var items = new List<NewsItem>();
        int wanted = Math.Min(context.Config.NewsItemsPerDay, candidates.Count);
        while (items.Count < wanted)
        {
            decimal total = candidates.Sum(c => c.Weight);
            decimal draw = context.Rng.NextDecimal(0m, total);
            int pick = candidates.Count - 1;
            for (int i = 0; i < candidates.Count; i++)
            {
                draw -= candidates[i].Weight;
                if (draw < 0m)
                {
                    pick = i;
                    break;
                }
            }

            items.Add(candidates[pick].Item);
            candidates.RemoveAt(pick);
        }

        var issue = new NewsIssue(now, items, touched.Count);
        IssuesPublished++;
        ItemsPublished += items.Count;
        CandidatesSeen += touched.Count;
        FrontierItems += items.Count(i => context.Contract(i.Contract).Places.Any(p => context.Location(p).Reach is AccordReach.OutsideAccord or AccordReach.BlackMarket));
        PostedItems += items.Count(i => i.Kind == NewsKind.Posted);
        _issues.Add(issue);
        if (_issues.Count > IssuesKept)
        {
            _issues.RemoveAt(0);
        }

        foreach (NewsItem item in items)
        {
            Contract contract = context.Contract(item.Contract);
            if (item.Kind == NewsKind.Posted && _reportedOpen.TryAdd(contract.Id, now))
            {
                _openOrder.Add(contract.Id);
                if (Market.HagglesInGameTime(context.Actor(contract.PostedBy)))
                {
                    DecisionEngine.NoticePost(context, contract, fromNews: true);
                }
            }

            if (contract is SaleContract { Side: SaleSide.ForSale } lot && item.Kind is NewsKind.Posted or NewsKind.Signed)
            {
                Knowledge.ReadLot(context, lot);
            }
        }

        return issue;
    }

    // What the news reports: Sales and Hauls, but not an order at a producer's door (bought on the spot, never on a board).
    private static bool Newsworthy(Contract contract) => contract switch
    {
        SaleContract sale => !sale.AtTheDoor,
        HaulContract => true,
        _ => false,
    };

    // The item for a contract, by where it stands now; none for a post taken down.
    private NewsItem? Item(SimulationContext context, Contract contract, Dictionary<ContractId, long> late, Dictionary<ContractId, FailureReason> reasons)
    {
        Credits? average = contract is SaleContract sale ? AverageUnit(sale.Good) : null;
        long tick = context.CurrentTick;
        return contract.Status switch
        {
            ContractStatus.Open => new NewsItem(contract.Id, NewsKind.Posted, contract.PostedTick, AverageUnit: average),
            ContractStatus.Active => new NewsItem(contract.Id, NewsKind.Signed, contract.SignedTick ?? tick, AverageUnit: average),
            ContractStatus.Fulfilled => new NewsItem(contract.Id, NewsKind.Delivered, tick, late.GetValueOrDefault(contract.Id), AverageUnit: average),
            ContractStatus.PartlyFulfilled => new NewsItem(contract.Id, NewsKind.Short, tick, late.GetValueOrDefault(contract.Id), AverageUnit: average),
            ContractStatus.Breached => new NewsItem(contract.Id, NewsKind.Failed, tick, Reason: reasons.TryGetValue(contract.Id, out FailureReason r) ? r : FailureReason.NotDelivered, AverageUnit: average),
            ContractStatus.Cancelled => new NewsItem(contract.Id, NewsKind.Cancelled, contract.Cancellation?.Tick ?? tick, Cancellation: contract.Cancellation, AverageUnit: average),
            ContractStatus.Defaulted => new NewsItem(contract.Id, NewsKind.Defaulted, tick, AverageUnit: average),
            ContractStatus.Expired => new NewsItem(contract.Id, NewsKind.Expired, contract.DeadlineTick, AverageUnit: average),
            _ => null,
        };
    }

    /// <summary>
    /// How likely a contract is to make the news (design §7.4; first-pass weights, an open question in design A.2): its size
    /// (the square root of its price against ₵500, from 0.5 to 4); how unusual it is (a Sale's price a unit against the
    /// sector's average, a haul's fee against the usual offer: 1 plus twice the gap, at most 4); how it went (a failure,
    /// cancellation or default 4, a late or short delivery 2, an open post or an expiry 1.5); and the frontier
    /// (<see cref="SimulationConfig.NewsFrontierWeight"/> for a contract with a place outside the Accord).
    /// </summary>
    public static decimal Weight(SimulationContext context, Contract contract, NewsItem item)
    {
        decimal size = Math.Clamp((decimal)Math.Sqrt((double)Math.Max(0m, contract.Price.Amount) / 500.0), 0.5m, 4m);
        decimal ratio = 1m;
        switch (contract)
        {
            case SaleContract sale when item.AverageUnit is { Amount: > 0m } average:
                ratio = sale.UnitPrice.Amount / average.Amount;
                break;
            case HaulContract haul when DecisionEngine.UsualHaulOffer(context, haul.Origin, haul.Destination) is { Amount: > 0m } usual:
                ratio = haul.Price.Amount / usual.Amount;
                break;
        }

        decimal novelty = Math.Min(4m, 1m + (2m * Math.Abs(ratio - 1m)));
        decimal outcome = item.Kind switch
        {
            NewsKind.Failed or NewsKind.Cancelled or NewsKind.Defaulted => 4m,
            NewsKind.Short => 2m,
            NewsKind.Delivered when item.TicksLate > 0 => 2m,
            NewsKind.Posted or NewsKind.Expired => 1.5m,
            _ => 1m,
        };
        bool frontier = contract.Places.Any(p => context.Location(p).Reach is AccordReach.OutsideAccord or AccordReach.BlackMarket);
        return size * novelty * outcome * (frontier ? context.Config.NewsFrontierWeight : 1m);
    }
}
