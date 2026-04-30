using FluentAssertions;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Domain.Tests.Enums;

public sealed class ShipLocalStatusMapperTests
{
    [Theory]
    [InlineData("DOCKED", ShipLocalStatus.Docked)]
    [InlineData("docked", ShipLocalStatus.Docked)]
    [InlineData("IN_ORBIT", ShipLocalStatus.InOrbit)]
    [InlineData("in_orbit", ShipLocalStatus.InOrbit)]
    [InlineData("IN_TRANSIT", ShipLocalStatus.InTransit)]
    [InlineData("in_transit", ShipLocalStatus.InTransit)]
    public void FromApiStatus_KnownStatus_MapsCorrectly(string apiStatus, ShipLocalStatus expected)
    {
        ShipLocalStatusMapper.FromApiStatus(apiStatus).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("UNKNOWN")]
    [InlineData("FLYING")]
    public void FromApiStatus_UnknownOrNullStatus_ReturnsNone(string? apiStatus)
    {
        ShipLocalStatusMapper.FromApiStatus(apiStatus).Should().Be(ShipLocalStatus.None);
    }
}
