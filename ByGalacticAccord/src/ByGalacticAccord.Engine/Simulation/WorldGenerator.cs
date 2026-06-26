using System.Collections.Generic;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Events;
using ByGalacticAccord.Engine.Random;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>Identifiers for the player's starting ship and home, returned from world generation.</summary>
public sealed record WorldHandles(SimulationContext Context, ActorId? Player, LocationId PlayerHome);

/// <summary>
/// Builds the slice-0 sector: one connected graph spanning a green Accord core (Helios) and an
/// amber/red frontier (the Marches), populated with decoupled producers and consumers, AI haulers,
/// a government tax sink, and — optionally — the player as an Independent Pilot at a mid-tier colony
/// (§3.3, §4.7.1). Everything downstream of the seed is deterministic.
/// </summary>
public static class WorldGenerator
{
    public static WorldHandles Generate(long seed, bool withPlayer, SimulationConfig? config = null)
    {
        var context = new SimulationContext(seed, config);
        DeterministicRandom rng = context.Rng;

        // ---- locations ----
        Location meridian = AddLocation(context, "Meridian Station", LocationType.IndustrialWorld, "Helios", AccordReach.AccordEdge);
        Location verdant = AddLocation(context, "Verdant", LocationType.AgriculturalWorld, "Helios", AccordReach.AccordEdge);
        Location heliosPrime = AddLocation(context, "Helios Prime", LocationType.CoreWorld, "Helios", AccordReach.DeepAccord);
        Location tycho = AddLocation(context, "Tycho Foundry", LocationType.IndustrialWorld, "Helios", AccordReach.AccordEdge);
        Location halcyon = AddLocation(context, "Halcyon Refinery", LocationType.RefineryStation, "Helios", AccordReach.AccordEdge);
        Location rustBelt = AddLocation(context, "Rust Belt", LocationType.MiningColony, "Marches", AccordReach.OutsideAccord);
        Location edge = AddLocation(context, "Edge Station", LocationType.FrontierOutpost, "Marches", AccordReach.OutsideAccord);

        // ---- routes (undirected). Danger climbs as you head out of the Accord. ----
        AddRoute(context, meridian, verdant, 30, 0.05m, AccordReach.AccordEdge);
        AddRoute(context, meridian, heliosPrime, 50, 0.04m, AccordReach.DeepAccord);
        AddRoute(context, meridian, tycho, 40, 0.08m, AccordReach.AccordEdge);
        AddRoute(context, verdant, heliosPrime, 45, 0.06m, AccordReach.DeepAccord);
        AddRoute(context, heliosPrime, halcyon, 40, 0.09m, AccordReach.AccordEdge);
        AddRoute(context, tycho, halcyon, 35, 0.10m, AccordReach.AccordEdge);
        AddRoute(context, tycho, rustBelt, 70, 0.28m, AccordReach.OutsideAccord);
        AddRoute(context, halcyon, rustBelt, 60, 0.22m, AccordReach.OutsideAccord);
        AddRoute(context, rustBelt, edge, 80, 0.42m, AccordReach.OutsideAccord);

        // ---- producers (Conjurers) ----
        AddProducer(context, verdant, "Verdant Agricombine", GoodType.Food, output: 26m, unitCost: 5m, cap: 400m, margin: 0.25m, startStock: 180m, rng);
        AddProducer(context, meridian, "Meridian Fabworks", GoodType.Parts, output: 12m, unitCost: 16m, cap: 200m, margin: 0.30m, startStock: 80m, rng);
        AddProducer(context, tycho, "Tycho Heavy Industries", GoodType.Machinery, output: 6m, unitCost: 34m, cap: 120m, margin: 0.30m, startStock: 40m, rng);
        AddProducer(context, halcyon, "Halcyon Fuelworks", GoodType.Fuel, output: 18m, unitCost: 8m, cap: 320m, margin: 0.25m, startStock: 140m, rng);
        AddProducer(context, rustBelt, "Rust Belt Extraction", GoodType.Ore, output: 30m, unitCost: 4m, cap: 500m, margin: 0.20m, startStock: 220m, rng);
        AddProducer(context, rustBelt, "Marches Volatiles", GoodType.Fuel, output: 12m, unitCost: 7m, cap: 260m, margin: 0.25m, startStock: 90m, rng);
        AddProducer(context, heliosPrime, "Prime Couturiers", GoodType.Luxury, output: 3m, unitCost: 70m, cap: 80m, margin: 0.40m, startStock: 30m, rng);
        AddProducer(context, heliosPrime, "Helios Biolabs", GoodType.Medicine, output: 2.5m, unitCost: 45m, cap: 70m, margin: 0.40m, startStock: 28m, rng);

        // ---- consumers (Eaters) with income faucets ----
        // Income is set near each consumer's restock spend so the faucet roughly nets against the sinks
        // (production/running/tax) and the inflation monitor sits mid-range (§4.2.1). Consumers carry a
        // deep cash buffer so monitor throttling never starves their restocking (avoids §7.7 collapse).
        AddConsumer(context, meridian, "Meridian Habs", income: 150m, rng,
            (GoodType.Food, 8m, 130m), (GoodType.Fuel, 5m, 90m));
        AddConsumer(context, verdant, "Verdant Township", income: 130m, rng,
            (GoodType.Parts, 3m, 55m), (GoodType.Machinery, 1m, 22m));
        AddConsumer(context, heliosPrime, "Helios Prime Metropole", income: 430m, rng,
            (GoodType.Food, 6m, 100m), (GoodType.Machinery, 2m, 45m), (GoodType.Medicine, 2m, 45m), (GoodType.Luxury, 1m, 28m));
        AddConsumer(context, tycho, "Tycho Workforce", income: 165m, rng,
            (GoodType.Ore, 10m, 160m), (GoodType.Fuel, 6m, 95m));
        AddConsumer(context, rustBelt, "Rust Belt Camp", income: 120m, rng,
            (GoodType.Food, 7m, 95m), (GoodType.Parts, 2m, 42m));
        AddConsumer(context, edge, "Edge Station Holdfast", income: 190m, rng,
            (GoodType.Food, 4m, 55m), (GoodType.Medicine, 1.5m, 22m), (GoodType.Machinery, 0.8m, 16m));
        AddConsumer(context, halcyon, "Halcyon Crew", income: 50m, rng,
            (GoodType.Ore, 6m, 105m));

        // ---- government (tax sink, §4.2.3) ----
        var government = new ActorState(context.NextActorId(), "Helios Accord Authority", ActorRole.Government, heliosPrime.Id, Personality.Balanced, Credits.Of(1000m));
        context.Add(government);
        heliosPrime.AddResident(government.Id);

        // ---- AI haulers ----
        AddHauler(context, "Coreward Logistics", ActorRole.ShippingCompany, meridian.Id, Credits.Of(40000m), rng,
            (ShipType.LightFreighter, meridian.Id), (ShipType.RefrigeratedTransport, heliosPrime.Id));
        AddHauler(context, "Foundry Freightways", ActorRole.ShippingCompany, tycho.Id, Credits.Of(40000m), rng,
            (ShipType.BulkHauler, tycho.Id), (ShipType.LightFreighter, halcyon.Id));
        AddHauler(context, "Juno Okafor", ActorRole.IndependentPilot, verdant.Id, Credits.Of(9000m), rng,
            (ShipType.LightFreighter, verdant.Id));
        AddHauler(context, "Sable Vance", ActorRole.IndependentPilot, halcyon.Id, Credits.Of(9000m), rng,
            (ShipType.FastCourier, halcyon.Id));
        AddHauler(context, "Dust Runner Kael", ActorRole.IndependentPilot, rustBelt.Id, Credits.Of(11000m), rng,
            (ShipType.ArmedFreighter, rustBelt.Id));
        AddHauler(context, "Pell Mirza", ActorRole.IndependentPilot, meridian.Id, Credits.Of(9000m), rng,
            (ShipType.LightFreighter, heliosPrime.Id));
        AddHauler(context, "Otto Reyes", ActorRole.IndependentPilot, tycho.Id, Credits.Of(9000m), rng,
            (ShipType.LightFreighter, tycho.Id));

        // ---- the player ----
        ActorId? playerId = null;
        if (withPlayer)
        {
            var player = new ActorState(context.NextActorId(), "You", ActorRole.IndependentPilot, meridian.Id, Personality.Balanced, Credits.Of(3000m))
            {
                IsPlayer = true,
            };
            context.Add(player);
            meridian.AddResident(player.Id);
            var ship = new Ship(context.NextShipId(), ShipType.LightFreighter, player.Id, meridian.Id, createdTick: 0);
            context.Add(ship);
            player.AddShip(ship.Id);
            context.PlayerActorId = player.Id;
            playerId = player.Id;
        }

        // ---- bootstrap recurring events ----
        foreach (ActorState actor in context.ActorsOrdered)
        {
            long firstBeat = rng.NextLong(1, context.Config.HeartbeatInterval);
            context.Schedule(new ActorHeartbeat(firstBeat, actor.Id));
        }

        context.Schedule(new InflationCheck(context.Config.InflationCheckInterval));
        context.Schedule(new ReputationDecay(context.Config.ReputationDecayInterval));

        // Start the faucet mid-range so the monitor settles toward equilibrium without a warm-up flood.
        context.FaucetMultiplier = 0.6m;
        context.CaptureInitialCredits();
        return new WorldHandles(context, playerId, meridian.Id);
    }

    private static Location AddLocation(SimulationContext context, string name, LocationType type, string region, AccordReach reach)
    {
        var loc = new Location(context.NextLocationId(), name, type, region, reach);
        context.Add(loc);
        return loc;
    }

    private static void AddRoute(SimulationContext context, Location a, Location b, long distance, decimal danger, AccordReach law) =>
        context.Add(new Route(a.Id, b.Id, distance, danger, law));

    private static void AddProducer(
        SimulationContext context, Location loc, string name, GoodType good,
        decimal output, decimal unitCost, decimal cap, decimal margin, decimal startStock, DeterministicRandom rng)
    {
        var actor = new ActorState(context.NextActorId(), name, ActorRole.Producer, loc.Id, RandomPersonality(rng), Credits.Of(4000m))
        {
            Producer = new ProducerProfile(good, output, Credits.Of(unitCost), cap, margin),
        };
        actor.AddStock(good, startStock);
        context.Add(actor);
        loc.AddResident(actor.Id);
    }

    private static void AddConsumer(
        SimulationContext context, Location loc, string name, decimal income, DeterministicRandom rng,
        params (GoodType Good, decimal Rate, decimal Target)[] demands)
    {
        var list = new List<ConsumerDemand>();
        foreach ((GoodType good, decimal rate, decimal target) in demands)
        {
            list.Add(new ConsumerDemand(good, rate, target));
        }

        var actor = new ActorState(context.NextActorId(), name, ActorRole.Consumer, loc.Id, RandomPersonality(rng), Credits.Of(income * 60m))
        {
            Demands = list,
            IncomePerHeartbeat = Credits.Of(income),
        };

        // Seed half-stocked larders so the economy doesn't cold-start into a posting stampede.
        foreach (ConsumerDemand d in list)
        {
            actor.AddStock(d.Good, d.TargetStock * 0.5m);
        }

        context.Add(actor);
        loc.AddResident(actor.Id);
    }

    private static void AddHauler(
        SimulationContext context, string name, ActorRole role, LocationId home, Credits credits, DeterministicRandom rng,
        params (ShipType Type, LocationId At)[] ships)
    {
        var actor = new ActorState(context.NextActorId(), name, role, home, RandomPersonality(rng), credits);
        context.Add(actor);
        context.Location(home).AddResident(actor.Id);
        foreach ((ShipType type, LocationId at) in ships)
        {
            var ship = new Ship(context.NextShipId(), type, actor.Id, at, createdTick: 0);
            context.Add(ship);
            actor.AddShip(ship.Id);
        }
    }

    private static Personality RandomPersonality(DeterministicRandom rng) => new Personality(
        RiskTolerance: rng.NextDecimal(0.2m, 0.9m),
        LiquidityPreference: rng.NextDecimal(0.2m, 0.8m),
        QualityBias: rng.NextDecimal(0.2m, 0.8m),
        GrowthAmbition: rng.NextDecimal(0.2m, 0.9m),
        TrustThreshold: rng.NextDecimal(0.1m, 0.7m),
        ConcessionRate: rng.NextDecimal(0.2m, 0.7m)).Clamped();
}
