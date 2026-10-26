using System.Collections.Generic;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>
/// A ship's course is what the client draws it along (design §19: the client interpolates, the engine
/// never does), so it must agree with the events that actually move the ship.
/// </summary>
public sealed class ShipCourseTests
{
    [Theory]
    [InlineData(42L)]
    [InlineData(20260619L)]
    public void A_course_matches_the_ship_and_its_contract_at_every_hour(long seed)
    {
        SimulationContext context = WorldGenerator.Generate(seed, withPlayer: false).Context;
        int laden = 0;
        int empty = 0;
        for (long t = 1; t <= 3_000; t++)
        {
            context.RunTo(t);
            foreach (Ship ship in context.Ships.Values)
            {
                if (ship.Status == ShipStatus.Docked)
                {
                    Assert.Null(ship.Course);
                    continue;
                }

                Contract contract = context.Contract(ship.ActiveContract!.Value);
                if (ship.Manifest is not null)
                {
                    // Laden: from the pickup, where the ship now is, to the contract's destination.
                    ShipCourse course = Assert.IsType<ShipCourse>(ship.Course);
                    Assert.Equal(contract.OriginLocationId, course.From);
                    Assert.Equal(contract.OriginLocationId, ship.Location);
                    Assert.Equal(contract.DestinationLocationId, course.To);
                    Assert.Equal(ship.DepartedTick, course.DepartedTick);
                    Assert.True(course.ArrivalTick >= t);
                    laden++;
                }
                else if (ship.Location == contract.OriginLocationId)
                {
                    Assert.Null(ship.Course); // signed at the pickup; loads when its departure event runs
                }
                else
                {
                    // Repositioning: the empty leg to the pickup.
                    ShipCourse course = Assert.IsType<ShipCourse>(ship.Course);
                    Assert.Equal(ship.Location, course.From);
                    Assert.Equal(contract.OriginLocationId, course.To);
                    Assert.True(course.ArrivalTick >= t);
                    empty++;
                }
            }
        }

        Assert.True(laden > 0);
        Assert.True(empty > 0);
    }

    [Theory]
    [InlineData(42L)]
    [InlineData(1L)]
    public void A_haul_settles_at_its_courses_arrival_even_after_a_delay(long seed)
    {
        SimulationContext context = WorldGenerator.Generate(seed, withPlayer: false).Context;
        var dueAt = new Dictionary<ContractId, long>();
        var settled = new HashSet<ContractId>();
        int checkedArrivals = 0;
        for (long t = 1; t <= 8_000; t++)
        {
            context.RunTo(t);
            foreach (Ship ship in context.Ships.Values)
            {
                if (ship.Manifest is not null && ship.Course is { } course && ship.ActiveContract is { } id)
                {
                    dueAt[id] = course.ArrivalTick;
                }
            }

            foreach ((ContractId id, long due) in dueAt)
            {
                ContractStatus status = context.Contract(id).Status;
                if (status is ContractStatus.Fulfilled or ContractStatus.Breached && settled.Add(id))
                {
                    Assert.Equal(due, t);
                    checkedArrivals++;
                }
            }
        }

        Assert.True(context.IncidentsOccurred > 0, "The run should include incidents, some of them delays.");
        Assert.True(checkedArrivals > 100);
    }

    [Fact]
    public void A_delay_pushes_back_the_arrival_and_nothing_else()
    {
        var path = new[] { new LocationId(1), new LocationId(2), new LocationId(3) };
        var ship = new Ship(new ShipId(1), ShipType.LightFreighter, new ActorId(1), path[0], createdTick: 0);
        ship.Reserve(0, new ContractId(1), reposition: null);
        ship.Depart(0, path[0], Cargo.Fresh(GoodType.Ore, 10m), new ContractId(1));
        ship.SetCourse(new ShipCourse(path, 0, 70));

        ship.DelayArrival(12);
        Assert.Equal(new ShipCourse(path, 0, 82), ship.Course);
        Assert.Equal(path[0], ship.Course!.From);
        Assert.Equal(path[2], ship.Course.To);

        ship.Arrive(82, path[2]);
        Assert.Null(ship.Course);
        Assert.Equal(path[2], ship.Location);
    }
}
