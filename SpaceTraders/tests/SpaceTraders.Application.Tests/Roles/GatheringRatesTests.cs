using FluentAssertions;
using SpaceTraders.Application.Roles;

namespace SpaceTraders.Application.Tests.Roles;

/// <summary>Slice 6.9: how fast a ship fills its hold, as its extractions and siphons showed.</summary>
public sealed class GatheringRatesTests
{
    private readonly GatheringRates _rates = new();

    [Fact]
    public void BeforeAnyExtraction_TheDefaultStandsIn()
    {
        _rates.For("SHIP-3", GatheringKind.Mining).Should().Be(GatheringRates.Default);
    }

    [Fact]
    public void AShipsOwnExtractions_GiveItsRate()
    {
        _rates.Record("SHIP-3", GatheringKind.Mining, 4, 70);
        _rates.Record("SHIP-3", GatheringKind.Mining, 6, 80);

        _rates.For("ship-3", GatheringKind.Mining).Should().Be(new GatheringRate(5, 75, Observed: true));
    }

    [Fact]
    public void AShipWithoutExtractions_TakesTheOtherShipsOfTheKind_NotTheOtherKind()
    {
        _rates.Record("SHIP-3", GatheringKind.Mining, 4, 70);
        _rates.Record("SHIP-4", GatheringKind.Mining, 2, 90);
        _rates.Record("SHIP-6", GatheringKind.Siphoning, 9, 60);

        _rates.For("SHIP-5", GatheringKind.Mining).Should().Be(new GatheringRate(3, 80, Observed: true));
    }

    [Fact]
    public void OnlyTheLatestExtractions_Count()
    {
        _rates.Record("SHIP-3", GatheringKind.Mining, 100, 10);
        for (var extraction = 0; extraction < GatheringRates.Window; extraction++)
        {
            _rates.Record("SHIP-3", GatheringKind.Mining, 3, 70);
        }

        _rates.For("SHIP-3", GatheringKind.Mining).Should().Be(new GatheringRate(3, 70, Observed: true));
    }

    [Fact]
    public void AnExtractionWithoutACooldown_CountsItsYieldButNotItsTime()
    {
        _rates.Record("SHIP-3", GatheringKind.Mining, 4, 0);

        _rates.For("SHIP-3", GatheringKind.Mining).Should().Be(new GatheringRate(4, GatheringRates.Default.SecondsPerAction, Observed: true));
    }

    [Fact]
    public void AKeptRate_StandsInAfterARestart_UntilTheShipExtracts()
    {
        _rates.Seed("SHIP-3", GatheringKind.Mining, new GatheringRate(5, 75, Observed: true));
        _rates.Seed("SHIP-4", GatheringKind.Mining, GatheringRates.Default);
        _rates.For("SHIP-3", GatheringKind.Mining).Should().Be(new GatheringRate(5, 75, Observed: true));

        // A kept default isn't an observation; and an extraction since the restart isn't overwritten by a seed.
        _rates.Record("SHIP-6", GatheringKind.Siphoning, 9, 60);
        _rates.Seed("SHIP-6", GatheringKind.Siphoning, new GatheringRate(1, 10, Observed: true));
        _rates.For("SHIP-6", GatheringKind.Siphoning).Should().Be(new GatheringRate(9, 60, Observed: true));
        _rates.For("SHIP-5", GatheringKind.Mining).Should().Be(new GatheringRate(5, 75, Observed: true));
    }
}
