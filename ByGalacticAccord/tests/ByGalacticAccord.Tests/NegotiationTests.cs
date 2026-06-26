using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Tests;

/// <summary>The central verb (§4.1.6): reservation prices, concession, acceptance, walking, and tells.</summary>
public sealed class NegotiationTests
{
    [Fact]
    public void A_deal_exists_only_when_the_zones_overlap()
    {
        var buyer = new NegotiationParty(Credits.Of(100m), 0.5m, IsBuyer: true);
        var sellerInZone = new NegotiationParty(Credits.Of(80m), 0.5m, IsBuyer: false);
        var sellerTooDear = new NegotiationParty(Credits.Of(120m), 0.5m, IsBuyer: false);

        Assert.True(Negotiation.ResolveAi(buyer, sellerInZone).Agreed);
        Assert.False(Negotiation.ResolveAi(buyer, sellerTooDear).Agreed);
    }

    [Fact]
    public void Agreed_fee_lands_inside_the_bargaining_zone()
    {
        var buyer = new NegotiationParty(Credits.Of(100m), 0.5m, IsBuyer: true);
        var seller = new NegotiationParty(Credits.Of(60m), 0.5m, IsBuyer: false);

        NegotiationOutcome outcome = Negotiation.ResolveAi(buyer, seller);

        Assert.True(outcome.Agreed);
        Assert.InRange(outcome.Fee.Amount, 60m, 100m);
    }

    [Fact]
    public void The_side_that_concedes_faster_gives_up_ground()
    {
        var buyer = new NegotiationParty(Credits.Of(100m), 0.5m, IsBuyer: true);
        var eagerSeller = new NegotiationParty(Credits.Of(60m), 0.9m, IsBuyer: false);
        var firmSeller = new NegotiationParty(Credits.Of(60m), 0.1m, IsBuyer: false);

        decimal eagerFee = Negotiation.ResolveAi(buyer, eagerSeller).Fee.Amount;
        decimal firmFee = Negotiation.ResolveAi(buyer, firmSeller).Fee.Amount;

        // The eager seller settles for less; the firm seller holds out for more.
        Assert.True(firmFee > eagerFee);
    }

    [Fact]
    public void Session_accepts_when_the_player_asks_at_or_below_the_current_offer()
    {
        var buyer = new NegotiationParty(Credits.Of(100m), 0.4m, IsBuyer: true);
        var session = new NegotiationSession(buyer, openingOffer: Credits.Of(50m), maxRounds: 6);

        NegotiationStep step = session.Counter(Credits.Of(50m));

        Assert.Equal(NegotiationStepKind.Accepted, step.Kind);
        Assert.True(session.Concluded);
    }

    [Fact]
    public void Session_walks_when_the_player_demands_above_the_reservation()
    {
        var buyer = new NegotiationParty(Credits.Of(100m), 0.4m, IsBuyer: true);
        var session = new NegotiationSession(buyer, openingOffer: Credits.Of(50m), maxRounds: 6);

        NegotiationStep step = session.Counter(Credits.Of(150m));

        Assert.Equal(NegotiationStepKind.Walked, step.Kind);
    }

    [Fact]
    public void Session_concedes_upward_and_reveals_a_tell()
    {
        var buyer = new NegotiationParty(Credits.Of(100m), 0.4m, IsBuyer: true);
        var session = new NegotiationSession(buyer, openingOffer: Credits.Of(50m), maxRounds: 8);

        decimal before = session.CurrentOffer.Amount;
        NegotiationStep step = session.Counter(Credits.Of(90m));

        Assert.Equal(NegotiationStepKind.Countered, step.Kind);
        Assert.True(session.CurrentOffer.Amount > before);
        Assert.False(string.IsNullOrWhiteSpace(session.Tell));
    }

    [Fact]
    public void Session_walks_after_max_rounds()
    {
        var buyer = new NegotiationParty(Credits.Of(1000m), 0.05m, IsBuyer: true);
        var session = new NegotiationSession(buyer, openingOffer: Credits.Of(10m), maxRounds: 3);

        // Always ask just above the slowly-rising offer so it never accepts; it should give up after maxRounds.
        NegotiationStep step = default;
        for (int i = 0; i < 10 && !session.Concluded; i++)
        {
            step = session.Counter(Credits.Of(session.CurrentOffer.Amount + 200m));
        }

        Assert.True(session.Concluded);
        Assert.Equal(NegotiationStepKind.Walked, step.Kind);
    }
}
