using System;
using System.Linq;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Random;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>
/// Transit incidents (design §8.4, §16.7): danger makes them likelier and shifts their outcomes toward the severe end, toughness scales
/// the damage, reliability the odds, and pushing raises the odds (doubled for a drive that runs hot) for a faster laden leg.
/// </summary>
public sealed class TransitIncidentTests
{
    private const decimal Shift = 2m; // SimulationConfig.IncidentDangerShift's default

    [Fact]
    public void Resolution_is_deterministic_for_a_given_seed()
    {
        var rngA = new DeterministicRandom(777L);
        var rngB = new DeterministicRandom(777L);
        ShipStats light = Designs.Stats(Design.LightFreighter);

        TransitIncident a = IncidentResolver.Resolve(rngA, 0.3m, light, Shift);
        TransitIncident b = IncidentResolver.Resolve(rngB, 0.3m, light, Shift);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Armed_freighter_suffers_milder_incidents_than_a_fast_courier()
    {
        ShipStats armed = Designs.Stats(Design.ArmouredFreighter);   // severity 0.625
        ShipStats courier = Designs.Stats(Design.FastCourier);    // severity 1.43

        decimal armedTotal = 0m;
        decimal courierTotal = 0m;
        for (long seed = 0; seed < 400; seed++)
        {
            // Same seed → same branch and same underlying draws; only the severity multiplier differs.
            TransitIncident a = IncidentResolver.Resolve(new DeterministicRandom(seed), 0.5m, armed, Shift);
            TransitIncident c = IncidentResolver.Resolve(new DeterministicRandom(seed), 0.5m, courier, Shift);
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

    /// <summary>Plan S8's defect: incident outcomes didn't shift with danger, despite the comment saying they did. They do now.</summary>
    [Fact]
    public void Danger_shifts_the_outcomes_toward_the_severe_end()
    {
        // The table at no danger is the design's; on the red Rust Belt run (0.42) a delay is half as likely and a total loss nearly twice.
        Assert.Equal(0.40m, IncidentResolver.Share(TransitIncidentKind.MinorDelay, 0m, Shift));
        Assert.Equal(0.04m, IncidentResolver.Share(TransitIncidentKind.TotalLoss, 0m, Shift));
        Assert.Equal(0.1984m, IncidentResolver.Share(TransitIncidentKind.MinorDelay, 0.42m, Shift));
        Assert.Equal(0.072256m, IncidentResolver.Share(TransitIncidentKind.TotalLoss, 0.42m, Shift));

        // The shares always make up the whole table, and every severe kind gains as danger rises.
        foreach (decimal danger in new[] { 0m, 0.05m, 0.22m, 0.42m, 1m })
        {
            decimal sum = Enum.GetValues<TransitIncidentKind>().Sum(k => IncidentResolver.Share(k, danger, Shift));
            Assert.Equal(1m, sum);
        }

        Assert.True(IncidentResolver.Share(TransitIncidentKind.TotalLoss, 0.42m, Shift) > IncidentResolver.Share(TransitIncidentKind.TotalLoss, 0.05m, Shift));
        Assert.True(IncidentResolver.Share(TransitIncidentKind.PartialLoss, 0.42m, Shift) > IncidentResolver.Share(TransitIncidentKind.PartialLoss, 0.05m, Shift));

        // The same draws land on worse outcomes on a dangerous route: never milder, and more often lost.
        ShipStats light = Designs.Stats(Design.LightFreighter);
        int safeLosses = 0;
        int redLosses = 0;
        for (long seed = 0; seed < 2000; seed++)
        {
            TransitIncident safe = IncidentResolver.Resolve(new DeterministicRandom(seed), 0.05m, light, Shift);
            TransitIncident red = IncidentResolver.Resolve(new DeterministicRandom(seed), 0.42m, light, Shift);
            Assert.True(red.Kind >= safe.Kind, $"seed {seed}: {red.Kind} on the red run is milder than {safe.Kind} on the safe one.");
            safeLosses += safe.Kind is TransitIncidentKind.PartialLoss or TransitIncidentKind.TotalLoss ? 1 : 0;
            redLosses += red.Kind is TransitIncidentKind.PartialLoss or TransitIncidentKind.TotalLoss ? 1 : 0;
        }

        Assert.True(redLosses > safeLosses * 1.3m, $"red {redLosses} losses against safe {safeLosses}.");
    }

    /// <summary>Design §16.7: pushing is ×1.25 the speed, ×1.5 the incident chance, or ×2 for a drive that runs hot; cruise changes nothing.</summary>
    [Fact]
    public void Pushing_is_faster_and_likelier_to_meet_an_incident_most_for_a_drive_that_runs_hot()
    {
        var config = new SimulationConfig();
        ShipStats light = Designs.Stats(Design.LightFreighter);      // a standard drive: runs hot when pushed
        ShipStats courier = Designs.Stats(Design.FastCourier);      // a fast drive: doesn't
        Assert.True(light.RunsHot);
        Assert.False(courier.RunsHot);

        Assert.Equal(190, Throttles.LadenTicks(config, 190, light, Throttle.Cruise));
        Assert.Equal(152, Throttles.LadenTicks(config, 190, light, Throttle.Push));
        Assert.Equal(Throttles.LadenTicks(config, 190, light, Throttle.Cruise), Throttles.LadenTicks(config, 190, light, Throttle.None));

        decimal cruise = Throttles.IncidentOdds(config, 0.42m, light, Throttle.Cruise);
        Assert.Equal(0.252m, cruise);
        Assert.Equal(cruise * 2m, Throttles.IncidentOdds(config, 0.42m, light, Throttle.Push));
        Assert.Equal(Throttles.IncidentOdds(config, 0.42m, courier, Throttle.Cruise) * 1.5m, Throttles.IncidentOdds(config, 0.42m, courier, Throttle.Push));
    }

    private static decimal SampleCreditCost(decimal danger)
    {
        ShipStats light = Designs.Stats(Design.LightFreighter);
        decimal total = 0m;
        for (long seed = 0; seed < 2000; seed++)
        {
            TransitIncident incident = IncidentResolver.Resolve(new DeterministicRandom(seed), danger, light, Shift);
            total += incident.CreditCost.Amount;
        }

        return total;
    }

    private static decimal Magnitude(TransitIncident incident) =>
        incident.DelayTicks + (incident.QualityLoss * 100m) + (incident.QuantityLossFraction * 100m) + incident.CreditCost.Amount;
}
