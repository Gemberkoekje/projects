using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>Unit tests for the value objects and clause maths the contract system rides on.</summary>
public sealed class DomainTests
{
    [Fact]
    public void Credits_arithmetic_is_exact()
    {
        Credits a = Credits.Of(10.25m);
        Credits b = Credits.Of(4.75m);
        Assert.Equal(15.00m, (a + b).Amount);
        Assert.Equal(5.50m, (a - b).Amount);
        Assert.Equal(20.50m, (a * 2m).Amount);
        Assert.True(a > b);
    }

    [Fact]
    public void Late_penalty_scales_with_lateness_and_caps_at_the_bond()
    {
        var clause = new PenaltyOnLateDelivery(PenaltyPerTickLate: 2m, MaxPenalty: Credits.Of(50m));

        Assert.Equal(0m, clause.PenaltyFor(0).Amount);
        Assert.Equal(20m, clause.PenaltyFor(10).Amount);
        Assert.Equal(50m, clause.PenaltyFor(1000).Amount); // capped
    }

    [Fact]
    public void Quality_warranty_only_refunds_below_threshold()
    {
        var warranty = new QualityWarranty(MinQuality: 0.7m, MaxRefundFraction: 0.5m);

        Assert.Equal(0m, warranty.RefundFraction(0.8m));            // fine quality, no refund
        Assert.True(warranty.RefundFraction(0.35m) > 0m);          // half quality, some refund
        Assert.True(warranty.RefundFraction(0m) <= 0.5m);          // never exceeds the cap
    }

    [Fact]
    public void Perishables_decay_and_refrigeration_slows_it()
    {
        Cargo food = Cargo.Fresh(GoodType.Food, 100m);
        Cargo plain = food.Decayed(50, preservation: 0m);
        Cargo chilled = food.Decayed(50, preservation: 0.85m);

        Assert.True(plain.Quality < 1m);
        Assert.True(chilled.Quality > plain.Quality);
    }

    [Fact]
    public void Non_perishables_do_not_decay()
    {
        Cargo ore = Cargo.Fresh(GoodType.Ore, 100m);
        Assert.Equal(1m, ore.Decayed(1000, preservation: 0m).Quality);
    }

    [Fact]
    public void FindPath_spans_multiple_hops_and_inherits_the_worst_legs_danger()
    {
        var ctx = new SimulationContext(1L);
        var a = new Location(ctx.NextLocationId(), "A", LocationType.CoreWorld, "R", AccordReach.DeepAccord);
        var b = new Location(ctx.NextLocationId(), "B", LocationType.IndustrialWorld, "R", AccordReach.AccordEdge);
        var c = new Location(ctx.NextLocationId(), "C", LocationType.FrontierOutpost, "R", AccordReach.OutsideAccord);
        ctx.Add(a);
        ctx.Add(b);
        ctx.Add(c);
        ctx.Add(new Route(a.Id, b.Id, 10, 0.05m, AccordReach.AccordEdge));
        ctx.Add(new Route(b.Id, c.Id, 20, 0.40m, AccordReach.OutsideAccord));

        RoutePlan? plan = ctx.FindPath(a.Id, c.Id);

        Assert.NotNull(plan);
        Assert.Equal(30, plan!.DistanceTicks);
        Assert.Equal(2, plan.Hops);
        Assert.Equal(0.40m, plan.MaxDanger); // the red leg dominates
    }

    [Fact]
    public void FindPath_returns_null_when_unreachable()
    {
        var ctx = new SimulationContext(1L);
        var a = new Location(ctx.NextLocationId(), "A", LocationType.CoreWorld, "R", AccordReach.DeepAccord);
        var island = new Location(ctx.NextLocationId(), "Island", LocationType.FrontierOutpost, "R", AccordReach.OutsideAccord);
        ctx.Add(a);
        ctx.Add(island);

        Assert.Null(ctx.FindPath(a.Id, island.Id));
    }

    [Fact]
    public void All_five_hulls_and_seven_goods_are_defined()
    {
        Assert.Equal(5, ShipClasses.All.Count);
        Assert.Equal(7, Goods.All.Count);
        foreach (ShipType type in ShipClasses.All)
        {
            Assert.True(ShipClasses.Info(type).CargoCapacity > 0m);
        }
    }
}
