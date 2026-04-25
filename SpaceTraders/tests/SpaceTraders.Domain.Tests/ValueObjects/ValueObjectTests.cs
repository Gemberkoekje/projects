using FluentAssertions;
using SpaceTraders.Domain.ValueObjects;

namespace SpaceTraders.Domain.Tests.ValueObjects;

public sealed class WaypointSymbolTests
{
    [Fact]
    public void Constructor_NormalizesToUppercase()
    {
        var symbol = new WaypointSymbol("x1-ab-001");

        symbol.Value.Should().Be("X1-AB-001");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_NullOrWhitespace_Throws(string? value)
    {
        var act = () => new WaypointSymbol(value!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EqualSymbols_AreEqual()
    {
        new WaypointSymbol("X1-AB-001").Should().Be(new WaypointSymbol("x1-ab-001"));
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        new WaypointSymbol("X1-AB-001").ToString().Should().Be("X1-AB-001");
    }
}

public sealed class FuelTests
{
    [Fact]
    public void Percentage_CalculatedCorrectly()
    {
        var fuel = new Fuel(50, 100);

        fuel.Percentage.Should().BeApproximately(0.5, 0.001);
    }

    [Fact]
    public void Percentage_ZeroCapacity_ReturnsZero()
    {
        var fuel = new Fuel(0, 0);

        fuel.Percentage.Should().Be(0);
    }

    [Fact]
    public void Constructor_NegativeCurrent_Throws()
    {
        var act = () => new Fuel(-1, 100);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_NegativeCapacity_Throws()
    {
        var act = () => new Fuel(0, -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}

public sealed class CargoItemTests
{
    [Fact]
    public void Constructor_NegativeUnits_Throws()
    {
        var act = () => new CargoItem(new TradeSymbol("IRON_ORE"), -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EqualItems_AreEqual()
    {
        var a = new CargoItem(new TradeSymbol("IRON_ORE"), 10);
        var b = new CargoItem(new TradeSymbol("IRON_ORE"), 10);

        a.Should().Be(b);
    }
}
