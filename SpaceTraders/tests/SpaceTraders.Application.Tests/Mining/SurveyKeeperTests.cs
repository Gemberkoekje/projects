using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;
using static SpaceTraders.Application.Tests.Mining.MiningFixture;

namespace SpaceTraders.Application.Tests.Mining;

/// <summary>
/// Slice 6.4, the survey dashboard: each survey's life (taken, used, ended) is counted and journaled, so
/// surveys that expire unused (too many) and extractions without one (too few) show.
/// </summary>
public sealed class SurveyKeeperTests
{
    private readonly ISurveyRepository _surveys = Substitute.For<ISurveyRepository>();
    private readonly IAutomationMetrics _metrics = Substitute.For<IAutomationMetrics>();
    private readonly LogRecorder _log = new();

    [Fact]
    public async Task ATakenSurvey_IsStored_Counted_AndJournaled()
    {
        var survey = Survey("S-1", XB5C, "COPPER_ORE", "COPPER_ORE", "IRON_ORE");

        await Keeper().TakenAsync("SHIP-1", "COPPER_ORE", [survey], CancellationToken.None);

        await _surveys.Received(1).UpsertAsync("SHIP-1", Arg.Is<IReadOnlyList<SurveyModel>>(s => s.Single() == survey), Arg.Any<CancellationToken>());
        _metrics.Received(1).SurveyTaken(XB5C, "MODERATE");
        var line = _log.Journal.Should().ContainSingle().Subject;
        line.EventKind.Should().Be("Surveyed");
        line.Properties["Signature"].Should().Be("S-1");
        line.Properties["Deposits"].Should().Be("COPPER_ORE x2, IRON_ORE");
        line.Properties["TradeSymbol"].Should().Be("COPPER_ORE");
    }

    [Fact]
    public async Task AnExpiredSurvey_EndsAsExpired_UsedOrNot()
    {
        _surveys.RemoveExpiredAsync(Now, Arg.Any<CancellationToken>()).Returns(
        [
            new StoredSurvey(Survey("S-1", XB5C, "COPPER_ORE"), "SHIP-1", Now.AddMinutes(-40), 0),
            new StoredSurvey(Survey("S-2", XB5C, "IRON_ORE"), "SHIP-1", Now.AddMinutes(-40), 3),
        ]);

        await Keeper().ExpireAsync(Now, CancellationToken.None);

        _metrics.Received(1).SurveyEnded(XB5C, "expired", false);
        _metrics.Received(1).SurveyEnded(XB5C, "expired", true);
        _log.Journal.Should().HaveCount(2).And.OnlyContain(line => line.EventKind == "SurveyEnded" && Equals(line.Properties["Reason"], "expired"));
        _log.Journal.Select(line => line.Properties["Extractions"]).Should().Equal(0, 3);
    }

    [Fact]
    public async Task ARefusedSurvey_EndsWithTheApisReason()
    {
        _surveys.RemoveAsync("S-1", Arg.Any<CancellationToken>()).Returns(
            [new StoredSurvey(Survey("S-1", XB5C, "COPPER_ORE"), "SHIP-1", Now.AddMinutes(-10), 7)]);
        var refused = new SurveyRefusedException("S-1", SurveyRefusedException.ExhaustedErrorCode, new InvalidOperationException("4224"));

        await Keeper().RefusedAsync(refused, CancellationToken.None);

        _metrics.Received(1).SurveyEnded(XB5C, "exhausted", true);
        _log.Journal.Should().ContainSingle(line => line.EventKind == "SurveyEnded" && Equals(line.Properties["Reason"], "exhausted"));
    }

    private SurveyKeeper Keeper() => new(_surveys, _metrics, _log.For<SurveyKeeper>());
}
