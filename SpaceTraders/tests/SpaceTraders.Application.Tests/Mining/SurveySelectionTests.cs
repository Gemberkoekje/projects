using FluentAssertions;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Tests.Mining;

/// <summary>Slice 6.4: a miner extracts with the survey where its ore makes up most of the deposits.</summary>
public sealed class SurveySelectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 02, 14, 00, 00, TimeSpan.Zero);

    [Fact]
    public void TheLargestShareOfTheOre_Wins_OverALargerDeposit()
    {
        var large = Survey("S-LARGE", "LARGE", "COPPER_ORE", "IRON_ORE", "IRON_ORE", "QUARTZ_SAND");
        var small = Survey("S-SMALL", "SMALL", "COPPER_ORE", "COPPER_ORE", "IRON_ORE");

        SurveySelection.TryPickBest([large, small], "X1-AB-XB5C", "COPPER_ORE", Now, out var best).Should().BeTrue();

        best.Signature.Should().Be("S-SMALL");
        SurveySelection.Share(best, "COPPER_ORE").Should().BeApproximately(2 / 3.0, 1e-9);
    }

    [Fact]
    public void AnEqualShare_GoesToTheLargerDeposit_ThenTheLaterExpiry()
    {
        var moderate = Survey("S-1", "MODERATE", "COPPER_ORE", "IRON_ORE");
        var large = Survey("S-2", "LARGE", "COPPER_ORE", "IRON_ORE");
        var largeLater = Survey("S-3", "LARGE", "COPPER_ORE", "IRON_ORE") with { Expiration = Now.AddHours(2) };

        SurveySelection.TryPickBest([moderate, large, largeLater], "X1-AB-XB5C", "COPPER_ORE", Now, out var best);

        best.Signature.Should().Be("S-3");
    }

    [Fact]
    public void ExpiredSurveys_SurveysElsewhere_AndSurveysWithoutTheOre_AreNotPicked()
    {
        var expired = Survey("S-OLD", "LARGE", "COPPER_ORE") with { Expiration = Now.AddSeconds(-1) };
        var elsewhere = Survey("S-B7", "LARGE", "COPPER_ORE") with { WaypointSymbol = "X1-AB-B14" };
        var withoutCopper = Survey("S-ICE", "LARGE", "ICE_WATER");

        SurveySelection.TryPickBest([expired, elsewhere, withoutCopper], "X1-AB-XB5C", "COPPER_ORE", Now, out _).Should().BeFalse();
        SurveySelection.HasUsable([withoutCopper], "X1-AB-XB5C", "ICE_WATER", Now).Should().BeTrue();
    }

    private static SurveyModel Survey(string signature, string size, params string[] deposits)
        => new(signature, "X1-AB-XB5C", [.. deposits.Select(deposit => new SurveyDepositModel(deposit))], Now.AddMinutes(30), size);
}
