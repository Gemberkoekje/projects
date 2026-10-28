using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>
/// Information (plan S6, design §7): presence and the here, near and far tiers; each actor's knowledge of asks and stocks
/// with their age, which AI actors and the player alike decide on, trusting what they last saw (D32); orders at a producer's
/// door at its ask; the Accord's public record; and The Daily Accord, the only way a far contract reaches anyone (D31).
/// </summary>
public sealed class InformationTests
{
    [Fact]
    public void The_tiers_follow_the_players_presence()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        Sight sight = Visibility.Of(context, context.Player());

        Assert.Equal(new[] { context.Place("Meridian Station").Id }, sight.Presence);
        Assert.Equal(Tier.Here, sight.TierOf(context.Place("Meridian Station").Id));
        Assert.All(new[] { "Verdant", "Helios Prime", "Tycho Foundry" }, n => Assert.Equal(Tier.Near, sight.TierOf(context.Place(n).Id)));
        Assert.All(new[] { "Halcyon Refinery", "Rust Belt", "Edge Station" }, n => Assert.Equal(Tier.Far, sight.TierOf(context.Place(n).Id)));
    }

    [Fact]
    public void A_ship_counts_where_it_is_heading_and_storage_where_it_is()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        LocationId rustBelt = context.Place("Rust Belt").Id;
        LocationId edge = context.Place("Edge Station").Id;

        Market.Dispatch(context, context.ShipOf(player), new ShipJob(player.Id, GoodType.Food, 0m, rustBelt, PickupSource.Aboard, null, Delivery: new ContractId(999)));
        Sight heading = Visibility.Of(context, player);
        Assert.Equal(Tier.Here, heading.TierOf(rustBelt)); // in transit, the ship is present where it's heading (design §7.1)
        Assert.Equal(Tier.Near, heading.TierOf(edge));

        context.PutInStorage(player, edge, context.TakeFromStock(context.Actor("Verdant Agricombine"), GoodType.Food, 5m, reserved: false));
        Assert.Equal(Tier.Here, Visibility.Of(context, player).TierOf(edge));
        context.AssertSound();
    }

    [Fact]
    public void A_post_is_seen_in_its_own_system_and_a_jump_away_only_once_its_poster_pays_to_show_it_there()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        SaleContract here = context.PostWanted(context.Actor("Meridian Habs"), GoodType.Fuel, 20m, 400m);
        SaleContract near = context.PostWanted(context.Actor("Verdant Township"), GoodType.Parts, 20m, 700m);
        SaleContract far = context.PostWanted(context.Actor("Edge Station Holdfast"), GoodType.Food, 20m, 400m);

        Sight now = Visibility.Of(context, player);
        Assert.Equal((true, false, false), (now.CanSee(here), now.CanSee(near), now.CanSee(far)));
        Assert.Equal(((long?)0, (long?)null, (long?)null), (now.ReachHours(here), now.ReachHours(near), now.ReachHours(far)));

        context.RunTo(context.CurrentTick + 48); // a jump away, nobody sees it on its own (D38)
        Assert.False(Visibility.Of(context, player).CanSee(near));

        context.ShowAJumpFurther(near); // Verdant Township pays to show it a jump out: at Meridian four hours later
        Sight later = Visibility.Of(context, player);
        Assert.Equal((true, true, false), (later.CanSee(here), later.CanSee(near), later.CanSee(far)));
        Assert.Equal(context.News.WasReportedOpen(far.Id), Knowledge.OpenContracts(context, player, later).Contains(far)); // only if the news carried it
    }

    [Fact]
    public void Here_is_live_near_is_the_morning_bulletin_and_far_is_as_last_seen()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState farm = context.Actor("Verdant Agricombine");
        ActorState ore = context.Actor("Rust Belt Extraction");
        Ship ship = context.ShipOf(player);
        context.RunTo(GameTime.At(0, PriceBulletin.Hour)); // the first bulletin
        Credits posted = DecisionEngine.Ask(farm);

        context.Consume(farm, GoodType.Food, 80m); // the farm's warehouse empties: its ask rises, but nobody a jump away sees it until tomorrow
        Assert.True(DecisionEngine.Ask(farm) > posted);
        PriceReading daily = Knowledge.ReadingOf(context, player, farm, Visibility.Of(context, player))!.Value;
        Assert.Equal((posted, ReadingSource.Daily, GameTime.At(0, PriceBulletin.Hour)), (daily.Ask, daily.Source, daily.AskTick));
        Assert.Null(daily.CurrentStock); // a jump away it sees a price, not a warehouse: the stock it saw at the start is superseded

        ship.Arrive(context.CurrentTick, farm.Home); // there: live
        PriceReading live = Knowledge.ReadingOf(context, player, farm, Visibility.Of(context, player))!.Value;
        Assert.Equal((DecisionEngine.Ask(farm), farm.Available(GoodType.Food), true), (live.Ask, live.CurrentStock, live.IsLive));
        Knowledge.LookAt(context, player, farm.Home);

        ship.Arrive(context.CurrentTick + 1, context.Place("Meridian Station").Id); // back home, a jump away: what it saw there, which is newer than the bulletin
        context.RunTo(context.CurrentTick + 1);
        PriceReading remembered = Knowledge.ReadingOf(context, player, farm, Visibility.Of(context, player))!.Value;
        Assert.Equal((live.Ask, ReadingSource.Seen, live.AskTick, live.CurrentStock), (remembered.Ask, remembered.Source, remembered.AskTick, remembered.CurrentStock));

        // Far: what everyone knew at the start, however old; the bulletin doesn't reach it.
        PriceReading start = Knowledge.ReadingOf(context, player, ore, Visibility.Of(context, player))!.Value;
        Assert.Equal((ReadingSource.Start, 0L), (start.Source, start.AskTick));
        Assert.Null(player.Knowledge.LastHere(ore.Home));
        Assert.Equal(context.CurrentTick, player.Knowledge.LastHere(farm.Home) + 1);
    }

    [Fact]
    public void An_AI_trader_trusts_the_ask_it_last_saw_and_buys_at_the_door_at_the_ask_as_it_stands()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState kael = context.Actor("Dust Runner Kael");
        ActorState farm = context.Actor("Verdant Agricombine");
        Credits seen = kael.Knowledge.Stored(farm.Id)!.Value.Ask; // what everyone knew at the start; Verdant is far from the Rust Belt
        context.Consume(farm, GoodType.Food, 43m); // the farm's ask rises about 5%
        Credits actual = DecisionEngine.Ask(farm);
        Assert.InRange(actual.Amount / seen.Amount, 1.03m, 1.07m);

        SaleContract wanted = context.PostWanted(context.Actor("Rust Belt Camp"), GoodType.Food, 60m, 1400m);
        FillSource source = DecisionEngine.FillSources(context, kael, wanted, context.ShipOf(kael)).Single(s => s.Kind == FillSourceKind.Producer);
        Assert.Equal(System.Math.Round(seen.Amount * 60m, 2), source.PurchasePrice.Amount); // it plans on the ask it last saw

        DecisionEngine.RunTrader(context, kael);

        Assert.Equal(kael.Id, wanted.Seller);
        SaleContract order = context.Contracts.Values.OfType<SaleContract>().Single(s => s.AtTheDoor && s.PostedBy == kael.Id);
        Assert.Equal(System.Math.Round(actual.Amount * 60m, 2), order.Price.Amount); // and pays the ask as it stands, within its limit
        Assert.Equal(Market.DoorLimit(source.PurchasePrice), order.OfferedPrice);
        context.AssertSound();
    }

    [Fact]
    public void A_producer_that_turns_down_an_order_at_its_door_is_left_out_for_a_day()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState kael = context.Actor("Dust Runner Kael");
        ActorState farm = context.Actor("Verdant Agricombine");
        context.Consume(farm, GoodType.Food, 140m); // its ask rises well past what Kael last saw, and past its limit
        SaleContract wanted = context.PostWanted(context.Actor("Rust Belt Camp"), GoodType.Food, 30m, 1000m);

        DecisionEngine.RunTrader(context, kael);

        Assert.Equal(ContractStatus.Open, wanted.Status);
        Assert.Equal(context.CurrentTick, kael.Knowledge.RefusedAt(farm.Id));
        Assert.Equal(1, context.DoorRefusedForPrice);
        Assert.DoesNotContain(DecisionEngine.FillSources(context, kael, wanted, context.ShipOf(kael)), s => s.Kind == FillSourceKind.Producer);

        context.RunTo(context.CurrentTick + Knowledge.RefusalHours);
        Assert.Contains(DecisionEngine.FillSources(context, kael, wanted, context.ShipOf(kael)), s => s.Kind == FillSourceKind.Producer); // it trusts its old reading again
        context.AssertSound();
    }

    [Fact]
    public void The_daily_accord_reports_yesterdays_contracts_as_they_stand_at_press_time()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        SaleContract medicine = context.PostWanted(context.Actor("Helios Prime Metropole"), GoodType.Medicine, 5m, 600m); // answered at once by Biolabs
        SaleContract food = context.PostWanted(context.Actor("Edge Station Holdfast"), GoodType.Food, 40m, 900m);
        Assert.Equal(ContractStatus.Fulfilled, medicine.Status);

        context.RunTo(GameTime.At(1, PriceBulletin.Hour));

        NewsIssue issue = context.News.Latest!;
        Assert.Equal((GameTime.At(1, PriceBulletin.Hour), 0L), (issue.Tick, issue.Day));
        Assert.Equal(2, issue.Candidates); // both of yesterday's contracts
        Assert.Contains(issue.Items, i => i.Contract == medicine.Id && i.Kind == NewsKind.Delivered && i.TicksLate == 0);
        Assert.Contains(issue.Items, i => i.Contract == food.Id && i.Kind == NewsKind.Posted);
        Assert.True(context.News.WasReportedOpen(food.Id));
        Assert.False(context.News.WasReportedOpen(medicine.Id));
    }

    [Fact]
    public void A_far_post_in_the_news_is_known_everywhere_until_someone_finds_it_closed()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState coreward = context.Actor("Coreward Logistics");
        SaleContract food = context.PostWanted(context.Actor("Edge Station Holdfast"), GoodType.Food, 40m, 900m);
        context.RunTo(GameTime.At(1, PriceBulletin.Hour));

        Assert.False(Visibility.Of(context, player).CanSee(food)); // still far
        Assert.Contains(food, Knowledge.OpenContracts(context, player, Visibility.Of(context, player)));
        Assert.Contains(food, Knowledge.OpenContracts(context, coreward, Visibility.Of(context, coreward))); // the same news for an AI

        Market.Withdraw(context, food); // gone, but nobody far away can tell
        Assert.True(PlayerActions.Believed(context, player, food));
        Assert.NotEqual(DealBlock.NotOpen, PlayerActions.CheckFill(context, player, food).Block);

        Assert.Equal(Signing.NotOpen, Market.FillWithShip(context, player, food, food.OfferedPrice, context.ShipOf(player), FillSource.FromProducer(context.Actor("Verdant Agricombine"), Credits.Of(300m))));
        Assert.True(player.Knowledge.FoundClosed(food.Id));
        Assert.DoesNotContain(food, Knowledge.OpenContracts(context, player, Visibility.Of(context, player)));
        Assert.Contains(food, Knowledge.OpenContracts(context, coreward, Visibility.Of(context, coreward))); // it hasn't tried yet
        Assert.Equal(1, context.FoundClosedFromNews);
    }

    [Fact]
    public void The_players_far_post_reaches_the_actors_that_read_of_it_in_the_news()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true, answers: true);
        ActorState player = context.Player();
        LocationId edge = context.Place("Edge Station").Id;

        // The carriers that see the Edge or the Rust Belt, here or a jump away, are all busy elsewhere.
        var near = new[] { "Dust Runner Kael", "Foundry Freightways", "Otto Reyes", "Sable Vance" }.Select(context.Actor).ToList();
        foreach (ActorState carrier in near)
        {
            foreach (ShipId id in carrier.Ships)
            {
                Market.Dispatch(context, context.Ship(id), new ShipJob(carrier.Id, GoodType.Food, 0m, context.Place("Verdant").Id, PickupSource.Aboard, null, Delivery: new ContractId(999)));
            }
        }

        context.PutInStorage(player, edge, context.TakeFromStock(context.Actor("Rust Belt Extraction"), GoodType.Ore, 20m, reserved: false));
        Contract haul = PlayerActions.Post(context, player, new PostDraft(
            PostKind.Haul, GoodType.Ore, 20m, edge, context.Place("Rust Belt").Id, new Offer(Credits.Of(1500m), context.CurrentTick + 400, TermPackage.Standard)))!;

        // Only carriers present at either end or a jump from it could see it, and none of those has a free ship: nobody answers.
        context.RunTo(GameTime.At(1, PriceBulletin.Hour) - 1);
        Assert.Equal(ContractStatus.Open, haul.Status);
        context.RunTo(GameTime.At(1, PriceBulletin.Hour) + Haggling.SlowestReply);

        Assert.True(context.News.WasReportedOpen(haul.Id));
        Assert.Equal(ContractStatus.Active, haul.Status);
        ActorState taker = context.Actor(haul.CounterpartyId!.Value);
        Assert.DoesNotContain(taker, near);
        Assert.True(haul.SignedTick > GameTime.At(1, PriceBulletin.Hour));
        Assert.Equal(1, context.SignedFromNews);
    }

    [Fact]
    public void Failure_size_novelty_and_the_frontier_make_the_news_likelier()
    {
        SimulationContext context = TestWorld.Quiet();
        SaleContract core = context.PostWanted(context.Actor("Meridian Habs"), GoodType.Food, 40m, 600m);
        SaleContract frontier = context.PostWanted(context.Actor("Rust Belt Camp"), GoodType.Food, 40m, 600m);
        SaleContract big = context.PostWanted(context.Actor("Meridian Habs"), GoodType.Fuel, 40m, 2400m);

        decimal Weight(Contract c, NewsKind kind, long late = 0, Credits? average = null) => NewsDesk.Weight(context, c, new NewsItem(c.Id, kind, 0, late, AverageUnit: average));

        Assert.Equal(4m, Weight(core, NewsKind.Failed) / Weight(core, NewsKind.Delivered));
        Assert.Equal(2m, Weight(core, NewsKind.Delivered, late: 5) / Weight(core, NewsKind.Delivered));
        Assert.Equal(context.Config.NewsFrontierWeight, Weight(frontier, NewsKind.Posted) / Weight(core, NewsKind.Posted));
        Assert.InRange(Weight(big, NewsKind.Posted) / Weight(core, NewsKind.Posted), 1.999m, 2.001m); // four times the price, twice the weight
        Assert.Equal(2m, Weight(core, NewsKind.Posted, average: Credits.Of(10m)) / Weight(core, NewsKind.Posted)); // ₵15 a unit against a ₵10 average
    }

    [Fact]
    public void The_news_is_the_same_for_a_seed()
    {
        IReadOnlyList<string> Issues()
        {
            SimulationContext context = TestWorld.RunHeadless(42L, 3_000);
            return context.News.Issues.SelectMany(i => i.Items.Select(n => $"{i.Tick}:{n.Contract}:{n.Kind}")).ToList();
        }

        IReadOnlyList<string> first = Issues();
        Assert.Equal(first, Issues());
        Assert.InRange(first.Count, 10, NewsDesk.IssuesKept * 3);
    }

    [Fact]
    public void Accord_contracts_make_the_public_record_and_frontier_ones_only_each_sides_own_dealings()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState metropole = context.Actor("Helios Prime Metropole");
        ActorState biolabs = context.Actor("Helios Biolabs");
        context.PostWanted(metropole, GoodType.Medicine, 5m, 600m); // deep Accord, handed over on the spot

        Assert.Equal((1, 0), (context.Records.PublicRecordOf(biolabs.Id).OnTime, context.Records.PublicRecordOf(biolabs.Id).Late));
        Assert.Equal(1, context.Records.PublicRecordOf(metropole.Id).Paid);
        Assert.Equal(1, context.Records.DealingsWith(metropole.Id, biolabs.Id).OnTime);

        ActorState extraction = context.Actor("Rust Belt Extraction");
        ActorState camp = context.Actor("Rust Belt Camp");
        LocationId rustBelt = context.Place("Rust Belt").Id;
        SaleContract lot = Market.PostSale(context, extraction, SaleSide.ForSale, GoodType.Ore, 20m, rustBelt, Credits.Of(120m), Credits.Of(100m), context.CurrentTick + 48, Holding.StockAt(rustBelt));
        Assert.Equal(Signing.Signed, Market.BuyLot(context, camp, lot, lot.OfferedPrice, Holding.StockAt(rustBelt)));

        Assert.Equal(0, context.Records.PublicRecordOf(extraction.Id).Total); // the frontier leaves no public record
        Assert.Equal(0, context.Records.PublicRecordOf(camp.Id).Total);
        Assert.Equal(1, context.Records.DealingsWith(camp.Id, extraction.Id).OnTime);
        Assert.Equal(1, context.Records.DealingsWith(extraction.Id, camp.Id).Paid);
        context.AssertSound();
    }
}
