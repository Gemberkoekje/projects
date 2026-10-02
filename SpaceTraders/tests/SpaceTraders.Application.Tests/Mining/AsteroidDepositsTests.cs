using FluentAssertions;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;

namespace SpaceTraders.Application.Tests.Mining;

/// <summary>Slice 6.4: which asteroid yields which ore, from the traits startup sync now stores (B34).</summary>
public sealed class AsteroidDepositsTests
{
    [Fact]
    public void CommonMetalDeposits_YieldWhatXB5CYieldedOnTheCluster()
    {
        // 2026-10-02: aluminum, copper and iron ore, ice water, quartz sand and silicon crystals.
        var xb5c = Waypoint("ENGINEERED_ASTEROID", """[{"symbol":"COMMON_METAL_DEPOSITS"},{"symbol":"MARKETPLACE"}]""");

        AsteroidDeposits.OresAt(xb5c).Should().BeEquivalentTo(
            "ALUMINUM_ORE", "COPPER_ORE", "IRON_ORE", "ICE_WATER", "QUARTZ_SAND", "SILICON_CRYSTALS");
        AsteroidDeposits.CanYield(xb5c, "copper_ore").Should().BeTrue();
        AsteroidDeposits.CanYield(xb5c, "GOLD_ORE").Should().BeFalse();
    }

    [Theory]
    [InlineData("ASTEROID", true)]
    [InlineData("ASTEROID_FIELD", true)]
    [InlineData("ENGINEERED_ASTEROID", true)]
    [InlineData("ASTEROID_BASE", false)]
    [InlineData("PLANET", false)]
    public void OnlyAsteroids_CanBeMined(string type, bool extractable)
    {
        AsteroidDeposits.IsExtractable(type).Should().Be(extractable);
        AsteroidDeposits.OresAt(Waypoint(type, """[{"symbol":"PRECIOUS_METAL_DEPOSITS"}]""")).Should().HaveCount(extractable ? 8 : 0);
    }

    [Fact]
    public void AnAsteroidWhoseTraitsAreUnknown_YieldsNothingWeKnowOf()
    {
        // A waypoint cached before B34's fix: nothing tells what it yields.
        AsteroidDeposits.OresAt(Waypoint("ASTEROID", null)).Should().BeEmpty();
        AsteroidDeposits.OresAt(Waypoint("ASTEROID", "not json")).Should().BeEmpty();
    }

    [Fact]
    public void Traits_AreReadInEitherCase()
    {
        AsteroidDeposits.TraitSymbols("""[{"Symbol":"FROZEN"},{"symbol":"STRIPPED"}]""").Should().Equal("FROZEN", "STRIPPED");
    }

    private static WaypointCacheModel Waypoint(string type, string? traits)
        => new("X1-AB-B1", "X1-AB", type, 0, 0, false, false, DateTimeOffset.UnixEpoch, TraitsJson: traits);
}
