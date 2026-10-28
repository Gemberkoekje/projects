using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Text;
using ByGalacticAccord.Presentation.Views;

namespace ByGalacticAccord.Tests;

/// <summary>
/// What the interface says about information (plan S6, design §7, §11.1, §11.2): The Daily Accord's items as headlines, the
/// map's tiers and the age of what the player knows of each place, the signals, the board's rows known only from the news,
/// and the contract's party with its public record.
/// </summary>
public sealed class InformationViewTests
{
    [Fact]
    public void Every_kind_of_news_item_reads_as_a_headline_for_an_ai_and_for_the_player()
    {
        SimulationContext context = WorldGenerator.Generate(42L, withPlayer: true).Context;
        ActorId player = context.PlayerActorId!.Value;
        ActorState consumer = context.ActorsOrdered.First(a => a.Demands.Count > 0);
        ActorState producer = context.ActorsOrdered.First(a => a.Producer is not null);
        ActorId trader = context.ActorsOrdered.First(a => a.MovesCargo && !a.IsPlayer).Id;

        var contracts = new List<Contract>();
        foreach ((ActorId poster, ActorId taker) in new[] { (consumer.Id, player), (player, trader), (consumer.Id, trader) })
        {
            contracts.Add(TemplateTests.Sale(context, poster, SaleSide.Wanted, producer, consumer));
            contracts.Add(TemplateTests.Signed(context, TemplateTests.Sale(context, poster, SaleSide.Wanted, producer, consumer), taker));
            contracts.Add(TemplateTests.Signed(context, TemplateTests.Sale(context, poster == player ? player : producer.Id, SaleSide.ForSale, producer, consumer), taker == player ? player : consumer.Id));
            contracts.Add(TemplateTests.Signed(context, TemplateTests.Haul(context, poster == player ? player : producer.Id, producer, consumer), taker));
        }

        var items = new List<NewsItem>();
        foreach (Contract contract in contracts)
        {
            foreach (Credits? average in new Credits?[] { null, Credits.Of(1m), Credits.Of(3m), Credits.Of(100m) })
            {
                items.Add(new NewsItem(contract.Id, NewsKind.Posted, 0, AverageUnit: average));
                items.Add(new NewsItem(contract.Id, NewsKind.Expired, 0, AverageUnit: average));
            }

            if (contract.CounterpartyId is null)
            {
                continue;
            }

            items.Add(new NewsItem(contract.Id, NewsKind.Signed, 1, AverageUnit: Credits.Of(2m)));
            items.Add(new NewsItem(contract.Id, NewsKind.Delivered, 5));
            items.Add(new NewsItem(contract.Id, NewsKind.Delivered, 5, TicksLate: 30));
            items.Add(new NewsItem(contract.Id, NewsKind.Short, 5, TicksLate: 2));
            items.Add(new NewsItem(contract.Id, NewsKind.Defaulted, 5));
            foreach (FailureReason reason in Enum.GetValues<FailureReason>())
            {
                items.Add(new NewsItem(contract.Id, NewsKind.Failed, 5, Reason: reason));
            }

            foreach (CancelReason reason in Enum.GetValues<CancelReason>())
            {
                foreach (ActorId by in new[] { contract.Payer!.Value, contract.Performer!.Value })
                {
                    items.Add(new NewsItem(contract.Id, NewsKind.Cancelled, 6, Cancellation: new Cancellation(by, reason, 6, Credits.Of(10m))));
                }
            }
        }

        foreach (NewsItem item in items)
        {
            string text = NewsText.Describe(context, item);
            Assert.DoesNotContain("{", text, StringComparison.Ordinal);
            Assert.DoesNotContain("  ", text, StringComparison.Ordinal);
            Assert.DoesNotContain("You's", text, StringComparison.Ordinal);
            Assert.False(Regex.IsMatch(text, @"\bYou (wants|offers|agreed|delivered|took)\b"), $"The player as the subject of a verb: {text}");
            Assert.EndsWith(".", text, StringComparison.Ordinal);
        }

        // A price set against the week's average says how far off it is; within a tenth of it, nothing.
        Contract wanted = contracts[0];
        Assert.Contains("200% above the week's sector average of ₵1.00", NewsText.Describe(context, new NewsItem(wanted.Id, NewsKind.Posted, 0, AverageUnit: Credits.Of(1m))), StringComparison.Ordinal);
        Assert.DoesNotContain("average", NewsText.Describe(context, new NewsItem(wanted.Id, NewsKind.Posted, 0, AverageUnit: Credits.Of(3m))), StringComparison.Ordinal);
    }

    [Fact]
    public void The_map_says_what_tier_each_place_is_and_how_old_what_the_player_knows_of_it_is()
    {
        var session = new GameSession(TestWorld.Quiet(withPlayer: true));
        session.Context.RunTo(GameTime.At(0, PriceBulletin.Hour));
        MapViewModel map = MapView.Build(session, null);
        MapNode Node(string name) => map.Nodes.Single(n => n.Name == name);

        Assert.Equal((Tier.Here, Age.Live), (Node("Meridian Station").Tier, Node("Meridian Station").Age));
        Assert.StartsWith("live", Node("Meridian Station").AgeText, StringComparison.Ordinal);
        Assert.Equal((Tier.Near, "daily · Day 0 · 06:00"), (Node("Verdant").Tier, Node("Verdant").AgeText));
        Assert.StartsWith("Food ₵", Node("Verdant").Info, StringComparison.Ordinal);
        Assert.Equal((Tier.Far, "as of Day 0 · 00:00"), (Node("Rust Belt").Tier, Node("Rust Belt").AgeText));
        Assert.Contains("Ore ₵", Node("Rust Belt").Info, StringComparison.Ordinal);
        Assert.Equal(("no producers", "never present", Age.Old), (Node("Edge Station").Info, Node("Edge Station").AgeText, Node("Edge Station").Age));

        session.Context.RunTo(GameTime.At(5, 0)); // five days on, the start is old news
        Assert.Equal(Age.Old, MapView.Build(session, null).Nodes.Single(n => n.Name == "Rust Belt").Age);
        Assert.Equal(Age.Today, MapView.Build(session, null).Nodes.Single(n => n.Name == "Verdant").Age);
    }

    [Fact]
    public void Signals_name_what_stands_out_with_its_age()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        var session = new GameSession(context);
        ActorState fab = context.Actor("Meridian Fabworks");
        ActorState habs = context.Actor("Meridian Habs");
        context.Produce(fab, GoodType.Parts, fab.Producer!.StockCap); // full: idle
        context.Consume(habs, GoodType.Food, habs.Stock(GoodType.Food)); // empty larder: running short

        IReadOnlyList<SignalLine> signals = NewsView.Signals(session);
        Assert.Contains(signals, s => s.Kind == SignalKind.Idle && s.Text.Contains("Meridian Fabworks", StringComparison.Ordinal) && s.Age == Age.Live);
        Assert.Contains(signals, s => s.Kind == SignalKind.Short && s.Text.Contains("Meridian Habs", StringComparison.Ordinal) && s.Text.Contains("Food", StringComparison.Ordinal));
        Assert.Equal("Idle", MapView.Build(session, null).Nodes.Single(n => n.Name == "Meridian Station").Badge);

        // Days of readings of the farm, then its warehouse fills and its ask drops: a jump away, the next bulletin shows a low price.
        ActorState farm = context.Actor("Verdant Agricombine");
        context.RunTo(GameTime.At(4, PriceBulletin.Hour));
        context.Produce(farm, GoodType.Food, farm.Producer!.StockCap - farm.Stock(GoodType.Food));
        context.RunTo(GameTime.At(5, PriceBulletin.Hour));
        SignalLine low = Assert.Single(NewsView.Signals(session), s => s.Kind == SignalKind.PriceLow);
        Assert.Contains("Verdant Agricombine asks", low.Text, StringComparison.Ordinal);
        Assert.Contains("below your readings since Day 0", low.Text, StringComparison.Ordinal);
        Assert.Equal("daily bulletin, Day 5 · 06:00", low.AgeText);
        Assert.Equal("Price low", MapView.Build(session, null).Nodes.Single(n => n.Name == "Verdant").Badge);
    }

    [Fact]
    public void A_post_known_from_the_news_says_so_on_the_board_and_in_the_contract()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        var session = new GameSession(context);
        SaleContract food = context.PostWanted(context.Actor("Edge Station Holdfast"), GoodType.Food, 40m, 900m);
        context.RunTo(GameTime.At(1, PriceBulletin.Hour));

        BoardRow row = BoardView.Build(session).Rows.Single(r => r.Id == food.Id);
        Assert.Equal("From The Daily Accord, Day 1 · 06:00: it may have been taken since", row.Seen);
        Assert.Equal(Age.Today, row.Age);

        ContractViewModel view = ContractView.Build(session, food.Id);
        Assert.Equal(row.Seen, view.Known);
        Assert.Contains(view.Uncertain, l => l.Label == "Still open?" && l.Value == "unknown");
        Assert.Equal("Public record: no Accord contracts yet.", view.Record[0]);
        Assert.Contains(view.Record, l => l.StartsWith("Outside the Accord", StringComparison.Ordinal));
        Assert.Equal("Your own dealings with Edge Station Holdfast: none.", view.Record[^1]);

        NewsViewModel news = NewsView.Build(session);
        Assert.Equal("The Daily Accord · Day 1", news.Title);
        Assert.Contains(news.Items, i => i.Contract == food.Id && i.Text.StartsWith("Wanted at Edge Station: 40 Food", StringComparison.Ordinal));
    }

    [Fact]
    public void A_party_with_accord_contracts_shows_its_public_record_and_the_goods_line_its_age()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        var session = new GameSession(context);
        ActorState metropole = context.Actor("Helios Prime Metropole");
        context.PostWanted(metropole, GoodType.Medicine, 5m, 600m); // handed over on the spot: on its record as paid
        SaleContract wanted = context.PostWanted(metropole, GoodType.Food, 40m, 900m);
        context.RunTo(context.CurrentTick + Visibility.ReachNear);

        ContractViewModel view = ContractView.Build(session, wanted.Id);
        Assert.Equal("Public record: paid for 1 in full.", view.Record[0]);
        Assert.Null(view.Known);
        MoneyLine goods = view.Lines.Single(l => l.Label.StartsWith("Goods, bought at", StringComparison.Ordinal));
        Assert.Contains("at its ask as you know it", goods.Note, StringComparison.Ordinal);
        Assert.Contains("as of Day 0 · 00:00, the start", goods.Note, StringComparison.Ordinal); // the first bulletin isn't out yet
        Assert.Contains(view.Uncertain, l => l.Label == "The ask at Verdant when you sign");
    }
}
