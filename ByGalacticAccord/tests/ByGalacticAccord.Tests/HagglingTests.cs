using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;

namespace ByGalacticAccord.Tests;

/// <summary>
/// Posting, responses and haggling (plan S5, design §3.6, §16.5, §16.6): the player's posts reach the actors that can see
/// them and draw acceptances or counters over game time; counters go back and forth, each reply after 1–12 game hours; an
/// agreed offer re-scales the terms; AI actors haggle among themselves in closed form; and the clock must run for any of it.
/// </summary>
public sealed class HagglingTests
{
    [Fact]
    public void A_food_post_at_verdant_draws_the_producers_counter_and_accepting_it_hands_the_food_over()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true, answers: true);
        ActorState player = context.Player();
        ActorState farm = context.Actor("Verdant Agricombine");
        decimal ask = DecisionEngine.Ask(farm).Amount * 60m;
        Contract post = PostWantedFood(context, player, price: 300m);
        Assert.True(300m < ask); // below what the farm asks for 60 food

        context.RunTo(context.CurrentTick + Haggling.SlowestReply);

        Negotiation thread = context.NegotiationWith(post.Id, farm.Id) ?? throw new Xunit.Sdk.XunitException("The farm didn't answer.");
        OfferMadeEntry counter = context.Journal.Entries.OfType<OfferMadeEntry>().Single(e => e.By == farm.Id);
        Assert.Equal((player.Id, 1, thread.Id), (counter.To, counter.Round, counter.Negotiation));
        Assert.True(counter.Offer.Price.Amount >= ask, "The farm counters at no less than its ask.");
        Assert.InRange(counter.ReplyHours!.Value, 1, 12);
        Assert.NotEqual(Tell.None, counter.Tell);
        Assert.Equal(player.Id, thread.AwaitingFrom);
        Assert.Equal(ContractStatus.Open, post.Status); // it stays open to others while they haggle

        Credits before = player.Credits;
        Assert.Equal(Signing.Signed, PlayerActions.AcceptOffer(context, player, thread));

        // Signed at the farm's counter, handed over from its stock into the player's storage at Verdant at once.
        Assert.Equal((ContractStatus.Fulfilled, farm.Id, counter.Offer.Price), (post.Status, post.CounterpartyId!.Value, post.Price));
        Assert.Equal(60m, context.StorageOf(player.Id, farm.Home)!.Held(GoodType.Food));
        Assert.Equal(NegotiationStatus.Agreed, thread.Status);
        Assert.Equal(System.Math.Round(before.Amount - (post.Price.Amount * (1m + post.TaxRate)), 2), System.Math.Round(player.Credits.Amount, 2));
        ContractSignedEntry signed = context.Journal.Entries.OfType<ContractSignedEntry>().Single(e => e.Contract == post.Id);
        Assert.IsType<OfferAcceptedEntry>(context.Journal.Find(signed.Cause!.Value)); // the signing follows from the accepted offer
        context.AssertSound();
    }

    [Fact]
    public void A_post_at_or_above_what_an_actor_needs_is_taken_as_posted_after_its_reply_time()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true, answers: true);
        ActorState player = context.Player();
        Contract post = PostWantedFood(context, player, price: 600m);

        Assert.Equal(ContractStatus.Open, post.Status); // nobody answers in no time
        context.RunTo(context.CurrentTick + Haggling.SlowestReply);

        Assert.NotEqual(ContractStatus.Open, post.Status);
        Assert.Equal(600m, post.Price.Amount);
        Assert.InRange(post.SignedTick!.Value - post.PostedTick, 1, Haggling.SlowestReply);
        context.RunUntilSettled(post);
        Assert.Equal(60m, context.StorageOf(player.Id, context.Place("Verdant").Id)!.Held(GoodType.Food));
        context.AssertSound();
    }

    [Fact]
    public void Reply_timing_is_deterministic_per_seed()
    {
        IReadOnlyList<string> first = HaggleRecord(42L);
        IReadOnlyList<string> second = HaggleRecord(42L);

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Haggling_cannot_advance_while_paused()
    {
        var session = new GameSession(TestWorld.Quiet(withPlayer: true, answers: true));
        SimulationContext context = session.Context;
        Contract post = PostWantedFood(context, session.Player, price: 300m);
        long posted = session.Now;

        for (int frame = 0; frame < 40; frame++)
        {
            Assert.False(session.Advance(0.25)); // paused: no game time passes, so no reply can come
        }

        Assert.Equal(posted, session.Now);
        Assert.Empty(context.Journal.Entries.OfType<OfferMadeEntry>());
        Assert.Empty(context.NegotiationsOn(post.Id));

        session.SetSpeed(ClockSpeed.X16);
        while (session.Now < posted + Haggling.SlowestReply)
        {
            session.Advance(0.25);
        }

        Assert.NotEmpty(context.Journal.Entries.OfType<OfferMadeEntry>());
    }

    [Fact]
    public void An_ai_poster_holds_to_a_fresh_post_and_settles_a_ripe_one_with_an_ai_trader_in_closed_form()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        ActorState juno = context.Actor("Juno Okafor");
        SaleContract wanted = Market.PostSale(
            context, habs, SaleSide.Wanted, GoodType.Food, 60m, habs.Home, Credits.Of(425m), Credits.Of(2000m), context.CurrentTick + 400,
            Holding.StockAt(habs.Home), TermPackage.Standard);

        // Too cheap for Juno to carry, and the Habs won't move on a fresh post.
        DecisionEngine.RunTrader(context, juno);
        Assert.Equal(ContractStatus.Open, wanted.Status);

        // Two days on, the Habs gives ground: they meet between Juno's costs and what the food is worth to the Habs.
        context.RunTo(context.CurrentTick + 48);
        DecisionEngine.RunTrader(context, juno);

        Assert.Equal((ContractStatus.Active, juno.Id), (wanted.Status, wanted.CounterpartyId!.Value));
        Assert.True(wanted.Price.Amount > 425m && wanted.Price.Amount < 2000m, $"agreed at {wanted.Price}");
        Assert.Equal(1, context.DealsHaggled);

        // The terms follow the agreed price, not the posted one.
        Assert.Equal(System.Math.Round(wanted.Price.Amount * 0.005m, 2), wanted.LatePenalty!.PenaltyPerTickLate);
        Assert.Equal(Credits.Of(System.Math.Round(wanted.Price.Amount * 0.15m, 2)), wanted.PerformanceBond);
        Assert.Equal(Credits.Of(System.Math.Round(wanted.Price.Amount * 0.1m, 2)), wanted.Terms.BreakFee);
        context.RunUntilSettled(wanted);
        context.AssertSound();
    }

    [Fact]
    public void A_counter_can_move_the_package_the_late_fee_and_the_deadline_and_the_terms_follow_it()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState farm = context.Actor("Verdant Agricombine");
        HaulContract haul = PlayerActionsTests.FarmHaul(context); // ₵70, worth up to ₵90 to the farm
        context.RunTo(context.CurrentTick + 48); // a post that has waited
        var offer = new Offer(haul.OfferedPrice, haul.DeadlineTick + 24, TermPackage.Lenient, LateFeeScale: 2m);

        Assert.Equal(DealBlock.None, PlayerActions.CheckCounter(context, player, haul, offer));
        Negotiation thread = PlayerActions.Counter(context, player, haul, offer)!;
        Assert.Equal((farm.Id, player.Id, 1), (thread.Poster, thread.Responder, thread.Rounds));
        Assert.Equal(DealBlock.NotYourTurn, PlayerActions.CheckCounter(context, player, haul, offer));
        context.RunTo(thread.ReplyDue!.Value);

        // The farm accepts: the player carries it on the terms it asked for, rebuilt from its price.
        OfferAcceptedEntry accepted = context.Journal.Entries.OfType<OfferAcceptedEntry>().Single();
        Assert.Equal((farm.Id, player.Id), (accepted.By, accepted.To));
        Assert.Equal((ContractStatus.Active, player.Id), (haul.Status, haul.Carrier!.Value));
        Assert.Equal((TermPackage.Lenient, 2m, offer.Deadline), (haul.Terms.Package, haul.Terms.LateFeeScale, haul.DeadlineTick));
        Assert.Equal(System.Math.Round(70m * 0.005m * 2m, 2), haul.LatePenalty!.PenaltyPerTickLate);
        Assert.Null(haul.Terms.Liability); // lenient hauls carry no cargo liability
        Assert.Equal(NegotiationStatus.Agreed, thread.Status);
        context.RunUntilSettled(haul);
        Assert.Equal(ContractStatus.Fulfilled, haul.Status);
        context.AssertSound();
    }

    [Fact]
    public void An_ai_poster_concedes_round_by_round_and_the_player_takes_its_counter()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        HaulContract haul = PlayerActionsTests.FarmHaul(context);
        context.RunTo(context.CurrentTick + 48);

        Negotiation thread = PlayerActions.Counter(context, player, haul, Offer.Of(haul) with { Price = Credits.Of(92m) })!;
        context.RunTo(thread.ReplyDue!.Value);

        // ₵92 is more than the farm will pay, but not out of reach: it counters above its post, and below the ask.
        Bid reply = thread.Latest;
        Assert.Equal((2, haul.Shipper), (reply.Round, reply.By));
        Assert.InRange(reply.Offer.Price.Amount, 70.01m, 91.99m);
        Assert.Equal(reply.Tick - thread.Bids[1].Tick, reply.ReplyHours);

        Assert.Equal(Signing.Signed, PlayerActions.AcceptOffer(context, player, thread));
        Assert.Equal((player.Id, reply.Offer.Price), (haul.Carrier!.Value, haul.Price));
        context.AssertSound();
    }

    [Fact]
    public void An_offer_too_far_from_the_reservation_is_walked_away_from()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        HaulContract haul = PlayerActionsTests.FarmHaul(context);

        Negotiation thread = PlayerActions.Counter(context, player, haul, Offer.Of(haul) with { Price = Credits.Of(200m) })!;
        context.RunTo(thread.ReplyDue!.Value);

        NegotiationEndedEntry ended = context.Journal.Entries.OfType<NegotiationEndedEntry>().Single();
        Assert.Equal((NegotiationEnd.WalkedAway, haul.Shipper), (ended.End, ended.By));
        Assert.Equal((NegotiationStatus.Ended, NegotiationEnd.WalkedAway), (thread.Status, thread.End));
        Assert.Equal(ContractStatus.Open, haul.Status); // still on the board as posted
        Assert.Equal(DealBlock.None, PlayerActions.CheckHaul(context, player, haul).Block);
    }

    [Fact]
    public void After_six_rounds_without_a_deal_the_responder_walks_away()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true, answers: true);
        ActorState player = context.Player();
        ActorState farm = context.Actor("Verdant Agricombine");
        Contract post = PostWantedFood(context, player, price: 300m);
        context.RunTo(context.CurrentTick + Haggling.SlowestReply);
        Negotiation thread = context.NegotiationWith(post.Id, farm.Id)!;

        // The player holds at ₵300, under what the farm asks: the farm concedes toward its ask but never below it.
        while (thread.IsOpen && thread.AwaitingFrom == player.Id)
        {
            Assert.NotNull(PlayerActions.Counter(context, player, post, thread.Latest.Offer with { Price = Credits.Of(300m) }, thread));
            context.RunTo(thread.ReplyDue!.Value);
        }

        Assert.Equal((NegotiationStatus.Ended, NegotiationEnd.RoundsRanOut), (thread.Status, thread.End));
        Assert.Equal(6, thread.Rounds);
        decimal[] farmOffers = thread.Bids.Where(b => b.By == farm.Id).Select(b => b.Offer.Price.Amount).ToArray();
        Assert.Equal(farmOffers.OrderByDescending(p => p), farmOffers); // each counter conceded a little
        Assert.All(farmOffers, p => Assert.True(p >= DecisionEngine.Ask(farm).Amount * 60m));
    }

    [Fact]
    public void The_player_posts_a_lot_and_a_haul_of_its_stored_goods_and_each_is_checked()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState farm = context.Actor("Verdant Agricombine");
        LocationId verdant = farm.Home;
        LocationId meridian = context.Place("Meridian Station").Id;
        long deadline = context.CurrentTick + 96;
        var offer = new Offer(Credits.Of(400m), deadline, TermPackage.Standard);

        // Nothing stored yet.
        Assert.Equal(PostBlock.NoGoods, PlayerActions.CheckPost(context, player, new PostDraft(PostKind.Lot, GoodType.Food, 50m, verdant, null, offer)));
        context.PutInStorage(player, verdant, context.TakeFromStock(farm, GoodType.Food, 100m, reserved: false));

        Assert.Equal(PostBlock.BadQuantity, PlayerActions.CheckPost(context, player, new PostDraft(PostKind.Lot, GoodType.Food, 0m, verdant, null, offer)));
        Assert.Equal(PostBlock.BadPrice, PlayerActions.CheckPost(context, player, new PostDraft(PostKind.Lot, GoodType.Food, 50m, verdant, null, offer with { Price = Credits.Zero })));
        Assert.Equal(PostBlock.BadDeadline, PlayerActions.CheckPost(context, player, new PostDraft(PostKind.Lot, GoodType.Food, 50m, verdant, null, offer with { Deadline = context.CurrentTick })));
        Assert.Equal(PostBlock.SamePlace, PlayerActions.CheckPost(context, player, new PostDraft(PostKind.Haul, GoodType.Food, 50m, verdant, verdant, offer)));
        Assert.Equal(PostBlock.CannotAfford, PlayerActions.CheckPost(context, player, new PostDraft(PostKind.Wanted, GoodType.Food, 50m, verdant, null, offer with { Price = Credits.Of(9000m) })));

        var lot = (SaleContract)PlayerActions.Post(context, player, new PostDraft(PostKind.Lot, GoodType.Food, 50m, verdant, null, offer))!;
        var haul = (HaulContract)PlayerActions.Post(
            context, player, new PostDraft(PostKind.Haul, GoodType.Food, 50m, verdant, meridian, new Offer(Credits.Of(90m), deadline, TermPackage.Strict, 0.5m)))!;

        Assert.Equal((SaleSide.ForSale, player.Id, Credits.Of(400m)), (lot.Side, lot.PostedBy, lot.OfferedPrice));
        Assert.Equal((player.Id, TermPackage.Strict, 0.5m), (haul.Shipper, haul.Terms.Package, haul.Terms.LateFeeScale));
        Assert.Equal(100m, context.StorageOf(player.Id, verdant)!.ReservedOf(GoodType.Food)); // both set aside
        Assert.Equal(PostBlock.NoGoods, PlayerActions.CheckPost(context, player, new PostDraft(PostKind.Lot, GoodType.Food, 1m, verdant, null, offer)));
        Assert.Equal(lot.OfferedPrice, lot.Reservation); // the player has no hidden number

        Assert.True(PlayerActions.Withdraw(context, player, lot));
        Assert.Equal(ContractStatus.Withdrawn, lot.Status);
        Assert.Equal(50m, context.StorageOf(player.Id, verdant)!.ReservedOf(GoodType.Food));
        context.AssertSound();
    }

    [Fact]
    public void Withdrawing_a_post_ends_its_haggles_and_another_taker_ends_the_rest()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true, answers: true);
        ActorState player = context.Player();
        Contract post = PostWantedFood(context, player, price: 300m);
        context.RunTo(context.CurrentTick + Haggling.SlowestReply);
        IReadOnlyList<Negotiation> threads = context.NegotiationsOn(post.Id);
        Assert.NotEmpty(threads);

        Assert.True(PlayerActions.Withdraw(context, player, post));

        Assert.All(threads, t => Assert.Equal((NegotiationStatus.Ended, NegotiationEnd.PostClosed), (t.Status, t.End)));
        Assert.Empty(context.NegotiationsOn(post.Id));
        Assert.False(PlayerActions.Withdraw(context, player, post));

        // The player haggles over the farm's haul; Juno takes it as posted meanwhile, and the haggle ends with the post.
        HaulContract haul = PlayerActionsTests.FarmHaul(context);
        Negotiation mine = PlayerActions.Counter(context, player, haul, Offer.Of(haul) with { Price = Credits.Of(85m) })!;
        ActorState juno = context.Actor("Juno Okafor");
        Assert.Equal(Signing.Signed, Market.TakeHaul(context, juno, haul, context.ShipOf(juno), haul.OfferedPrice));
        Assert.Equal((NegotiationStatus.Ended, NegotiationEnd.PostClosed), (mine.Status, mine.End));
        context.RunTo(mine.ReplyDue ?? context.CurrentTick + Haggling.SlowestReply);
        Assert.Empty(context.Journal.Entries.OfType<OfferAcceptedEntry>());
    }

    [Fact]
    public void The_player_as_buyer_is_asked_when_its_seller_defaults()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        ActorState player = context.Player();
        ActorState juno = context.Actor("Juno Okafor");
        LocationId meridian = context.Place("Meridian Station").Id;
        var wanted = (SaleContract)PlayerActions.Post(
            context, player, new PostDraft(PostKind.Wanted, GoodType.Food, 40m, meridian, null, new Offer(Credits.Of(700m), context.CurrentTick + 10, TermPackage.Strict)))!;
        Assert.Equal(Holding.StorageAt(meridian), wanted.Receipt);
        Ship ship = context.ShipOf(juno); // at Verdant, 30 h away
        Assert.Equal(Signing.Signed, Market.FillWithShip(context, juno, wanted, wanted.OfferedPrice, ship, DecisionEngine.FillSources(context, juno, wanted, ship).Single()));

        // Past the six hours' grace with nothing delivered: the player decides, and the world waits.
        Assert.Equal(RunStop.PlayerDecision, context.RunTo(wanted.GraceEndsTick + 5));
        PlayerDecision decision = context.PlayerDecisions.Single();
        Assert.Equal((DecisionKind.CancelForDefault, wanted.Id), (decision.Kind, decision.Contract));

        Assert.True(PlayerActions.Decide(context, player, decision, DecisionChoice.Wait));
        context.RunUntilSettled(wanted);

        GoodsHandedOverEntry handed = context.Journal.Entries.OfType<GoodsHandedOverEntry>().Single(e => e.Contract == wanted.Id);
        Assert.Equal((juno.Id, player.Id), (handed.Seller, handed.Buyer));
        Assert.True(handed.LateFee.Amount > 0m);
        Assert.Equal(40m, context.StorageOf(player.Id, meridian)!.Held(GoodType.Food));
        context.AssertSound();
    }

    // The player posts a wanted Sale for 60 food delivered into its storage at Verdant, for three days.
    private static Contract PostWantedFood(SimulationContext context, ActorState player, decimal price) =>
        PlayerActions.Post(
            context, player,
            new PostDraft(PostKind.Wanted, GoodType.Food, 60m, context.Place("Verdant").Id, null, new Offer(Credits.Of(price), context.CurrentTick + 72, TermPackage.Standard)))
        ?? throw new Xunit.Sdk.XunitException("The post was refused.");

    // A living world with the player: it posts food wanted at Verdant and haggles with whoever answers first, and every
    // haggle and signing is written down with its tick.
    private static IReadOnlyList<string> HaggleRecord(long seed)
    {
        var session = new GameSession(seed, startTick: GameTime.At(1, 6));
        SimulationContext context = session.Context;
        Contract post = PostWantedFood(context, session.Player, price: 300m);
        long end = context.CurrentTick + 72;
        while (context.CurrentTick < end)
        {
            context.RunTo(context.CurrentTick + 1);
            if (context.NegotiationsOn(post.Id).FirstOrDefault(n => n.AwaitingFrom == session.Player.Id) is { } thread && thread.Rounds < 4)
            {
                PlayerActions.Counter(context, session.Player, post, thread.Latest.Offer with { Price = Credits.Of(320m) }, thread);
            }
        }

        return context.Journal.Entries
            .Where(e => e is INegotiationEntry || (e is ContractSignedEntry s && s.Contract == post.Id))
            .Select(e => $"{e.Tick} {e.GetType().Name} {e.Seq} {e.Cause}")
            .ToList();
    }
}
