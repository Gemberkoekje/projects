using FluentAssertions;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Roles;

namespace SpaceTraders.Application.Tests.Probes;

/// <summary>
/// Slice 6.38 (D119), asked on 2026-10-09: "When available, I'd like INTERCEPTORS to be used instead of PROBES." An interceptor is
/// a probe to the plans: the probe plan flies it, and no other plan gives it work, whatever it carries.
/// </summary>
public sealed class InterceptorTests
{
    [Theory]
    [InlineData("SHIP_INTERCEPTOR", null)]
    [InlineData("INTERCEPTOR", null)]
    [InlineData("PATROL", """{"symbol":"FRAME_INTERCEPTOR","fuelCapacity":100}""")]
    public void AnInterceptor_AsBought_AsStartupSyncStoresIt_OrByItsFrame_IsAProbe(string shipType, string? frame)
    {
        var ship = Interceptor() with { ShipType = shipType, FrameJson = frame };

        FleetRoles.IsInterceptor(ship).Should().BeTrue();
        FleetRoles.IsProbe(ship).Should().BeTrue();
    }

    [Fact]
    public void AnInterceptor_HasNoRoleOnTheBoard_ThoughItHadAHold()
    {
        // A probe has none: the probe plan flies it (D29).
        var withAHold = Interceptor() with { CargoCapacity = 10 };

        FleetRoles.PotentialRoles(withAHold).Should().BeEmpty();
        FleetRoles.CanConstruct(withAHold).Should().BeFalse();
    }

    [Fact]
    public void WithTheBoardOff_AnInterceptorIsNoTrader_ThoughItHadAHold()
    {
        // The fixed rules give a route to any ship with a hold and a tank that doesn't survey; the probe plan flies this one.
        var board = FleetRoleBoard.For(rolesOn: false, surveyOn: true, spareTimeOn: true);

        board.IsTrader(Interceptor() with { CargoCapacity = 10 }).Should().BeFalse();
    }

    [Fact]
    public void ACargoShip_IsNoInterceptor()
    {
        var hauler = new ShipModel("SHIP-6", "X1-AB", "X1-AB-A1", "DOCKED", "CRUISE", 600, 600, CargoCapacity: 80, ShipType: "SHIP_LIGHT_HAULER");

        FleetRoles.IsInterceptor(hauler).Should().BeFalse();
        FleetRoles.IsProbe(hauler).Should().BeFalse();
    }

    private static ShipModel Interceptor()
        => new("SHIP-9", "X1-AB", "X1-AB-A1", "DOCKED", "CRUISE", 100, 100, ShipType: "SHIP_INTERCEPTOR");
}
