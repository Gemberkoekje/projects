using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Naming;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Tests.Naming;

/// <summary>Slice 2.14 (D72): the names, kept for what can't read the fleet itself, such as the log lines.</summary>
public sealed class ShipNameBookTests
{
    private readonly IActiveReset _reset = Substitute.For<IActiveReset>();

    public ShipNameBookTests() => _reset.ResetDate.Returns("2026-10-04");

    [Fact]
    public void AShipItWasToldOf_ItNames_AsTheFleetsNamesHaveIt()
    {
        var book = new ShipNameBook(_reset);
        IReadOnlyList<ShipModel> fleet = [Probe("SPECTER-2"), Probe("SPECTER-4")];

        var names = book.Know(fleet);

        names.Should().Equal(ShipNames.For(fleet, "2026-10-04"));
        book.NameOf("SPECTER-4").Should().Be(names["SPECTER-4"]).And.EndWith("-2");
        book.NameOf("SPECTER-9").Should().BeEmpty("nobody told it of that ship");
    }

    [Fact]
    public void BeforeTheAgentIsKnown_ItNamesNothing()
    {
        _reset.ResetDate.Returns(string.Empty);
        var book = new ShipNameBook(_reset);

        book.Know([Probe("SPECTER-2")]).Should().BeEmpty();
        book.NameOf("SPECTER-2").Should().BeEmpty();
    }

    [Fact]
    public void AFleetReadBeforeAPurchase_ToldAfterIt_KeepsTheNewShipsName()
    {
        // The metrics read the fleet every 10 seconds; a purchase may tell the book of its ship in between.
        var book = new ShipNameBook(_reset);
        book.Know([Probe("SPECTER-2"), Probe("SPECTER-5")]);

        book.Know([Probe("SPECTER-2")]);

        book.NameOf("SPECTER-5").Should().EndWith("-2");
    }

    private static ShipModel Probe(string symbol) => new(symbol, "X1-FJ91", "X1-FJ91-A1", "DOCKED", "CRUISE", 0, 0, ShipType: "SHIP_PROBE");
}
