using System;
using System.Collections.Generic;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Presentation.Text;

/// <summary>
/// Words an item of The Daily Accord (design §7.4, §11.5): a template keyed by what it reports and the contract's kind
/// (<c>News.Posted.Sale.Wanted</c>, <c>News.Failed.Haul</c>, …), filled from the contract's fields and the facts the item
/// kept at press time. Headline style: a party's name is never the subject of a verb, so the player ("You") reads as well as
/// anyone. A Sale's price a unit is set against the sector's average that week when it is a tenth or more away from it.
/// </summary>
public static class NewsText
{
    public static string Describe(SimulationContext context, NewsItem item)
    {
        Contract contract = context.Contract(item.Contract);
        string key = item.Kind switch
        {
            NewsKind.Posted or NewsKind.Signed or NewsKind.Expired => "News." + item.Kind + "." + JournalText.Kind(contract),
            NewsKind.Cancelled => "News.Cancelled." + (item.Cancellation?.Reason ?? CancelReason.AtWill) + "." + JournalText.Kind(contract, side: false),
            _ => "News." + item.Kind + "." + JournalText.Kind(contract, side: false),
        };

        return Templates.Fill(key, Facts(context, contract, item).ToArray());
    }

    private static List<(string Name, string Value)> Facts(SimulationContext context, Contract contract, NewsItem item)
    {
        string Name(ActorId? id) => id is { } a ? context.Actor(a).Name : string.Empty;
        var facts = new List<(string Name, string Value)>
        {
            ("poster", Name(contract.PostedBy)),
            ("taker", Name(contract.CounterpartyId)),
            ("payer", Name(contract.Payer)),
            ("performer", Name(contract.Performer)),
            ("price", Format.Money(contract.Price)),
            ("deadline", Format.Date(contract.DeadlineTick)),
            ("when", item.TicksLate > 0 ? Templates.Fill("News.Late", ("late", Format.Duration(item.TicksLate))) : Templates.Get("News.OnTime")),
            ("reason", Templates.Get("News.Reason." + (item.Reason ?? FailureReason.NotDelivered))),
            ("by", Name(item.Cancellation?.By)),
        };

        switch (contract)
        {
            case SaleContract sale:
                facts.Add(("quantity", Format.Quantity(sale.Quantity)));
                facts.Add(("good", Templates.Good(sale.Good)));
                facts.Add(("place", context.Location(sale.Place).Name));
                facts.Add(("unit", Format.MoneyCents(item.Kind == NewsKind.Posted || contract.SignedTick is null ? sale.OfferedPrice.Amount / Math.Max(1m, sale.Quantity) : sale.UnitPrice.Amount)));
                facts.Add(("versus", Versus(item.Kind == NewsKind.Posted || contract.SignedTick is null ? Credits.Of(sale.OfferedPrice.Amount / Math.Max(1m, sale.Quantity)) : sale.UnitPrice, item.AverageUnit)));
                facts.Add(("seller", Name(sale.Seller)));
                facts.Add(("buyer", Name(sale.Buyer)));
                break;
            case HaulContract haul:
                facts.Add(("quantity", Format.Quantity(haul.Quantity)));
                facts.Add(("good", Templates.Good(haul.Good)));
                facts.Add(("origin", context.Location(haul.Origin).Name));
                facts.Add(("destination", context.Location(haul.Destination).Name));
                facts.Add(("fee", Format.Money(haul.Price)));
                facts.Add(("carrier", Name(haul.Carrier)));
                facts.Add(("shipper", Name(haul.Shipper)));
                break;
        }

        return facts;
    }

    // ", 75% above the week's average of ₵12.00", or nothing when it is within a tenth of it or nothing was signed.
    private static string Versus(Credits unit, Credits? average)
    {
        if (average is not { Amount: > 0m } avg)
        {
            return string.Empty;
        }

        decimal ratio = unit.Amount / avg.Amount;
        if (Math.Abs(ratio - 1m) < 0.1m)
        {
            return string.Empty;
        }

        return Templates.Fill(
            ratio > 1m ? "News.Versus.Above" : "News.Versus.Below", ("pct", Format.Percent(Math.Abs(ratio - 1m))), ("average", Format.MoneyCents(avg)));
    }
}
