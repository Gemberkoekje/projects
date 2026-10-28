using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>
/// Showing a post further out (plan S6b, design §7.1, D38): a post is shown in its own system at once and nowhere else;
/// further out its poster pays each system's station a fee, four times dearer a jump, and it is shown there a delay after.
/// An AI shows a post that sits unsold a ring further each day while that stays within its budget; the player chooses.
/// </summary>
public sealed class AdvertisingTests
{
    [Fact]
    public void A_post_is_shown_only_at_its_places_and_each_further_system_has_its_price_and_delay()
    {
        SimulationContext context = TestWorld.Quiet();
        SaleContract wanted = context.PostWanted(context.Actor("Meridian Habs"), GoodType.Fuel, 20m, 2000m);

        Assert.Equal(new[] { (context.Place("Meridian Station").Id, wanted.PostedTick) }, wanted.Shown.ToArray());
        Assert.False(wanted.Advertised);

        // A share of its price (0.05% of ₵2,000 is ₵1), four times dearer for each jump past the first; four hours, a day, two.
        var options = Advertising.Options(context, wanted).Select(o => (context.Location(o.At).Name, o.Jumps, o.Fee.Amount, Advertising.DelayHours(context, o.Jumps)));
        Assert.Equal(
            new[]
            {
                ("Verdant", 1, 1.00m, 4L), ("Helios Prime", 1, 1.00m, 4L), ("Tycho Foundry", 1, 1.00m, 4L),
                ("Halcyon Refinery", 2, 4.00m, 24L), ("Rust Belt", 2, 4.00m, 24L), ("Edge Station", 3, 16.00m, 48L),
            },
            options.ToArray());

        // A cheap post pays the floor; a haul counts its jumps from the nearer of its two places.
        SaleContract cheap = context.PostWanted(context.Actor("Meridian Habs"), GoodType.Food, 4m, 40m);
        Assert.Equal(0.20m, Advertising.FeeAt(context, cheap, context.Place("Verdant").Id).Amount);
        HaulContract haul = PlayerActionsTests.FarmHaul(context);
        Assert.Equal(1, Advertising.JumpsFrom(context, haul, context.Place("Tycho Foundry").Id));
        Assert.Equal(0m, Advertising.FeeAt(context, haul, context.Place("Meridian Station").Id).Amount);
        Assert.DoesNotContain(Advertising.Options(context, haul), o => o.At == context.Place("Verdant").Id || o.At == context.Place("Meridian Station").Id);
    }

    [Fact]
    public void Showing_a_post_further_pays_each_systems_station_and_shows_it_there_after_the_delay()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState habs = context.Actor("Meridian Habs");
        LocationId tycho = context.Place("Tycho Foundry").Id;
        LocationId rustBelt = context.Place("Rust Belt").Id;
        SaleContract wanted = context.PostWanted(habs, GoodType.Fuel, 20m, 2000m);
        Credits habsBefore = habs.Credits, tychoDocks = context.StationOperatorAt(tycho)!.Credits, rustDocks = context.StationOperatorAt(rustBelt)!.Credits;
        long now = context.CurrentTick;

        Assert.Equal(5.00m, Advertising.Quote(context, wanted, new[] { tycho, rustBelt })?.Amount);
        Assert.True(Advertising.Show(context, habs, wanted, new[] { tycho, rustBelt }));

        Assert.Equal(habsBefore.Amount - 5.00m, habs.Credits.Amount);
        Assert.Equal(tychoDocks.Amount + 1.00m, context.StationOperatorAt(tycho)!.Credits.Amount);
        Assert.Equal(rustDocks.Amount + 4.00m, context.StationOperatorAt(rustBelt)!.Credits.Amount);
        Assert.Equal((now + 4, now + 24), (wanted.ShownFrom(tycho), wanted.ShownFrom(rustBelt)));
        Assert.Equal((5.00m, 1L, 1L), (context.AdFeesPaid.Amount, context.AdsShown[1], context.AdsShown[2]));
        Assert.True(wanted.Advertised);

        // Whoever is present at Tycho sees it once it is shown there, not before.
        ActorState workforce = context.Actor("Tycho Workforce");
        context.RunTo(now + 3);
        Assert.Equal(1L, Visibility.Of(context, workforce).ReachHours(wanted));
        Assert.False(Visibility.Of(context, workforce).CanSee(wanted));
        context.RunTo(now + 4);
        Assert.True(Visibility.Of(context, workforce).CanSee(wanted));
        ActorState player = context.Player();

        // Already shown there, it can't be bought again; someone else's post can't be shown; nothing is paid for either.
        Assert.False(Advertising.Show(context, habs, wanted, new[] { tycho }));
        Assert.False(Advertising.Show(context, player, wanted, new[] { context.Place("Verdant").Id }));
        Assert.Equal(5.00m, context.AdFeesPaid.Amount);
        context.AssertSound();
    }

    [Fact]
    public void An_AI_shows_a_post_that_sits_unsold_a_ring_further_each_day_while_that_stays_within_its_budget()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        SaleContract dear = context.PostWanted(habs, GoodType.Fuel, 20m, 2000m, hours: 400);
        SaleContract cheap = context.PostWanted(habs, GoodType.Food, 4m, 40m, hours: 400);
        long posted = context.CurrentTick;

        context.RunTo(posted + 23);
        Advertising.AdvertiseUnsold(context, habs);
        Assert.False(dear.Advertised); // not yet a day unsold

        // Each day: the next ring, every system at that distance, while its fees are no more than 5% of the price.
        int[] Furthest(Contract c) => c.Shown.Select(s => Advertising.JumpsFrom(context, c, s.At)).ToArray();
        for (int day = 1; day <= 4; day++)
        {
            context.RunTo(posted + (24 * day));
            Advertising.AdvertiseUnsold(context, habs);
            Advertising.AdvertiseUnsold(context, habs); // the same day again changes nothing
        }

        Assert.Equal(3, Furthest(dear).Max()); // ₵3, ₵8, then ₵16 for Edge Station: all within ₵100
        Assert.Equal(7, dear.Shown.Count());
        Assert.Equal(2, Furthest(cheap).Max()); // ₵0.60 and ₵1.60 fit in ₵2; Edge Station's ₵3.20 doesn't
        Assert.Null(cheap.ShownFrom(context.Place("Edge Station").Id));
        Assert.Equal(3.00m + 8.00m + 16.00m + 0.60m + 1.60m, context.AdFeesPaid.Amount);
        context.AssertSound();
    }

    [Fact]
    public void The_player_pays_to_show_its_post_when_it_posts_and_the_farm_a_jump_away_hears_of_it_only_then()
    {
        // Food wanted at Meridian, under the farm's ask: the farm at Verdant, a jump out, never sees it ...
        SimulationContext unseen = TestWorld.Quiet(withPlayer: true, answers: true);
        Contract alone = PostFoodAtMeridian(unseen, showAt: null);
        unseen.RunTo(unseen.CurrentTick + 4 + Haggling.SlowestReply);
        Assert.Null(unseen.NegotiationWith(alone.Id, unseen.Actor("Verdant Agricombine").Id));

        // ... unless the player pays to show it at Verdant: the farm hears of it four hours later, and counters.
        SimulationContext context = TestWorld.Quiet(withPlayer: true, answers: true);
        ActorState player = context.Player();
        ActorState verdantDocks = context.StationOperatorAt(context.Place("Verdant").Id)!;
        Credits before = player.Credits, docks = verdantDocks.Credits;
        Contract post = PostFoodAtMeridian(context, showAt: new[] { context.Place("Verdant").Id });
        Assert.Equal(before.Amount - 0.20m, player.Credits.Amount); // the floor: 0.05% of ₵300 is less
        Assert.Equal(docks.Amount + 0.20m, verdantDocks.Credits.Amount);
        Assert.Equal(post.PostedTick + 4, post.ShownFrom(context.Place("Verdant").Id));

        context.RunTo(context.CurrentTick + 4 + Haggling.SlowestReply);
        Negotiation thread = context.NegotiationWith(post.Id, context.Actor("Verdant Agricombine").Id) ?? throw new Xunit.Sdk.XunitException("The farm didn't answer.");
        Assert.True(thread.Latest.Tick >= post.PostedTick + 4, "It heard of the post only once it was shown at Verdant.");
        context.AssertSound();
    }

    [Fact]
    public void The_player_shows_an_open_post_further_later_and_is_told_why_not_when_it_cannot()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        LocationId tycho = context.Place("Tycho Foundry").Id;
        Contract post = PostFoodAtMeridian(context, showAt: null);
        SaleContract theirs = context.PostWanted(context.Actor("Meridian Habs"), GoodType.Fuel, 20m, 400m);

        Assert.Equal(AdBlock.NotYours, PlayerActions.CheckAdvertise(context, player, theirs, new[] { tycho }));
        Assert.Equal(AdBlock.NothingChosen, PlayerActions.CheckAdvertise(context, player, post, new LocationId[0]));
        Assert.Equal(AdBlock.NothingChosen, PlayerActions.CheckAdvertise(context, player, post, new[] { context.Place("Meridian Station").Id })); // shown there already

        Assert.True(PlayerActions.Advertise(context, player, post, new[] { tycho }));
        Assert.Equal(context.CurrentTick + 4, post.ShownFrom(tycho));
        Assert.Equal(AdBlock.NothingChosen, PlayerActions.CheckAdvertise(context, player, post, new[] { tycho }));

        context.ApplySink(player, Credits.Of(player.Credits.Amount - 0.10m));
        Assert.Equal(AdBlock.CannotAfford, PlayerActions.CheckAdvertise(context, player, post, new[] { context.Place("Verdant").Id }));
        Assert.False(PlayerActions.Advertise(context, player, post, new[] { context.Place("Verdant").Id }));

        // Nor can it post something with a fee it can't pay on top.
        var draft = new PostDraft(
            PostKind.Wanted, GoodType.Food, 1m, context.Place("Meridian Station").Id, null, new Offer(Credits.Of(0.05m), context.CurrentTick + 72, TermPackage.Standard),
            new[] { context.Place("Verdant").Id });
        Assert.Equal(PostBlock.None, PlayerActions.CheckPost(context, player, draft with { ShowAt = null }));
        Assert.Equal(PostBlock.CannotAfford, PlayerActions.CheckPost(context, player, draft));
        context.AssertSound();
    }

    [Fact]
    public void A_contract_taken_by_someone_who_saw_it_only_where_it_was_advertised_is_counted()
    {
        // Halcyon's fuel to Tycho, shown at Meridian a jump from Tycho: the player, present only at Meridian, sees it there.
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState fuelworks = context.Actor("Halcyon Fuelworks");
        HaulContract haul = Market.PostHaul(
            context, fuelworks, GoodType.Fuel, 30m, Holding.StockAt(fuelworks.Home), context.Place("Tycho Foundry").Id,
            Credits.Of(120m), Credits.Of(150m), context.CurrentTick + 300, serves: null, TermPackage.Standard);
        Assert.False(Visibility.Of(context, player).CanSee(haul));

        Assert.True(Advertising.Show(context, fuelworks, haul, new[] { context.Place("Meridian Station").Id }));
        context.RunTo(context.CurrentTick + 4);
        Assert.True(Visibility.Of(context, player).CanSee(haul));
        PlayerActions.HaulFeasibility check = PlayerActions.CheckHaul(context, player, haul);
        Assert.True(check.Ok, check.Block.ToString());
        Assert.Equal(Signing.Signed, PlayerActions.TakeHaul(context, player, haul, check.Ship!));
        Assert.Equal(1L, context.SignedViaAd);
        context.AssertSound();
    }

    private static Contract PostFoodAtMeridian(SimulationContext context, IReadOnlyList<LocationId>? showAt) =>
        PlayerActions.Post(
            context, context.Player(),
            new PostDraft(
                PostKind.Wanted, GoodType.Food, 60m, context.Place("Meridian Station").Id, null, new Offer(Credits.Of(300m), context.CurrentTick + 72, TermPackage.Standard),
                showAt))
        ?? throw new Xunit.Sdk.XunitException("The post was refused.");
}
