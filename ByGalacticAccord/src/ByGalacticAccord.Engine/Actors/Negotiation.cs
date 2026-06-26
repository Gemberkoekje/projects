using System;
using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Engine.Actors;

/// <summary>A negotiating side: its hidden walk-away (reservation) price and how fast it concedes (§4.1.6).</summary>
public readonly record struct NegotiationParty(Credits Reservation, decimal ConcessionRate, bool IsBuyer);

/// <summary>Result of a concluded negotiation.</summary>
public readonly record struct NegotiationOutcome(bool Agreed, Credits Fee);

/// <summary>
/// The central verb (§4.1.6): offer / counter / accept, driven by hidden reservation prices and
/// readable soft tells. <see cref="ResolveAi"/> is the fast closed form used for AI-vs-AI deals
/// (cheap enough for million-tick soak runs); <see cref="NegotiationSession"/> is the interactive,
/// round-by-round version the player plays against, with the hidden numbers staying hidden.
/// </summary>
public static class Negotiation
{
    /// <summary>
    /// Closed-form settlement: a deal exists iff the buyer's max ≥ the seller's min. The price lands
    /// inside that zone, weighted by bargaining power — the side that concedes faster gives up ground.
    /// </summary>
    public static NegotiationOutcome ResolveAi(NegotiationParty buyer, NegotiationParty seller)
    {
        decimal buyerMax = buyer.Reservation.Amount;
        decimal sellerMin = seller.Reservation.Amount;
        if (buyerMax < sellerMin)
        {
            return new NegotiationOutcome(false, Credits.Zero);
        }

        decimal totalConcession = buyer.ConcessionRate + seller.ConcessionRate;
        // Seller is powerful (price near buyer's max) when the buyer concedes the larger share.
        decimal sellerPower = totalConcession <= 0m ? 0.5m : buyer.ConcessionRate / totalConcession;
        decimal fee = sellerMin + ((buyerMax - sellerMin) * sellerPower);
        return new NegotiationOutcome(true, Credits.Of(fee));
    }
}

/// <summary>
/// An interactive negotiation the player drives. The player is the seller of haul service (wants a
/// high fee); the AI counterparty is the buyer with a hidden reservation. Each round the player
/// offers an ask; the AI accepts, counters (conceding a fraction of the gap), or walks — and a soft
/// tell hints at how close the AI is to its limit.
/// </summary>
public sealed class NegotiationSession
{
    private readonly decimal _buyerMax;        // AI consumer's hidden reservation (its ceiling)
    private readonly decimal _buyerConcession; // how fast the AI gives ground
    private readonly int _maxRounds;
    private decimal _buyerOffer;               // the AI's current standing offer

    public NegotiationSession(NegotiationParty aiBuyer, Credits openingOffer, int maxRounds)
    {
        _buyerMax = aiBuyer.Reservation.Amount;
        _buyerConcession = aiBuyer.ConcessionRate;
        _maxRounds = maxRounds;
        _buyerOffer = Math.Min(openingOffer.Amount, _buyerMax);
    }

    public int Round { get; private set; }

    public bool Concluded { get; private set; }

    /// <summary>The fee the AI is currently offering the player.</summary>
    public Credits CurrentOffer => Credits.Of(_buyerOffer);

    /// <summary>A soft, readable hint — never the hidden number (§4.1.6).</summary>
    public string Tell { get; private set; } = "awaiting your move";

    /// <summary>
    /// The player asks for <paramref name="playerAsk"/>. Returns the outcome of this round:
    /// accepted (deal), still negotiating (a new counter-offer), or walked.
    /// </summary>
    public NegotiationStep Counter(Credits playerAsk)
    {
        if (Concluded)
        {
            return new NegotiationStep(NegotiationStepKind.Walked, CurrentOffer);
        }

        Round++;
        decimal ask = playerAsk.Amount;

        // The AI accepts anything at or under its ceiling that it judges fair this round.
        if (ask <= _buyerOffer)
        {
            Concluded = true;
            Tell = "accepts at once";
            return new NegotiationStep(NegotiationStepKind.Accepted, playerAsk);
        }

        if (ask > _buyerMax || Round > _maxRounds)
        {
            Concluded = true;
            Tell = "walks away";
            return new NegotiationStep(NegotiationStepKind.Walked, CurrentOffer);
        }

        decimal gap = ask - _buyerOffer;
        decimal concession = gap * _buyerConcession;
        _buyerOffer = Math.Min(_buyerMax, _buyerOffer + concession);

        decimal headroom = _buyerMax <= 0m ? 0m : (_buyerMax - _buyerOffer) / _buyerMax;
        Tell = headroom switch
        {
            < 0.04m => "appears to be at its limit",
            < 0.15m => "reluctant",
            _ => "responds readily",
        };

        return new NegotiationStep(NegotiationStepKind.Countered, CurrentOffer);
    }
}

public enum NegotiationStepKind
{
    Accepted,
    Countered,
    Walked,
}

public readonly record struct NegotiationStep(NegotiationStepKind Kind, Credits Fee);
