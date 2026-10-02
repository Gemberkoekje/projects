using FluentAssertions;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.Persistence.Repositories;

namespace SpaceTraders.Application.Tests.Services;

/// <summary>
/// Slice 6.4: the cache keeps each survey with who took it, when, and how often it was used, until it
/// expires or the API refuses it, and hands it back when it goes, for the survey dashboard.
/// </summary>
public sealed class SurveyRepositoryTests
{
    private static readonly DateTimeOffset Now = TimeProvider.System.GetUtcNow();

    [Fact]
    public async Task ASurvey_IsStoredWithItsShip_AndCountsItsExtractions()
    {
        await using var db = TestDbContextFactory.Create();
        var surveys = new SurveyRepository(db);

        await surveys.UpsertAsync("SHIP-1", [Survey("SIG-1", Now.AddMinutes(30))]);
        await surveys.RecordExtractionAsync("SIG-1");
        await surveys.RecordExtractionAsync("SIG-1");
        await surveys.RecordExtractionAsync("SIG-UNKNOWN");

        var stored = (await surveys.GetActiveAsync()).Should().ContainSingle().Subject;
        stored.ShipSymbol.Should().Be("SHIP-1");
        stored.Extractions.Should().Be(2);
        stored.Survey.Deposits.Select(deposit => deposit.Symbol).Should().Equal("COPPER_ORE", "IRON_ORE");
    }

    [Fact]
    public async Task StoringASurveyAgain_KeepsItsUseCount()
    {
        await using var db = TestDbContextFactory.Create();
        var surveys = new SurveyRepository(db);
        await surveys.UpsertAsync("SHIP-1", [Survey("SIG-1", Now.AddMinutes(30))]);
        await surveys.RecordExtractionAsync("SIG-1");

        await surveys.UpsertAsync("SHIP-1", [Survey("SIG-1", Now.AddMinutes(30)), Survey("SIG-2", Now.AddMinutes(30))]);

        (await surveys.GetActiveAsync()).Should().HaveCount(2).And.Contain(s => s.Survey.Signature == "SIG-1" && s.Extractions == 1);
    }

    [Fact]
    public async Task ExpiredSurveys_AreRemoved_AndHandedBack()
    {
        await using var db = TestDbContextFactory.Create();
        var surveys = new SurveyRepository(db);
        await surveys.UpsertAsync("SHIP-1", [Survey("SIG-OLD", Now.AddMinutes(-1)), Survey("SIG-NEW", Now.AddMinutes(30))]);

        (await surveys.GetActiveAsync()).Should().ContainSingle(s => s.Survey.Signature == "SIG-NEW");
        var removed = await surveys.RemoveExpiredAsync(Now);

        removed.Should().ContainSingle().Which.Survey.Signature.Should().Be("SIG-OLD");
        (await surveys.RemoveExpiredAsync(Now)).Should().BeEmpty();
        (await surveys.GetActiveAsync()).Should().ContainSingle(s => s.Survey.Signature == "SIG-NEW");
    }

    [Fact]
    public async Task ARefusedSurvey_IsRemoved_AndHandedBackOnce()
    {
        await using var db = TestDbContextFactory.Create();
        var surveys = new SurveyRepository(db);
        await surveys.UpsertAsync("SHIP-1", [Survey("SIG-1", Now.AddMinutes(30))]);
        await surveys.RecordExtractionAsync("SIG-1");

        (await surveys.RemoveAsync("SIG-1")).Should().ContainSingle().Which.Extractions.Should().Be(1);
        (await surveys.RemoveAsync("SIG-1")).Should().BeEmpty();
        (await surveys.GetActiveAsync()).Should().BeEmpty();
    }

    private static SurveyModel Survey(string signature, DateTimeOffset expiration)
        => new(signature, "X1-AB-XB5C", [new SurveyDepositModel("COPPER_ORE"), new SurveyDepositModel("IRON_ORE")], expiration, "SMALL");
}
