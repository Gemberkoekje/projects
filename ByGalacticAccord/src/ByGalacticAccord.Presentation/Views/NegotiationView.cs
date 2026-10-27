using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Text;

namespace ByGalacticAccord.Presentation.Views;

/// <summary>One choice in a form's list: its value and how it reads.</summary>
public sealed record Choice<T>(T Value, string Label);

/// <summary>
/// An offer as a form (design §3.6, §16.5): its price, its deadline, its term package and its late fee, starting from an
/// offer on the table, with the choices a counter can name for each.
/// </summary>
public sealed record OfferFormModel(
    decimal Price,
    long Deadline,
    TermPackage Package,
    decimal LateFeeScale,
    IReadOnlyList<Choice<long>> Deadlines,
    IReadOnlyList<Choice<TermPackage>> Packages,
    IReadOnlyList<Choice<decimal>> LateFees,
    string PriceLabel,
    string DeadlineLabel,
    string PackageLabel,
    string LateFeeLabel,
    string SubmitLabel,
    string Note);

/// <summary>One offer in a haggle: who made it, when, its terms, and for a reply how long it took and how it read.</summary>
public sealed record BidLine(string Who, string When, string Offer, string? Note, bool Yours);

/// <summary>
/// A haggle as a thread (design §11.3, "Negotiations"): the offers so far, each reply with its tell, where it stands (whose
/// turn, when an answer can come at the latest, or how it ended), and what the player can do when it is its turn.
/// </summary>
public sealed record ThreadViewModel(
    NegotiationId Id,
    ContractId Contract,
    string Title,
    string Status,
    Tone StatusTone,
    IReadOnlyList<BidLine> Bids,
    bool YourTurn,
    string AcceptLabel,
    string AcceptNote,
    string DeclineLabel,
    OfferFormModel? Counter);

/// <summary>Builds haggles as threads and offers as forms, and words what came of the player's haggling.</summary>
public static class NegotiationView
{
    /// <summary>How far a counter may move a deadline, in hours either way from the one on the table.</summary>
    private static readonly long[] DeadlineSteps = { -24, -12, 0, 12, 24, 48, 72 };

    public static ThreadViewModel Thread(GameSession session, Negotiation thread)
    {
        SimulationContext context = session.Context;
        ActorId player = session.Player.Id;
        string party = context.Actor(thread.Other(player)).Name;
        int max = context.Config.NegotiationMaxRounds;
        var bids = new List<BidLine>();
        foreach (Bid bid in thread.Bids)
        {
            bool yours = bid.By == player;
            string who = bid.Round == 0
                ? Templates.Fill(yours ? "Thread.Posted.You" : "Thread.Posted", ("party", party))
                : yours ? Templates.Get("Thread.You") : party;
            string? note = bid.ReplyHours is { } hours
                ? bid.Tell == Tell.None
                    ? Templates.Fill("Reply.Plain", ("hours", Format.Duration(hours)))
                    : Templates.Fill("Reply.Tell", ("hours", Format.Duration(hours)), ("tell", Templates.Tell(bid.Tell)))
                : null;
            bids.Add(new BidLine(who, Format.Date(bid.Tick), Templates.Offer(bid.Offer), note, yours));
        }

        bool yourTurn = thread.IsOpen && thread.AwaitingFrom == player;
        (string status, Tone tone) = Status(session, thread, party);
        OfferFormModel? counter = yourTurn && thread.Rounds < max
            ? Form(session, thread.Latest.Offer, Templates.Get("Thread.Counter.Send"), Templates.Get("Thread.Counter.Note"))
            : null;
        Contract contract = context.Contract(thread.Contract);
        decimal posted = contract.OfferedPrice.Amount;
        decimal difference = thread.Latest.Offer.Price.Amount - posted;
        string acceptNote = difference == 0m
            ? Templates.Get("Thread.Accept.Note.Posted")
            : Templates.Fill(difference > 0m ? "Thread.Accept.Note.More" : "Thread.Accept.Note.Less", ("difference", Format.Money(System.Math.Abs(difference))));
        return new ThreadViewModel(
            thread.Id, thread.Contract,
            Templates.Fill("Thread.Title", ("party", party), ("round", thread.Rounds.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("max", max.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            status, tone, bids, yourTurn,
            Templates.Fill("Thread.Accept", ("price", Format.Money(thread.Latest.Offer.Price))), acceptNote, Templates.Get("Thread.Decline"), counter);
    }

    /// <summary>
    /// A form starting from <paramref name="from"/>: the deadline may move a day earlier or up to three days later (never into
    /// the past), the package and late fee to any the Accord standard names.
    /// </summary>
    public static OfferFormModel Form(GameSession session, Offer from, string submit, string note)
    {
        long now = session.Now;
        var deadlines = new List<Choice<long>>();
        foreach (long step in DeadlineSteps)
        {
            long tick = from.Deadline + step;
            if (tick > now)
            {
                deadlines.Add(new Choice<long>(tick, Templates.Fill(step == 0 ? "Form.Deadline.Same" : "Form.Deadline", ("date", Format.Date(tick)), ("when", Format.Relative(tick, now)))));
            }
        }

        if (deadlines.Count == 0)
        {
            deadlines.Add(new Choice<long>(now + 12, Templates.Fill("Form.Deadline", ("date", Format.Date(now + 12)), ("when", Format.Relative(now + 12, now)))));
        }

        long deadline = deadlines.Any(d => d.Value == from.Deadline) ? from.Deadline : deadlines[0].Value;
        return new OfferFormModel(
            from.Price.Amount, deadline, from.Package, from.LateFeeScale, deadlines, Packages(), LateFees(),
            Templates.Get("Form.Price"), Templates.Get("Form.Deadline.Label"), Templates.Get("Form.Package"), Templates.Get("Form.LateFee.Label"), submit, note);
    }

    /// <summary>The three term packages, as choices.</summary>
    public static IReadOnlyList<Choice<TermPackage>> Packages() =>
        new[] { TermPackage.Strict, TermPackage.Standard, TermPackage.Lenient }.Select(p => new Choice<TermPackage>(p, Templates.Package(p))).ToArray();

    /// <summary>The late fees a post or counter may name, as shares of the package's.</summary>
    public static IReadOnlyList<Choice<decimal>> LateFees() =>
        Offer.LateFeeScales.Select(s => new Choice<decimal>(s, Templates.Fill("Form.LateFee", ("share", Templates.LateFee(s))))).ToArray();

    /// <summary>What came of the player's counter: sent, or why not.</summary>
    public static string CounterText(GameSession session, Contract contract, DealBlock block, Negotiation? thread)
    {
        if (block != DealBlock.None)
        {
            return ContractView.BlockText(session, contract, block);
        }

        ActorId other = thread?.Other(session.Player.Id) ?? contract.PostedBy;
        return Templates.Fill("Outcome.Countered", ("party", session.Context.Actor(other).Name));
    }

    /// <summary>What came of accepting an offer in a haggle.</summary>
    public static string AcceptText(GameSession session, Negotiation thread, Signing signing)
    {
        string party = session.Context.Actor(thread.Other(session.Player.Id)).Name;
        Contract contract = session.Context.Contract(thread.Contract);
        return signing switch
        {
            Signing.Signed => Templates.Fill("Outcome.OfferAccepted", ("party", party), ("price", Format.Money(contract.Price))),
            Signing.CannotPerform => Templates.Fill(
                thread.Responder == session.Player.Id ? "Outcome.CannotPerform.You" : "Outcome.CannotPerform", ("party", party)),
            Signing.PayerCannotFund => Templates.Fill("Outcome.PayerCannotFund", ("poster", session.Context.Actor(contract.PostedBy).Name)),
            _ => Templates.Get("Block.NotOpen"),
        };
    }

    /// <summary>What came of declining an offer.</summary>
    public static string DeclineText(GameSession session, Negotiation thread, bool done) =>
        done ? Templates.Fill("Outcome.Declined", ("party", session.Context.Actor(thread.Other(session.Player.Id)).Name)) : Templates.Get("Block.NotOpen");

    private static (string Text, Tone Tone) Status(GameSession session, Negotiation thread, string party)
    {
        SimulationContext context = session.Context;
        ActorId player = session.Player.Id;
        switch (thread.Status)
        {
            case NegotiationStatus.Open when thread.AwaitingFrom == player:
                return (Templates.Fill(thread.Rounds >= context.Config.NegotiationMaxRounds ? "Thread.YourTurn.Last" : "Thread.YourTurn", ("party", party)), Tone.Warn);
            case NegotiationStatus.Open:
                return (Templates.Fill(
                    "Thread.Waiting", ("party", party),
                    ("latest", Format.Date(thread.Latest.Tick + Haggling.SlowestReply)), ("fastest", Format.Duration(Haggling.FastestReply)),
                    ("slowest", Format.Duration(Haggling.SlowestReply))), Tone.Muted);
            case NegotiationStatus.Agreed:
                return (Templates.Fill("Thread.Agreed", ("price", Format.Money(context.Contract(thread.Contract).Price))), Tone.Gain);
        }

        string by = thread.ClosedBy == player ? ".You" : string.Empty;
        return (Templates.Fill("Thread.Ended." + thread.End + by, ("party", party)), thread.End is NegotiationEnd.PostClosed ? Tone.Muted : Tone.Loss);
    }
}
