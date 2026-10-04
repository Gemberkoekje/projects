using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Naming;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Queries;

namespace SpaceTraders.Application.Tests.Naming;

/// <summary>Slice 2.14 (D72): the WebUI's ship list shows each ship's name beside its symbol.</summary>
public sealed class ShipListNamesTests
{
    [Fact]
    public async Task TheShipList_NamesEachShip_AndTellsTheNameBook()
    {
        var reset = Substitute.For<IActiveReset>();
        reset.ResetDate.Returns("2026-10-04");
        var book = new ShipNameBook(reset);
        var ships = Substitute.For<IShipRepository>();
        IReadOnlyList<ShipModel> fleet =
        [
            new("SPECTER-2", "X1-FJ91", "X1-FJ91-A1", "DOCKED", "CRUISE", 0, 0, ShipType: "SATELLITE", FrameJson: """{"symbol":"FRAME_PROBE"}"""),
            new("SPECTER-5", "X1-FJ91", "X1-FJ91-A1", "DOCKED", "CRUISE", 0, 0, ShipType: "SHIP_PROBE"),
        ];
        ships.GetAllAsync(Arg.Any<CancellationToken>()).Returns(fleet);

        var list = await new GetAllShipsQueryHandler(ships, book).Handle(new GetAllShipsQuery(), CancellationToken.None);

        var names = ShipNames.For(fleet, "2026-10-04");
        list.Select(ship => (ship.Symbol, ship.Name)).Should().Equal(("SPECTER-2", names["SPECTER-2"]), ("SPECTER-5", names["SPECTER-5"]));
        book.NameOf("SPECTER-5").Should().Be(names["SPECTER-5"]);
    }
}
