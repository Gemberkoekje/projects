using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;
using ByGalacticAccord.Presentation.Session;
using ByGalacticAccord.Presentation.Views;

namespace ByGalacticAccord.Tests;

/// <summary>
/// The take-home estimate uses the engine's own rules (design §11.5), so what it calls certain must be
/// what the engine then does, when nothing goes wrong on the way.
/// </summary>
public sealed class HaulEstimateTests
{
    [Theory]
    [InlineData(42L)]
    [InlineData(20260619L)]
    public void The_estimate_predicts_the_settlement_of_a_clean_run(long seed)
    {
        var session = new GameSession(seed, startTick: 48);
        Contract contract = session.Context.OpenContracts
            .Where(c => c.UsesEscrow && PlayerActions.CheckHaul(session.Context, session.Player, c).Ok)
            .OrderBy(c => HaulEstimate.For(session.Context, session.Player, session.PlayerShip, c)!.IncidentOdds)
            .First();
        HaulEstimate estimate = HaulEstimate.For(session.Context, session.Player, session.PlayerShip, contract)!;
        Credits before = session.Player.Credits;

        Assert.True(session.TakeHaul(contract.Id).Signed);
        session.SetSpeed(ClockSpeed.X16);
        while (contract.Status == ContractStatus.Active)
        {
            session.Advance(0.25);
            if (session.Speed == ClockSpeed.Paused)
            {
                session.SetSpeed(ClockSpeed.X16);
            }
        }

        Assert.DoesNotContain(session.Context.Journal.Entries.OfType<IncidentEntry>(), e => e.Contract == contract.Id);
        HaulDeliveredEntry delivered = session.Context.Journal.Entries.OfType<HaulDeliveredEntry>().Single(e => e.Contract == contract.Id);
        Assert.Equal(estimate.ArrivalTick, delivered.Tick);
        Assert.Equal(estimate.Fee, delivered.Fee);
        Assert.Equal(estimate.QualityRefund, delivered.QualityRefund);
        Assert.Equal(estimate.LateFee, delivered.LateFee);
        Assert.Equal(estimate.TicksLate, delivered.TicksLate);

        // The engine doesn't charge the player running costs yet (plan S3), so the change in credits is the
        // take-home without them. To the cent: the engine's fee divisions leave noise in the 26th place.
        Credits expected = estimate.TakeHome + estimate.Running;
        Assert.Equal(System.Math.Round(expected.Amount, 2), System.Math.Round((session.Player.Credits - before).Amount, 2));
    }

    [Fact]
    public void Outside_the_Accord_the_pilot_pays_for_the_goods()
    {
        var session = new GameSession(42L, startTick: 48);
        Contract frontier = session.Context.OpenContracts.First(c => !c.UsesEscrow);

        HaulEstimate estimate = HaulEstimate.For(session.Context, session.Player, session.PlayerShip, frontier)!;

        Assert.Equal(frontier.GoodsCost, estimate.GoodsFronted);
        Assert.Equal(Credits.Zero, estimate.Bond);
        Assert.Equal(Credits.Zero, estimate.LateFee); // no bond, so nothing for a late fee to draw on
        Assert.Equal(
            estimate.Fee - estimate.GoodsFronted - estimate.Running - estimate.Docking - estimate.QualityRefund,
            estimate.TakeHome);
    }

    [Fact]
    public void Running_costs_cover_the_empty_leg_and_the_laden_one()
    {
        var session = new GameSession(42L, startTick: 48);
        Contract away = session.Context.OpenContracts.First(c => c.OriginLocationId != session.PlayerShip.Location);

        HaulEstimate estimate = HaulEstimate.For(session.Context, session.Player, session.PlayerShip, away)!;

        Assert.NotNull(estimate.Repositioning);
        Assert.True(estimate.RepoTicks > 0);
        Assert.Equal(session.PlayerShip.Class.RunningCostPerTick.Amount * (estimate.RepoTicks + estimate.LadenTicks), estimate.Running.Amount);
        Assert.Equal(session.Now + estimate.RepoTicks + estimate.LadenTicks, estimate.ArrivalTick);
    }

    [Fact]
    public void A_busy_ship_is_estimated_from_where_and_when_it_is_next_free()
    {
        var session = new GameSession(42L, startTick: 48);
        Contract first = session.Context.OpenContracts.First(c => c.UsesEscrow && PlayerActions.CheckHaul(session.Context, session.Player, c).Ok);
        long arrival = HaulEstimate.For(session.Context, session.Player, session.PlayerShip, first)!.ArrivalTick;
        session.TakeHaul(first.Id);

        (LocationId at, long tick) = HaulEstimate.NextFree(session.Context, session.PlayerShip);
        Assert.Equal(first.DestinationLocationId, at);
        Assert.Equal(arrival, tick);

        Contract next = session.Context.OpenContracts.First(c => c.Status == ContractStatus.Open);
        HaulEstimate later = HaulEstimate.For(session.Context, session.Player, session.PlayerShip, next)!;
        Assert.Equal(arrival, later.StartTick);
        Assert.Equal(first.DestinationLocationId, later.StartAt);
    }

    [Fact]
    public void Food_on_a_long_unrefrigerated_run_carries_a_certain_warranty_refund()
    {
        var session = new GameSession(42L, startTick: 48);
        Contract food = session.Context.OpenContracts.First(c => c.Good == GoodType.Food && c.Warranty is not null);

        HaulEstimate estimate = HaulEstimate.For(session.Context, session.Player, session.PlayerShip, food)!;

        decimal quality = 1m - (Goods.Info(GoodType.Food).QualityDecayPerTick * estimate.LadenTicks);
        Assert.Equal(System.Math.Max(0m, quality), estimate.ArrivalQuality);
        Assert.Equal(food.Fee.Amount * food.Warranty!.RefundFraction(estimate.ArrivalQuality), estimate.QualityRefund.Amount);
        Assert.True(estimate.QualityRefund.Amount > 0m, "Every food route is longer than food keeps its warranty (plan S3).");
    }
}
