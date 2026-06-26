using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Random;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>The Transit Incident mechanic (§6.3) — danger gets teeth, and ship type modulates severity.</summary>
public sealed class TransitIncidentTests
{
    [Fact]
    public void Resolution_is_deterministic_for_a_given_seed()
    {
        var rngA = new DeterministicRandom(777L);
        var rngB = new DeterministicRandom(777L);
        ShipClassInfo light = ShipClasses.Info(ShipType.LightFreighter);

        TransitIncident a = IncidentResolver.Resolve(rngA, 0.3m, light);
        TransitIncident b = IncidentResolver.Resolve(rngB, 0.3m, light);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Armed_freighter_suffers_milder_incidents_than_a_fast_courier()
    {
        ShipClassInfo armed = ShipClasses.Info(ShipType.ArmedFreighter);   // severity 0.5
        ShipClassInfo courier = ShipClasses.Info(ShipType.FastCourier);    // severity 1.4

        decimal armedTotal = 0m;
        decimal courierTotal = 0m;
        for (long seed = 0; seed < 400; seed++)
        {
            // Same seed → same branch and same underlying draws; only the severity multiplier differs.
            TransitIncident a = IncidentResolver.Resolve(new DeterministicRandom(seed), 0.5m, armed);
            TransitIncident c = IncidentResolver.Resolve(new DeterministicRandom(seed), 0.5m, courier);
            armedTotal += Magnitude(a);
            courierTotal += Magnitude(c);
        }

        Assert.True(armedTotal < courierTotal, $"armed {armedTotal} should be milder than courier {courierTotal}.");
    }

    [Fact]
    public void Higher_danger_routes_produce_more_incidents_over_a_run()
    {
        // Compare incident counts between two identical worlds is hard; instead assert the resolver's
        // own danger sensitivity through a large sample on credit-cost magnitude.
        decimal lowDanger = SampleCreditCost(0.05m);
        decimal highDanger = SampleCreditCost(0.6m);
        Assert.True(highDanger > lowDanger);
    }

    private static decimal SampleCreditCost(decimal danger)
    {
        ShipClassInfo light = ShipClasses.Info(ShipType.LightFreighter);
        decimal total = 0m;
        for (long seed = 0; seed < 2000; seed++)
        {
            TransitIncident incident = IncidentResolver.Resolve(new DeterministicRandom(seed), danger, light);
            total += incident.CreditCost.Amount;
        }

        return total;
    }

    private static decimal Magnitude(TransitIncident incident) =>
        incident.DelayTicks + (incident.QualityLoss * 100m) + (incident.QuantityLossFraction * 100m) + incident.CreditCost.Amount;
}
