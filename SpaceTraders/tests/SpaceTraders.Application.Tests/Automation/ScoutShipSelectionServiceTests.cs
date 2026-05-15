using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Tests.Automation;

public sealed class ScoutShipSelectionServiceTests
{
    [Fact]
    public async Task SelectAsync_ReturnsOnlyShipWithFuel()
    {
        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ShipModel("SHIP-NO-FUEL", "X1-SYS", "X1-SYS-START", "DOCKED", "CRUISE", 0, 100),
            new ShipModel("SHIP-SCOUT", "X1-SYS", "X1-SYS-START", "DOCKED", "CRUISE", 80, 100),
        ]);

        var service = new ScoutShipSelectionService(ships, NullLogger<ScoutShipSelectionService>.Instance);

        var selected = await service.SelectAsync(CancellationToken.None);

        selected.Symbol.Should().Be("SHIP-SCOUT");
    }

    [Fact]
    public async Task SelectAsync_Throws_WhenNoShipHasFuel()
    {
        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ShipModel("SHIP-1", "X1-SYS", "X1-SYS-START", "DOCKED", "CRUISE", 0, 100),
            new ShipModel("SHIP-2", "X1-SYS", "X1-SYS-START", "DOCKED", "CRUISE", 0, 0),
        ]);

        var service = new ScoutShipSelectionService(ships, NullLogger<ScoutShipSelectionService>.Instance);

        var act = () => service.SelectAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*found 0 candidates*");
    }

    [Fact]
    public async Task SelectAsync_Throws_WhenMultipleShipsHaveFuel()
    {
        var ships = Substitute.For<IShipRepository>();
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ShipModel("SHIP-B", "X1-SYS", "X1-SYS-START", "DOCKED", "CRUISE", 50, 100),
            new ShipModel("SHIP-A", "X1-SYS", "X1-SYS-START", "DOCKED", "CRUISE", 80, 100),
        ]);

        var service = new ScoutShipSelectionService(ships, NullLogger<ScoutShipSelectionService>.Instance);

        var act = () => service.SelectAsync(CancellationToken.None);

        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.Which.Message.Should().Contain("found 2 candidates");
        ex.Which.Message.Should().Contain("SHIP-A");
        ex.Which.Message.Should().Contain("SHIP-B");
    }
}
