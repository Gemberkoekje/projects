using System;
using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Engine.Actors;

/// <summary>A negotiating side: its hidden walk-away (reservation) price and how fast it concedes (design §3.6).</summary>
public readonly record struct NegotiationParty(Credits Reservation, decimal ConcessionRate, bool IsBuyer);

/// <summary>Result of a closed-form negotiation.</summary>
public readonly record struct NegotiationOutcome(bool Agreed, Credits Fee);

/// <summary>
/// The rules of haggling (design §3.6, §16.5), shared by every side. Each side has a hidden reservation point: the least it
/// would perform for, or the most it would pay, on the terms on the table. Each round the side that concedes moves part of
/// the way toward it, by its eagerness (<see cref="Personality.ConcessionRate"/>); it accepts an offer that crosses it, and
/// walks away when the gap is too wide or the rounds run out. A reply takes 1–12 game hours, quicker for an eager side and
/// slower near its limit, and reads readily, reluctantly or at its limit: both are tells. AI-to-AI haggles play the same
/// rules out at once (<see cref="Resolve"/>), so the world stays cheap to simulate.
/// </summary>
public static class Haggling
{
    /// <summary>A side walks away from an offer further than this from its reservation (as a share of it).</summary>
    public const decimal WalkAwayGap = 0.35m;

    /// <summary>The quickest and slowest replies, in game hours (design §16.5).</summary>
    public const long FastestReply = 1;

    public const long SlowestReply = 12;

    /// <summary>
    /// An AI-to-AI haggle in closed form (design §3.6): the rounds the interactive rules would run, played out at once. The
    /// post is the poster's first offer; a responder it doesn't satisfy, and that isn't too far from it, counters at its
    /// opening; from then each side accepts the other's offer once it crosses its own reservation, walks away from one too far
    /// from it, or concedes part of the way toward its reservation, until the rounds run out. The price is the offer accepted.
    /// </summary>
    public static NegotiationOutcome Resolve(NegotiationParty poster, Credits posted, NegotiationParty responder, int maxRounds)
    {
        if (Crosses(posted, responder.Reservation, responder.IsBuyer))
        {
            return new NegotiationOutcome(true, posted);
        }

        if (TooWide(posted, responder.Reservation, responder.IsBuyer))
        {
            return new NegotiationOutcome(false, Credits.Zero);
        }

        Credits posterOffer = posted;
        Credits responderOffer = Opening(responder.Reservation, responder.IsBuyer, responder.ConcessionRate);
        for (int round = 1; ; round++)
        {
            // The side whose turn it is answers the other's latest offer.
            bool posterAnswers = round % 2 == 1;
            NegotiationParty answering = posterAnswers ? poster : responder;
            Credits offered = posterAnswers ? responderOffer : posterOffer;
            if (Crosses(offered, answering.Reservation, answering.IsBuyer))
            {
                return new NegotiationOutcome(true, offered);
            }

            if (TooWide(offered, answering.Reservation, answering.IsBuyer) || round >= maxRounds)
            {
                return new NegotiationOutcome(false, Credits.Zero);
            }

            if (posterAnswers)
            {
                posterOffer = Concede(posterOffer, poster.Reservation, poster.ConcessionRate);
            }
            else
            {
                responderOffer = Concede(responderOffer, responder.Reservation, responder.ConcessionRate);
            }
        }
    }

    /// <summary>
    /// A side's opening counter when it answers a post it won't take as posted: above its reservation for a performer, below
    /// it for a payer, by a margin a slow conceder keeps wider.
    /// </summary>
    public static Credits Opening(Credits reservation, bool pays, decimal concession)
    {
        decimal margin = 0.08m + (0.25m * (1m - concession));
        return Credits.Of(Math.Round(reservation.Amount * (pays ? 1m - margin : 1m + margin), 2));
    }

    /// <summary>The next counter: the side's standing offer moved part of the way toward its reservation, never past it.</summary>
    public static Credits Concede(Credits standing, Credits reservation, decimal concession)
    {
        decimal next = standing.Amount + ((reservation.Amount - standing.Amount) * concession);
        return Credits.Of(Math.Round(next, 2));
    }

    /// <summary>Whether an offer at <paramref name="price"/> crosses a side's reservation: at most it for a payer, at least it for a performer.</summary>
    public static bool Crosses(Credits price, Credits reservation, bool pays) =>
        pays ? price.Amount <= reservation.Amount : price.Amount >= reservation.Amount;

    /// <summary>Whether an offer is too far from a side's reservation to keep talking.</summary>
    public static bool TooWide(Credits price, Credits reservation, bool pays) =>
        pays
            ? price.Amount > reservation.Amount * (1m + WalkAwayGap)
            : price.Amount < reservation.Amount * (1m - WalkAwayGap);

    /// <summary>How a reply at <paramref name="price"/> reads, by how far it is from the side's reservation.</summary>
    public static Tell TellFor(Credits price, Credits reservation)
    {
        decimal headroom = reservation.Amount <= 0m ? 1m : Math.Abs(price.Amount - reservation.Amount) / reservation.Amount;
        return headroom switch
        {
            < 0.04m => Tell.AtLimit,
            < 0.15m => Tell.Reluctant,
            _ => Tell.Readily,
        };
    }

    /// <summary>
    /// How many game hours a reply takes (design §16.5): an eager side answers within an hour or two, a slow one in a few,
    /// and one near its limit takes longer still, up to twelve. A deterministic hour of spread by negotiation and round keeps
    /// replies from arriving in lockstep without drawing randomness outside an event.
    /// </summary>
    public static long ReplyHours(decimal concession, Tell tell, long spread)
    {
        decimal closeness = tell switch
        {
            Tell.AtLimit => 1m,
            Tell.Reluctant => 0.5m,
            _ => 0m,
        };
        decimal hours = 1m + ((1m - Math.Clamp(concession, 0m, 1m)) * 4m) + (closeness * 6m) + (Math.Abs(spread) % 2);
        return Math.Clamp((long)Math.Round(hours, MidpointRounding.AwayFromZero), FastestReply, SlowestReply);
    }
}
