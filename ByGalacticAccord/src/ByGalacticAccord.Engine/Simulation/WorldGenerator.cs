using System.Collections.Generic;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Events;
using ByGalacticAccord.Engine.Random;

namespace ByGalacticAccord.Engine.Simulation;

/// <summary>Identifiers for the player's starting ship and home, returned from world generation.</summary>
public sealed record WorldHandles(SimulationContext Context, ActorId? Player, LocationId PlayerHome);

/// <summary>
/// Builds the sector (design §15): one connected graph spanning a green Accord core (Helios) and an
/// amber/red frontier (the Marches), populated with decoupled producers and consumers, AI traders,
/// a government tax sink, a station operator at every location, and — optionally — the player as an
/// Independent Pilot at Meridian Station (design §13). Everything downstream of the seed is deterministic.
/// </summary>
public static class WorldGenerator
{
    /// <summary>What every station charges for storage, per unit per day (design §3.2's example is ₵2 a week).</summary>
    public static readonly Credits StorageRate = Credits.Of(0.10m);

    /// <summary>What a station operator keeps in hand; rent above it goes on upkeep.</summary>
    public static readonly Credits StationFloat = Credits.Of(2000m);

    public static WorldHandles Generate(long seed, bool withPlayer, SimulationConfig? config = null)
    {
        var context = new SimulationContext(seed, config);
        DeterministicRandom rng = context.Rng;

        // ---- locations ----
        // Map points place the sector schematically (design §11.1): the Accord core on the left, the Marches
        // on the right. They are the UI mockup's layout, in its 860 × 540 frame.
        Location meridian = AddLocation(context, "Meridian Station", LocationType.IndustrialWorld, "Helios", AccordReach.AccordEdge, new MapPoint(120, 262));
        Location verdant = AddLocation(context, "Verdant", LocationType.AgriculturalWorld, "Helios", AccordReach.AccordEdge, new MapPoint(200, 96));
        Location heliosPrime = AddLocation(context, "Helios Prime", LocationType.CoreWorld, "Helios", AccordReach.DeepAccord, new MapPoint(282, 430));
        Location tycho = AddLocation(context, "Tycho Foundry", LocationType.IndustrialWorld, "Helios", AccordReach.AccordEdge, new MapPoint(452, 238));
        Location halcyon = AddLocation(context, "Halcyon Refinery", LocationType.RefineryStation, "Helios", AccordReach.AccordEdge, new MapPoint(520, 420));
        Location rustBelt = AddLocation(context, "Rust Belt", LocationType.MiningColony, "Marches", AccordReach.OutsideAccord, new MapPoint(684, 250));
        Location edge = AddLocation(context, "Edge Station", LocationType.FrontierOutpost, "Marches", AccordReach.OutsideAccord, new MapPoint(800, 410));

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

        // ---- part factories (design §15.1, §15.4): each makes its parts from ordinary goods, at grade A ----
        AddFactory(context, tycho, "Tycho Frameworks", rng,
            new PartRecipe(PartKind.LightFrame, new[] { new RecipeInput(GoodType.Ore, 40m), new RecipeInput(GoodType.Parts, 20m) }, Hours: 48, StockCap: 2m),
            new PartRecipe(PartKind.BulkFrame, new[] { new RecipeInput(GoodType.Ore, 160m), new RecipeInput(GoodType.Parts, 80m) }, Hours: 120, StockCap: 1m),
            new PartRecipe(PartKind.CourierFrame, new[] { new RecipeInput(GoodType.Ore, 20m), new RecipeInput(GoodType.Parts, 30m) }, Hours: 48, StockCap: 1m));
        AddFactory(context, meridian, "Meridian Drive Works", rng,
            new PartRecipe(PartKind.StandardDrive, new[] { new RecipeInput(GoodType.Machinery, 12m), new RecipeInput(GoodType.Fuel, 30m) }, Hours: 48, StockCap: 2m),
            new PartRecipe(PartKind.FastDrive, new[] { new RecipeInput(GoodType.Machinery, 24m), new RecipeInput(GoodType.Fuel, 50m) }, Hours: 96, StockCap: 1m));
        AddFactory(context, halcyon, "Halcyon Modules", rng,
            new PartRecipe(PartKind.RefrigerationUnit, new[] { new RecipeInput(GoodType.Parts, 40m), new RecipeInput(GoodType.Machinery, 20m) }, Hours: 72, StockCap: 1m),
            new PartRecipe(PartKind.ArmourPlating, new[] { new RecipeInput(GoodType.Parts, 30m), new RecipeInput(GoodType.Machinery, 20m), new RecipeInput(GoodType.Ore, 120m) }, Hours: 96, StockCap: 1m),
            new PartRecipe(PartKind.CargoPod, new[] { new RecipeInput(GoodType.Parts, 30m), new RecipeInput(GoodType.Machinery, 6m) }, Hours: 24, StockCap: 1m));

        // ---- shipyards and the salvage yard (design §15.5) ----
        // Tycho Yards: cheap and quick, lenient by habit, next door to the frame maker. Helios Orbital Yards: dearer and slower,
        // strict by habit, in the deep Accord. Each builds one ship at a time and, idle, a standard design on spec.
        AddYard(context, tycho, "Tycho Yards", rng, new YardProfile(
            AssemblyHours: 5 * 24, Labour: Credits.Of(1_200m), Margin: 0.12m, Habit: TermPackage.Lenient, LotLimit: 1,
            SpecDesigns: new[] { Design.LightFreighter }));
        AddYard(context, heliosPrime, "Helios Orbital Yards", rng, new YardProfile(
            AssemblyHours: 8 * 24, Labour: Credits.Of(1_800m), Margin: 0.25m, Habit: TermPackage.Strict, LotLimit: 1,
            SpecDesigns: new[] { Design.RefrigeratedTransport, Design.LightFreighter }));

        // Edge Salvage doesn't build: it buys ships others sell and sells them second-hand. It starts with a worn Refrigerated
        // Transport on its lot, its frame a grade down (design §13: cheap, unarmoured, a frontier sale with no escrow).
        var salvage = new ActorState(context.NextActorId(), "Edge Salvage", ActorRole.Salvage, edge.Id, RandomPersonality(rng), Credits.Of(30_000m))
        {
            Salvage = new SalvageProfile(BuyShare: 0.45m, AskShare: 0.75m, FloorShare: 0.50m, LotLimit: 3),
        };
        context.Add(salvage);
        edge.AddResident(salvage.Id);
        var worn = new Ship(
            context.NextShipId(),
            Designs.Parts(Design.RefrigeratedTransport).With(PartSlot.Frame, new FittedPart(PartKind.LightFrame, Grade.B)),
            salvage.Id, edge.Id, createdTick: 0);
        context.Add(worn);
        salvage.AddShip(worn.Id);

        // ---- government (tax sink, §4.2.3) ----
        var government = new ActorState(context.NextActorId(), "Helios Accord Authority", ActorRole.Government, heliosPrime.Id, Personality.Balanced, Credits.Of(1000m));
        context.Add(government);
        heliosPrime.AddResident(government.Id);

        // ---- AI haulers ----
        AddHauler(context, "Coreward Logistics", ActorRole.ShippingCompany, meridian.Id, Credits.Of(40000m), rng,
            (Design.LightFreighter, meridian.Id), (Design.RefrigeratedTransport, heliosPrime.Id));
        AddHauler(context, "Foundry Freightways", ActorRole.ShippingCompany, tycho.Id, Credits.Of(40000m), rng,
            (Design.BulkHauler, tycho.Id), (Design.LightFreighter, halcyon.Id));
        AddHauler(context, "Juno Okafor", ActorRole.IndependentPilot, verdant.Id, Credits.Of(9000m), rng,
            (Design.LightFreighter, verdant.Id));
        AddHauler(context, "Sable Vance", ActorRole.IndependentPilot, halcyon.Id, Credits.Of(9000m), rng,
            (Design.FastCourier, halcyon.Id));
        AddHauler(context, "Dust Runner Kael", ActorRole.IndependentPilot, rustBelt.Id, Credits.Of(11000m), rng,
            (Design.ArmouredFreighter, rustBelt.Id));
        AddHauler(context, "Pell Mirza", ActorRole.IndependentPilot, meridian.Id, Credits.Of(9000m), rng,
            (Design.LightFreighter, heliosPrime.Id));
        AddHauler(context, "Otto Reyes", ActorRole.IndependentPilot, tycho.Id, Credits.Of(9000m), rng,
            (Design.LightFreighter, tycho.Id));

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
            var ship = new Ship(context.NextShipId(), Design.LightFreighter, player.Id, meridian.Id, createdTick: 0);
            context.Add(ship);
            player.AddShip(ship.Id);
            context.PlayerActorId = player.Id;
            playerId = player.Id;
        }

        // ---- station operators: docking and storage at every location (design §8.1) ----
        foreach (Location location in new[] { meridian, verdant, heliosPrime, tycho, halcyon, rustBelt, edge })
        {
            var station = new ActorState(context.NextActorId(), location.Name + " Docks", ActorRole.StationOperator, location.Id, Personality.Balanced, StationFloat)
            {
                Station = new StationProfile(StorageRate, StationFloat),
            };
            context.Add(station);
            location.AddResident(station.Id);
        }

        // ---- bootstrap recurring events ----
        foreach (ActorState actor in context.ActorsOrdered)
        {
            long firstBeat = rng.NextLong(1, context.Config.HeartbeatInterval);
            context.Schedule(new ActorHeartbeat(firstBeat, actor.Id));
        }

        context.Schedule(new InflationCheck(context.Config.InflationCheckInterval));
        context.Schedule(new ReputationDecay(context.Config.ReputationDecayInterval));

        // Information (design §7): everyone knows the sector as it stands at the start; then the morning bulletin and The Daily
        // Accord, each day at the same hour, the bulletin first.
        Knowledge.Seed(context);
        context.Schedule(new PricesPosted(GameTime.At(0, PriceBulletin.Hour)));
        context.Schedule(new NewsPublished(GameTime.At(0, PriceBulletin.Hour)));

        // Start the faucet mid-range so the monitor settles toward equilibrium without a warm-up flood.
        context.FaucetMultiplier = 0.6m;
        context.CaptureInitialCredits();
        return new WorldHandles(context, playerId, meridian.Id);
    }

    private static Location AddLocation(SimulationContext context, string name, LocationType type, string region, AccordReach reach, MapPoint map)
    {
        var loc = new Location(context.NextLocationId(), name, type, region, reach) { Map = map };
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
        context.Add(actor);
        context.Produce(actor, good, startStock);
        loc.AddResident(actor.Id);
    }

    /// <summary>What a factory keeps above its parts' cost as its margin (design §5.2).</summary>
    public const decimal FactoryMargin = 0.25m;

    private static void AddFactory(SimulationContext context, Location loc, string name, DeterministicRandom rng, params PartRecipe[] recipes)
    {
        var profile = new FactoryProfile(Grade.A, recipes, FactoryMargin);
        var actor = new ActorState(context.NextActorId(), name, ActorRole.Factory, loc.Id, RandomPersonality(rng), context.Config.FactoryReserve)
        {
            Factory = profile,
        };
        context.Add(actor);

        // It starts with one of each part it makes, and half its inputs: counted as produced, so the goods ledger balances.
        foreach (PartRecipe recipe in recipes)
        {
            context.Produce(actor, ShipPart.GoodOf(recipe.Kind, profile.Quality), 1m);
        }

        foreach (GoodType input in profile.Inputs)
        {
            context.Produce(actor, input, System.Math.Floor(profile.InputTarget(input) * 0.5m));
        }

        loc.AddResident(actor.Id);
    }

    private static void AddYard(SimulationContext context, Location loc, string name, DeterministicRandom rng, YardProfile profile)
    {
        var actor = new ActorState(context.NextActorId(), name, ActorRole.Shipyard, loc.Id, RandomPersonality(rng), context.Config.YardReserve)
        {
            Yard = profile,
        };
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

        context.Add(actor);

        // Seed half-stocked larders so the economy doesn't cold-start into a posting stampede. Starting goods count as
        // produced, so the goods ledger balances from tick 0.
        foreach (ConsumerDemand d in list)
        {
            context.Produce(actor, d.Good, d.TargetStock * 0.5m);
        }

        loc.AddResident(actor.Id);
    }

    private static void AddHauler(
        SimulationContext context, string name, ActorRole role, LocationId home, Credits credits, DeterministicRandom rng,
        params (Design Type, LocationId At)[] ships)
    {
        var actor = new ActorState(context.NextActorId(), name, role, home, RandomPersonality(rng), credits);
        context.Add(actor);
        context.Location(home).AddResident(actor.Id);
        foreach ((Design type, LocationId at) in ships)
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
