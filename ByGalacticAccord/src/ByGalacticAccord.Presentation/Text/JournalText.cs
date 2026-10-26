using System;
using System.Collections.Generic;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Presentation.Text;

/// <summary>
/// Turns a journal entry into its sentence (design §11.5): the template keyed by the entry's type and the contract's
/// kind, filled from the entry's own fields and the contract it names. Where the player is the entry's subject the
/// template's ".You" twin words it in the second person; where the player is on the receiving end, its ".ToYou" twin.
/// </summary>
public static class JournalText
{
    /// <summary>The sentence for <paramref name="entry"/>.</summary>
    public static string Describe(SimulationContext context, JournalEntry entry) => entry switch
    {
        ContractPostedEntry e => Templates.Fill(Key("ContractPosted." + Kind(context, e.Contract), context, e.Poster), Facts(context, e.Contract)),
        ContractSignedEntry e => Signed(context, e),
        GoodsHandedOverEntry e => HandedOver(context, e),
        HaulDeliveredEntry e => Delivered(context, e),
        PaymentDefaultedEntry e => Templates.Fill(
            Key("PaymentDefaulted." + Kind(context, e.Contract, side: false), context, e.Payer, e.Payee),
            Facts(context, e.Contract, ("payer", Name(context, e.Payer)), ("payee", Name(context, e.Payee)))),
        DeliveryFailedEntry e => Failed(context, e),
        ContractExpiredEntry e => Templates.Fill(
            Key("ContractExpired." + Kind(context, e.Contract), context, context.Contract(e.Contract).PostedBy), Facts(context, e.Contract)),
        ContractWithdrawnEntry e => Templates.Fill(
            Key("ContractWithdrawn." + Kind(context, e.Contract), context, context.Contract(e.Contract).PostedBy), Facts(context, e.Contract)),
        StorageOpenedEntry e => Templates.Fill(Key("StorageOpened", context, e.Renter), Facts(context, e.Contract, ("renter", Name(context, e.Renter)))),
        StorageClosedEntry e => Templates.Fill(
            Key("StorageClosed", context, e.Renter), Facts(context, e.Contract, ("renter", Name(context, e.Renter)), ("rent", Format.Money(e.RentPaid)))),
        IncidentEntry e => Incident(context, e),
        FaucetAdjustedEntry e => Templates.Fill("FaucetAdjusted", ("drift", Format.Percent(e.Drift)), ("multiplier", e.Multiplier.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))),
        _ => throw new ArgumentOutOfRangeException(nameof(entry), entry, "No template for this journal entry."),
    };

    /// <summary>The actor an entry is about first: who posted, took, handed over, delivered, defaulted, failed or was struck.</summary>
    public static ActorId? Subject(JournalEntry entry) => entry switch
    {
        ContractPostedEntry e => e.Poster,
        ContractSignedEntry e => e.Taker,
        GoodsHandedOverEntry e => e.Seller,
        HaulDeliveredEntry e => e.Hauler,
        PaymentDefaultedEntry e => e.Payer,
        DeliveryFailedEntry e => e.Performer,
        StorageOpenedEntry e => e.Renter,
        StorageClosedEntry e => e.Renter,
        IncidentEntry e => e.Hauler,
        _ => null,
    };

    /// <summary>The contract an entry concerns, if any.</summary>
    public static ContractId? ContractOf(JournalEntry entry) => entry switch
    {
        ContractPostedEntry e => e.Contract,
        ContractSignedEntry e => e.Contract,
        GoodsHandedOverEntry e => e.Contract,
        HaulDeliveredEntry e => e.Contract,
        PaymentDefaultedEntry e => e.Contract,
        DeliveryFailedEntry e => e.Contract,
        ContractExpiredEntry e => e.Contract,
        ContractWithdrawnEntry e => e.Contract,
        StorageOpenedEntry e => e.Contract,
        StorageClosedEntry e => e.Contract,
        IncidentEntry e => e.Contract,
        _ => null,
    };

    /// <summary>A contract's kind as a template key part: "Sale.Wanted", "Sale.ForSale", "Haul", "Storage".</summary>
    public static string Kind(Contract contract, bool side = true) => contract switch
    {
        SaleContract sale when side => "Sale." + sale.Side,
        _ => contract.Type.ToString(),
    };

    private static string Kind(SimulationContext context, ContractId id, bool side = true) => Kind(context.Contract(id), side);

    private static string Signed(SimulationContext context, ContractSignedEntry e)
    {
        Contract contract = context.Contract(e.Contract);
        return Templates.Fill(
            Key("ContractSigned." + Kind(contract), context, e.Taker, contract.PostedBy),
            Facts(context, e.Contract, ("taker", Name(context, e.Taker)), ("signed", Format.Money(e.Price))));
    }

    private static string HandedOver(SimulationContext context, GoodsHandedOverEntry e)
    {
        var facts = Facts(
            context, e.Contract,
            ("seller", Name(context, e.Seller)),
            ("buyer", Name(context, e.Buyer)),
            ("delivered", Format.Quantity(e.Quantity)),
            ("paid", Format.Money(e.Price)),
            ("late", Format.Duration(Math.Max(0, e.TicksLate))),
            ("lateFee", Format.Money(e.LateFee)));
        string sentence = Templates.Fill(Key(e.TicksLate > 0 ? "GoodsHandedOver.Late" : "GoodsHandedOver.OnTime", context, e.Seller, e.Buyer), facts);
        return Refund(sentence, e.Quality, e.QualityRefund);
    }

    private static string Delivered(SimulationContext context, HaulDeliveredEntry e)
    {
        var facts = Facts(
            context, e.Contract,
            ("hauler", Name(context, e.Hauler)),
            ("delivered", Format.Quantity(e.Quantity)),
            ("fee", Format.Money(e.Fee)),
            ("late", Format.Duration(Math.Max(0, e.TicksLate))),
            ("lateFee", Format.Money(e.LateFee)));
        string sentence = Templates.Fill(
            Key(e.TicksLate > 0 ? "HaulDelivered.Late" : "HaulDelivered.OnTime", context, e.Hauler, context.Contract(e.Contract).PostedBy), facts);
        return Refund(sentence, e.Quality, e.QualityRefund);
    }

    private static string Refund(string sentence, decimal quality, Credits refund) =>
        refund.Amount > 0m
            ? sentence + " " + Templates.Fill("Delivered.Refund", ("quality", Format.Percent(quality)), ("refund", Format.Money(refund)))
            : sentence;

    private static string Failed(SimulationContext context, DeliveryFailedEntry e)
    {
        Contract contract = context.Contract(e.Contract);
        return Templates.Fill(
            Key("DeliveryFailed." + e.Reason + "." + Kind(contract, side: false), context, e.Performer, contract.Payer),
            Facts(context, e.Contract, ("performer", Name(context, e.Performer))));
    }

    private static string Incident(SimulationContext context, IncidentEntry e)
    {
        TransitIncident incident = e.Incident;
        var facts = Facts(
            context, e.Contract,
            ("hauler", Name(context, e.Hauler)),
            ("delay", Format.Duration(Math.Max(0, incident.DelayTicks))),
            ("qualityLoss", Format.Points(incident.QualityLoss)),
            ("lostShare", Format.Percent(incident.QuantityLossFraction)),
            ("cost", Format.Money(incident.CreditCost)));
        return Templates.Fill(Key("Incident." + incident.Kind, context, e.Hauler), facts);
    }

    // The player's own events read in the second person: ".You" when the player is the subject, ".ToYou" when it is
    // the other party the sentence names.
    private static string Key(string key, SimulationContext context, ActorId subject, ActorId? other = null)
    {
        if (subject == context.PlayerActorId)
        {
            return key + ".You";
        }

        return other is { } o && o == context.PlayerActorId ? key + ".ToYou" : key;
    }

    // The facts every contract sentence can draw on: its good, quantity, places, poster, parties and posted price.
    private static (string Name, string Value)[] Facts(SimulationContext context, ContractId id, params (string Name, string Value)[] extra)
    {
        Contract c = context.Contract(id);
        var facts = new List<(string Name, string Value)>
        {
            ("poster", Name(context, c.PostedBy)),
            ("price", Format.Money(c.OfferedPrice)),
            ("kind", Templates.ContractKind(c.Type)),
        };

        switch (c)
        {
            case SaleContract sale:
                string place = context.Location(sale.Place).Name;
                facts.Add(("good", Templates.Good(sale.Good)));
                facts.Add(("quantity", Format.Quantity(sale.Quantity)));
                facts.Add(("place", place));
                facts.Add(("origin", place));
                facts.Add(("destination", place));
                facts.Add(("buyer", sale.Buyer is { } b ? Name(context, b) : string.Empty));
                break;
            case HaulContract haul:
                facts.Add(("good", Templates.Good(haul.Good)));
                facts.Add(("quantity", Format.Quantity(haul.Quantity)));
                facts.Add(("place", context.Location(haul.Destination).Name));
                facts.Add(("origin", context.Location(haul.Origin).Name));
                facts.Add(("destination", context.Location(haul.Destination).Name));
                break;
            case StorageContract storage:
                facts.Add(("place", context.Location(storage.Location).Name));
                break;
        }

        // A sentence's own fields come first, so they win over the contract's.
        var result = new List<(string Name, string Value)>(extra);
        result.AddRange(facts);
        return result.ToArray();
    }

    private static string Name(SimulationContext context, ActorId id) => context.Actor(id).Name;
}
