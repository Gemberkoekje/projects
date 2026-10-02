using System.Net;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Adapters;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Exceptions;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;

namespace SpaceTraders.Application.Tests.Ports;

/// <summary>
/// Slice 6.4: when the API refuses a survey itself (exhausted, expired, not verified), the miner drops it
/// and extracts without one; any other error stays the API's.
/// </summary>
public sealed class SurveyRefusalTests
{
    private readonly ISpaceTradersApiClient _client = Substitute.For<ISpaceTradersApiClient>();

    [Theory]
    [InlineData(4224, "exhausted")]
    [InlineData(4221, "expired")]
    [InlineData(4220, "not_verified")]
    public async Task ASurveyTheApiRefuses_IsReportedAsRefused(int errorCode, string reason)
    {
        _client.ExtractWithSurveyAsync("SHIP-3", Arg.Any<Survey>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(ApiError(errorCode));

        var extract = () => new SpaceTradersPortAdapter(_client).ExtractWithSurveyAsync("SHIP-3", Survey());

        var refused = (await extract.Should().ThrowAsync<SurveyRefusedException>()).Which;
        refused.Signature.Should().Be("SIG-1");
        refused.Reason.Should().Be(reason);
    }

    [Fact]
    public async Task ASurveyTheApiCantRead_IsReportedAsRejected_WithWhatTheApiSaid()
    {
        // B51, seen on the cluster on 2026-10-02: the API answered an extraction with a survey with 422
        // ("invalid payload"), and the miner tried the same survey again on every step, five calls a
        // tick with Wolverine's retries. Dropped, the survey costs one call, and the API's own words, its
        // "data", are kept for the log.
        const string body = """{"error":{"message":"The request could not be processed due to an invalid payload or application state.","code":422,"data":{"expiration":["Invalid datetime"]}}}""";
        _client.ExtractWithSurveyAsync("SHIP-3", Arg.Any<Survey>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SpaceTradersApiException("invalid payload", HttpStatusCode.UnprocessableEntity, "my/ships/SHIP-3/extract/survey", body, 422));

        var extract = () => new SpaceTradersPortAdapter(_client).ExtractWithSurveyAsync("SHIP-3", Survey());

        var refused = (await extract.Should().ThrowAsync<SurveyRefusedException>()).Which;
        refused.Signature.Should().Be("SIG-1");
        refused.Reason.Should().Be("rejected");
        refused.Detail.Should().Be(body);
    }

    [Fact]
    public async Task AnyOtherError_StaysTheApis()
    {
        // 4000: the ship's cooldown, which has nothing to do with the survey.
        _client.ExtractWithSurveyAsync("SHIP-3", Arg.Any<Survey>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(ApiError(4000));

        var extract = () => new SpaceTradersPortAdapter(_client).ExtractWithSurveyAsync("SHIP-3", Survey());

        await extract.Should().ThrowAsync<SpaceTradersApiException>();
    }

    [Fact]
    public async Task A422WithAGameErrorCode_StaysTheApis()
    {
        // 4228: the hold is full. The ship's state, not the survey, which another ship can still use.
        _client.ExtractWithSurveyAsync("SHIP-3", Arg.Any<Survey>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SpaceTradersApiException("cargo full", HttpStatusCode.UnprocessableEntity, "my/ships/SHIP-3/extract/survey", null, 4228));

        var extract = () => new SpaceTradersPortAdapter(_client).ExtractWithSurveyAsync("SHIP-3", Survey());

        await extract.Should().ThrowAsync<SpaceTradersApiException>();
    }

    private static SpaceTradersApiException ApiError(int errorCode)
        => new("refused", HttpStatusCode.BadRequest, "my/ships/SHIP-3/extract/survey", null, errorCode);

    private static SurveyModel Survey()
        => new("SIG-1", "X1-AB-XB5C", [new SurveyDepositModel("COPPER_ORE")], DateTimeOffset.UtcNow.AddMinutes(10), "SMALL");
}
