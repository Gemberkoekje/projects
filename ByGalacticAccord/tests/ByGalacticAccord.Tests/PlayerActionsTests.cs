using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>
/// The player's way to take a haul, through the same signing steps an AI hauler uses, and each way it can
/// refuse. The interface words the codes these return.
/// </summary>
public sealed class PlayerActionsTests
{
    [Fact]
    public void The_player_signs_an_open_haul_through_the_engine_path()
    {
        (SimulationContext context, ActorState player) = PlayerWorld();
        (Contract contract, PlayerActions.HaulFeasibility check) = FirstTakeable(context, player);
        Ship ship = check.Ship!;

        HaulAcceptance result = PlayerActions.AcceptHaul(context, player, contract, ship, contract.OfferedPayment, check.RepoTicks);

        Assert.Equal(HaulAcceptance.Accepted, result);
        Assert.Equal(ContractStatus.Active, contract.Status);
        Assert.Equal(player.Id, contract.CounterpartyId);
        Assert.Equal(ship.Id, contract.AssignedShip);
        Assert.Equal(ShipStatus.InTransit, ship.Status);
        Assert.Equal(contract.Id, ship.ActiveContract);
        HaulTakenEntry taken = Assert.IsType<HaulTakenEntry>(context.Journal.Entries[^1]);
        Assert.Equal((contract.Id, player.Id, contract.OfferedPayment), (taken.Contract, taken.Hauler, taken.Fee));
        Assert.Empty(Invariants.Check(context));
    }

    [Fact]
    public void Signing_a_haul_that_is_no_longer_open_is_refused()
    {
        (SimulationContext context, ActorState player) = PlayerWorld();
        (Contract contract, PlayerActions.HaulFeasibility check) = FirstTakeable(context, player);
        Assert.Equal(HaulAcceptance.Accepted, PlayerActions.AcceptHaul(context, player, contract, check.Ship!, contract.OfferedPayment, check.RepoTicks));
        long recorded = context.Journal.TotalRecorded;
        Credits credits = player.Credits;

        HaulAcceptance again = PlayerActions.AcceptHaul(context, player, contract, check.Ship!, contract.OfferedPayment, check.RepoTicks);

        Assert.Equal(HaulAcceptance.NotOpen, again);
        Assert.Equal(recorded, context.Journal.TotalRecorded);
        Assert.Equal(credits, player.Credits);
        Assert.Equal(HaulBlock.NotOpen, PlayerActions.CheckHaul(context, player, contract).Block);
    }

    [Fact]
    public void A_busy_ship_cannot_take_a_second_haul()
    {
        (SimulationContext context, ActorState player) = PlayerWorld();
        (Contract first, PlayerActions.HaulFeasibility check) = FirstTakeable(context, player);
        PlayerActions.AcceptHaul(context, player, first, check.Ship!, first.OfferedPayment, check.RepoTicks);

        Contract? other = context.OpenContracts.FirstOrDefault(c => c.Status == ContractStatus.Open);
        Assert.NotNull(other);
        Assert.Equal(HaulBlock.NoFreeShip, PlayerActions.CheckHaul(context, player, other).Block);
    }

    [Fact]
    public void A_haul_larger_than_the_hold_is_blocked_with_the_ship_named()
    {
        (SimulationContext context, ActorState player) = PlayerWorld();
        Ship ship = context.Ship(player.Ships[0]);
        Contract big = AddHaul(context, quantity: ship.CargoCapacity + 1m);

        PlayerActions.HaulFeasibility check = PlayerActions.CheckHaul(context, player, big);

        Assert.Equal(HaulBlock.TooLarge, check.Block);
        Assert.False(check.Ok);
        Assert.Same(ship, check.Ship);
    }

    [Fact]
    public void A_haul_the_origin_cannot_supply_is_blocked()
    {
        (SimulationContext context, ActorState player) = PlayerWorld();
        Contract huge = AddHaul(context, quantity: 100m);
        ActorState producer = context.ProducerAt(huge.OriginLocationId, huge.Good)!;
        producer.RemoveStock(huge.Good, producer.Stock(huge.Good));

        Assert.Equal(HaulBlock.OriginShort, PlayerActions.CheckHaul(context, player, huge).Block);
    }

    [Fact]
    public void A_frontier_haul_the_player_cannot_front_is_refused()
    {
        (SimulationContext context, ActorState player) = PlayerWorld();
        Contract frontier = AddHaul(context, quantity: 10m, frontier: true, goodsCost: player.Credits + Credits.Of(1m));
        PlayerActions.HaulFeasibility check = PlayerActions.CheckHaul(context, player, frontier);
        Assert.True(check.Ok);

        HaulAcceptance result = PlayerActions.AcceptHaul(context, player, frontier, check.Ship!, frontier.OfferedPayment, check.RepoTicks);

        Assert.Equal(HaulAcceptance.CannotFrontGoods, result);
        Assert.Equal(ContractStatus.Open, frontier.Status);
    }

    [Fact]
    public void A_player_alert_is_the_journal_entry_and_stops_the_run()
    {
        SimulationContext context = WorldGenerator.Generate(42L, withPlayer: true).Context;
        var entry = new IncidentEntry(0, new ContractId(1), context.PlayerActorId!.Value, default);
        context.RaisePlayerAlert(entry);

        Assert.Equal(RunStop.PlayerAlert, context.RunTo(100));
        Assert.True(context.CurrentTick < 100);
        Assert.Same(entry, Assert.Single(context.PlayerAlerts));

        context.ClearPlayerAlerts();
        Assert.Equal(RunStop.ReachedTarget, context.RunTo(100));
        Assert.Equal(100, context.CurrentTick);
    }

    internal static (SimulationContext Context, ActorState Player) PlayerWorld(long seed = 42L, long ticks = 48)
    {
        WorldHandles handles = WorldGenerator.Generate(seed, withPlayer: true);
        handles.Context.RunTo(ticks);
        return (handles.Context, handles.Context.Actor(handles.Player!.Value));
    }

    internal static (Contract Contract, PlayerActions.HaulFeasibility Check) FirstTakeable(SimulationContext context, ActorState player)
    {
        foreach (Contract contract in context.OpenContracts)
        {
            PlayerActions.HaulFeasibility check = PlayerActions.CheckHaul(context, player, contract);
            if (check.Ok && contract.UsesEscrow)
            {
                return (contract, check);
            }
        }

        throw new Xunit.Sdk.XunitException("No haul the player could take.");
    }

    // A haul from Verdant's food producer to Meridian (or to Rust Belt, on the frontier), posted directly.
    private static Contract AddHaul(SimulationContext context, decimal quantity, bool frontier = false, Credits? goodsCost = null)
    {
        Location verdant = context.Locations.Values.Single(l => l.Name == "Verdant");
        Location to = context.Locations.Values.Single(l => l.Name == (frontier ? "Rust Belt" : "Meridian Station"));
        ActorState poster = context.Actor(to.Residents.First(id => context.Actor(id).Demands.Count > 0));
        var contract = new Contract(
            context.NextContractId(), ContractType.Haul, poster.Id, GoodType.Food, quantity, verdant.Id, to.Id,
            Credits.Of(100m), Credits.Of(200m), goodsCost ?? Credits.Of(quantity * 6m), 0.04m, context.CurrentTick + 200,
            to.Reach, to.Region, new ContractClause[] { new PenaltyOnLateDelivery(1m, Credits.Of(60m)) });
        context.Add(contract);
        return contract;
    }
}
