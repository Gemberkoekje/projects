using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Tests;

/// <summary>The rules of haggling (design §3.6, §16.5): reservations, concession, acceptance, walking away, and tells.</summary>
public sealed class NegotiationTests
{
    [Fact]
    public void A_post_that_crosses_the_responders_reservation_is_taken_as_posted()
    {
        var buyer = new NegotiationParty(Credits.Of(100m), 0.5m, IsBuyer: true);
        var seller = new NegotiationParty(Credits.Of(80m), 0.5m, IsBuyer: false);

        NegotiationOutcome outcome = Haggling.Resolve(buyer, Credits.Of(90m), seller, maxRounds: 6);

        Assert.Equal((true, Credits.Of(90m)), (outcome.Agreed, outcome.Fee));
    }

    [Fact]
    public void A_deal_lands_inside_both_reservations_or_not_at_all()
    {
        var buyer = new NegotiationParty(Credits.Of(100m), 0.4m, IsBuyer: true);
        var seller = new NegotiationParty(Credits.Of(85m), 0.4m, IsBuyer: false);
        var dearSeller = new NegotiationParty(Credits.Of(120m), 0.4m, IsBuyer: false);

        NegotiationOutcome deal = Haggling.Resolve(buyer, Credits.Of(70m), seller, maxRounds: 6);
        NegotiationOutcome none = Haggling.Resolve(buyer, Credits.Of(90m), dearSeller, maxRounds: 6);

        Assert.True(deal.Agreed);
        Assert.InRange(deal.Fee.Amount, 85m, 100m);
        Assert.False(none.Agreed);
    }

    [Fact]
    public void A_post_too_far_from_the_responders_reservation_draws_nothing()
    {
        var buyer = new NegotiationParty(Credits.Of(1000m), 0.5m, IsBuyer: true);
        var seller = new NegotiationParty(Credits.Of(100m), 0.5m, IsBuyer: false);

        Assert.False(Haggling.Resolve(buyer, Credits.Of(60m), seller, maxRounds: 6).Agreed); // 40% under its floor
        Assert.True(Haggling.Resolve(buyer, Credits.Of(70m), seller, maxRounds: 6).Agreed);
    }

    [Fact]
    public void A_slow_conceder_holds_out_for_more()
    {
        var buyer = new NegotiationParty(Credits.Of(200m), 0.5m, IsBuyer: true);
        var eager = new NegotiationParty(Credits.Of(100m), 0.9m, IsBuyer: false);
        var firm = new NegotiationParty(Credits.Of(100m), 0.1m, IsBuyer: false);

        decimal eagerPrice = Haggling.Resolve(buyer, Credits.Of(80m), eager, maxRounds: 6).Fee.Amount;
        decimal firmPrice = Haggling.Resolve(buyer, Credits.Of(80m), firm, maxRounds: 6).Fee.Amount;

        Assert.True(firmPrice > eagerPrice);
    }

    [Fact]
    public void Without_overlap_the_rounds_run_out()
    {
        var buyer = new NegotiationParty(Credits.Of(100m), 0.3m, IsBuyer: true);
        var seller = new NegotiationParty(Credits.Of(110m), 0.3m, IsBuyer: false);

        Assert.False(Haggling.Resolve(buyer, Credits.Of(95m), seller, maxRounds: 6).Agreed);
    }

    [Fact]
    public void Concessions_move_toward_the_reservation_and_never_past_it()
    {
        Credits next = Haggling.Concede(Credits.Of(150m), Credits.Of(100m), 0.4m);

        Assert.Equal(Credits.Of(130m), next);
        Assert.Equal(Credits.Of(100m), Haggling.Concede(Credits.Of(100m), Credits.Of(100m), 0.9m));
        Assert.True(Haggling.Opening(Credits.Of(100m), pays: false, 0.5m).Amount > 100m);
        Assert.True(Haggling.Opening(Credits.Of(100m), pays: true, 0.5m).Amount < 100m);
    }

    [Fact]
    public void The_wording_tells_how_close_a_reply_is_to_its_limit()
    {
        Assert.Equal(Tell.AtLimit, Haggling.TellFor(Credits.Of(102m), Credits.Of(100m)));
        Assert.Equal(Tell.Reluctant, Haggling.TellFor(Credits.Of(110m), Credits.Of(100m)));
        Assert.Equal(Tell.Readily, Haggling.TellFor(Credits.Of(130m), Credits.Of(100m)));
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(0.5)]
    [InlineData(0.95)]
    public void Replies_take_one_to_twelve_hours_quicker_for_an_eager_side_and_slower_near_its_limit(double concession)
    {
        decimal c = (decimal)concession;
        foreach (Tell tell in new[] { Tell.Readily, Tell.Reluctant, Tell.AtLimit })
        {
            for (long spread = 0; spread < 4; spread++)
            {
                Assert.InRange(Haggling.ReplyHours(c, tell, spread), 1, 12);
            }
        }

        Assert.True(Haggling.ReplyHours(c, Tell.AtLimit, 0) > Haggling.ReplyHours(c, Tell.Readily, 0));
        Assert.True(Haggling.ReplyHours(0.9m, Tell.Readily, 0) < Haggling.ReplyHours(0.1m, Tell.Readily, 0));
    }
}
