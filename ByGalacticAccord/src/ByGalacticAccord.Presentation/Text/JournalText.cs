using System;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Presentation.Text;

/// <summary>
/// Turns a journal entry into its sentence (design §11.5): the template keyed by the entry's type, filled
/// from the entry's own fields and the contract it names. The player's own events use each template's
/// ".You" twin, worded in the second person.
/// </summary>
public static class JournalText
{
    /// <summary>The sentence for <paramref name="entry"/>.</summary>
    public static string Describe(SimulationContext context, JournalEntry entry) => entry switch
    {
        ContractPostedEntry e => Templates.Fill("ContractPosted", Haul(context, e.Contract, ("supplier", Name(context, e.Supplier)))),
        HaulTakenEntry e => Templates.Fill(Key("HaulTaken", context, e.Hauler), Haul(context, e.Contract, ("hauler", Name(context, e.Hauler)), ("fee", Format.Money(e.Fee)))),
        HaulDeliveredEntry e => Delivered(context, e),
        PaymentDefaultedEntry e => Templates.Fill(Key("PaymentDefaulted", context, e.Hauler), Haul(context, e.Contract, ("payer", Name(context, e.Payer)), ("hauler", Name(context, e.Hauler)))),
        DeliveryFailedEntry e => Templates.Fill(Key("DeliveryFailed", context, e.Hauler), Haul(context, e.Contract, ("hauler", Name(context, e.Hauler)))),
        ContractExpiredEntry e => Templates.Fill("ContractExpired", Haul(context, e.Contract)),
        IncidentEntry e => Incident(context, e),
        FaucetAdjustedEntry e => Templates.Fill("FaucetAdjusted", ("drift", Format.Percent(e.Drift)), ("multiplier", e.Multiplier.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture))),
        _ => throw new ArgumentOutOfRangeException(nameof(entry), entry, "No template for this journal entry."),
    };

    /// <summary>The actor an entry is about first: who posted, took, delivered or was struck.</summary>
    public static ActorId? Subject(JournalEntry entry) => entry switch
    {
        ContractPostedEntry e => e.Poster,
        HaulTakenEntry e => e.Hauler,
        HaulDeliveredEntry e => e.Hauler,
        PaymentDefaultedEntry e => e.Payer,
        DeliveryFailedEntry e => e.Hauler,
        IncidentEntry e => e.Hauler,
        _ => null,
    };

    /// <summary>The contract an entry concerns, if any.</summary>
    public static ContractId? ContractOf(JournalEntry entry) => entry switch
    {
        ContractPostedEntry e => e.Contract,
        HaulTakenEntry e => e.Contract,
        HaulDeliveredEntry e => e.Contract,
        PaymentDefaultedEntry e => e.Contract,
        DeliveryFailedEntry e => e.Contract,
        ContractExpiredEntry e => e.Contract,
        IncidentEntry e => e.Contract,
        _ => null,
    };

    private static string Delivered(SimulationContext context, HaulDeliveredEntry e)
    {
        var facts = Haul(
            context, e.Contract,
            ("hauler", Name(context, e.Hauler)),
            ("delivered", Format.Quantity(e.Quantity)),
            ("fee", Format.Money(e.Fee)),
            ("late", Format.Duration(Math.Max(0, e.TicksLate))),
            ("lateFee", Format.Money(e.LateFee)));
        string sentence = Templates.Fill(Key(e.TicksLate > 0 ? "HaulDelivered.Late" : "HaulDelivered.OnTime", context, e.Hauler), facts);
        if (e.QualityRefund.Amount > 0m)
        {
            sentence += " " + Templates.Fill("HaulDelivered.Refund", ("quality", Format.Percent(e.Quality)), ("refund", Format.Money(e.QualityRefund)));
        }

        return sentence;
    }

    private static string Incident(SimulationContext context, IncidentEntry e)
    {
        TransitIncident incident = e.Incident;
        var facts = Haul(
            context, e.Contract,
            ("hauler", Name(context, e.Hauler)),
            ("delay", Format.Duration(Math.Max(0, incident.DelayTicks))),
            ("qualityLoss", Format.Points(incident.QualityLoss)),
            ("lostShare", Format.Percent(incident.QuantityLossFraction)),
            ("cost", Format.Money(incident.CreditCost)));
        return Templates.Fill(Key("Incident." + incident.Kind, context, e.Hauler), facts);
    }

    // The player's own events read in the second person: each such template has a ".You" twin.
    private static string Key(string key, SimulationContext context, ActorId actor) =>
        actor == context.PlayerActorId ? key + ".You" : key;

    // The facts every haul sentence can draw on: the contract's own good, quantity, places, poster and posted fee.
    private static (string Name, string Value)[] Haul(SimulationContext context, ContractId id, params (string Name, string Value)[] extra)
    {
        Contract c = context.Contract(id);
        var facts = new (string Name, string Value)[extra.Length + 6];
        facts[0] = ("good", Templates.Good(c.Good));
        facts[1] = ("quantity", Format.Quantity(c.Quantity));
        facts[2] = ("origin", context.Location(c.OriginLocationId).Name);
        facts[3] = ("destination", context.Location(c.DestinationLocationId).Name);
        facts[4] = ("postedFee", Format.Money(c.OfferedPayment));
        facts[5] = ("poster", Name(context, c.PostedBy));
        Array.Copy(extra, 0, facts, 6, extra.Length);
        return facts;
    }

    private static string Name(SimulationContext context, ActorId id) => context.Actor(id).Name;
}
