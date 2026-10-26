using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;

namespace ByGalacticAccord.Tests;

/// <summary>The client's game clock (design §8.3): speeds, the hard pause, and the player's commands.</summary>
public sealed class SessionTests
{
    [Fact]
    public void A_session_starts_paused_and_a_paused_clock_does_not_move()
    {
        var session = new GameSession(42L, startTick: 30);

        Assert.Equal(ClockSpeed.Paused, session.Speed);
        Assert.Equal(30, session.Now);
        Assert.False(session.Advance(0.25));
        Assert.Equal(30, session.Now);
        Assert.Equal(30.0, session.Clock);
    }

    [Theory]
    [InlineData(ClockSpeed.X1, 1)]
    [InlineData(ClockSpeed.X4, 4)]
    [InlineData(ClockSpeed.X16, 16)]
    public void Each_speed_runs_that_many_game_hours_per_real_second(ClockSpeed speed, int hoursPerSecond)
    {
        var session = new GameSession(42L);
        session.SetSpeed(speed);
        for (int frame = 0; frame < 32; frame++)
        {
            session.Advance(0.125); // four real seconds in all, in steps a double holds exactly
        }

        Assert.Equal(4 * hoursPerSecond, session.Now);
    }

    [Fact]
    public void The_clock_keeps_its_fraction_for_drawing_and_runs_whole_ticks()
    {
        var session = new GameSession(42L);
        session.SetSpeed(ClockSpeed.X1);

        Assert.False(session.Advance(0.25));
        Assert.Equal(0, session.Now);
        Assert.Equal(0.25, session.Clock);

        Assert.False(session.Advance(0.25));
        Assert.False(session.Advance(0.25));
        Assert.True(session.Advance(0.25));
        Assert.Equal(1, session.Now);
        Assert.Equal(1.0, session.Clock);
    }

    [Fact]
    public void A_long_frame_is_capped_so_a_hidden_tab_does_not_leap_ahead()
    {
        var session = new GameSession(42L);
        session.SetSpeed(ClockSpeed.X16);

        session.Advance(30.0);

        Assert.Equal((long)(GameSession.MaxFrameSeconds * 16), session.Now);
    }

    [Fact]
    public void The_clock_runs_the_same_world_as_one_RunTo()
    {
        var session = new GameSession(20260619L);
        session.SetSpeed(ClockSpeed.X16);
        while (session.Now < 2_000)
        {
            session.Advance(0.05);
        }

        SimulationContext direct = WorldGenerator.Generate(20260619L, withPlayer: true).Context;
        direct.RunTo(session.Now);
        Assert.Equal(TestWorld.Signature(direct), TestWorld.Signature(session.Context));
    }

    [Fact]
    public void A_player_alert_stops_the_clock_and_resuming_acknowledges_it()
    {
        var session = new GameSession(42L);
        session.SetSpeed(ClockSpeed.X16);
        session.Context.RaisePlayerAlert(new IncidentEntry(0, new ContractId(1), session.Player.Id, default));

        session.Advance(0.25);

        Assert.Equal(ClockSpeed.Paused, session.Speed);
        Assert.Equal(session.Now, session.Clock);
        Assert.Single(session.Alerts);

        session.SetSpeed(ClockSpeed.X1);
        Assert.Empty(session.Alerts);
        Assert.Equal(ClockSpeed.X1, session.Speed);
    }

    [Fact]
    public void Accepting_a_contract_signs_it_or_says_why_not()
    {
        var session = new GameSession(42L, startTick: 48);
        HaulContract first = PlayerActionsTests.FarmHaul(session.Context);
        HaulContract second = PlayerActionsTests.FarmHaul(session.Context);
        long revision = session.Revision;

        DealOutcome signed = session.Accept(first.Id);

        Assert.True(signed.Signed);
        Assert.True(session.Revision > revision);
        Assert.Equal(first.Id, session.PlayerShip.Job!.Haul);
        Assert.Same(first, Assert.Single(session.PlayerActiveContracts));

        DealOutcome refused = session.Accept(second.Id);
        Assert.False(refused.Signed);
        Assert.Equal(DealBlock.NoFreeShip, refused.Block);
        Assert.Null(refused.Signing);
        Assert.Equal(DealBlock.NotOpen, session.Accept(first.Id).Block);
    }

    [Fact]
    public void Goods_aboard_go_into_storage_and_come_back()
    {
        var session = new GameSession(42L, startTick: 48);
        SaleTests.GiveAboard(session.Context, session.PlayerShip, session.Player, GoodType.Parts, 25m);
        long revision = session.Revision;

        Assert.True(session.Store(GoodType.Parts));
        Assert.True(session.Revision > revision);
        Assert.Null(session.PlayerShip.LotOf(session.Player.Id, GoodType.Parts));
        Assert.False(session.Store(GoodType.Parts)); // nothing left aboard

        Assert.True(session.Retrieve(GoodType.Parts));
        Assert.Equal(25m, session.PlayerShip.LotOf(session.Player.Id, GoodType.Parts)!.Quantity);
        session.Context.AssertSound();
    }

    [Fact]
    public void A_decision_stops_the_clock_and_it_wont_run_again_until_the_player_answers()
    {
        (SimulationContext context, SaleContract sale, HaulContract haul) = FailurePathTests.PlayerBrokerWithLateCarrier();
        var session = new GameSession(context);
        session.SetSpeed(ClockSpeed.X16);
        while (session.Speed != ClockSpeed.Paused)
        {
            session.Advance(0.25);
        }

        Assert.Equal(haul.GraceEndsTick, session.Now);
        PlayerDecision decision = Assert.Single(session.Decisions);
        Assert.Equal("waiting for your decision", Presentation.Views.HeaderView.Build(session).SpeedNote);

        // The speed buttons don't start it: the world waits for the answer.
        session.SetSpeed(ClockSpeed.X16);
        Assert.Equal(ClockSpeed.Paused, session.Speed);
        Assert.False(session.Advance(0.25));

        long revision = session.Revision;
        Assert.True(session.Decide(decision, DecisionChoice.Wait));
        Assert.True(session.Revision > revision);
        Assert.Empty(session.Decisions);
        session.SetSpeed(ClockSpeed.X16);
        Assert.True(session.Advance(0.25));
        Assert.Equal(ContractStatus.Active, sale.Status);
    }

    [Fact]
    public void The_player_fills_through_a_carrier_walks_away_and_raises_its_haul_through_the_session()
    {
        SimulationContext context = TestWorld.Quiet(withPlayer: true);
        var session = new GameSession(context);
        SaleContract wanted = context.PostWanted(context.Actor("Meridian Habs"), GoodType.Food, 40m, 700m, hours: 200);

        DealOutcome brokered = session.Broker(wanted.Id);
        Assert.True(brokered.Signed);
        Assert.Equal(session.Player.Id, wanted.Seller);
        HaulContract haul = Assert.IsType<HaulContract>(Assert.Single(session.PlayerOpenPosts));
        Assert.Equal(DealBlock.NotOpen, session.Broker(wanted.Id).Block);

        Assert.True(session.HaulAgain(wanted.Id));
        Assert.Equal(ContractStatus.Withdrawn, haul.Status);
        Assert.Single(session.PlayerOpenPosts);

        Assert.True(session.CancelAtWill(wanted.Id));
        Assert.Equal(ContractStatus.Cancelled, wanted.Status);
        Assert.Empty(session.PlayerOpenPosts); // the haul for it went with it
        Assert.False(session.CancelForDefault(wanted.Id));
        context.AssertSound();
    }
}
