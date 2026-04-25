using System.Collections.Generic;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace DatumPrikker.Tests;

public class WebTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    [Test]
    public async Task GetWebResourceRootReturnsOkStatusCode()
    {
        // Arrange
        var cancellationToken = TestContext.CurrentContext.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.DatumPrikker_AppHost>(cancellationToken);
        appHost.Services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            // Override the logging filters from the app's configuration
            logging.AddFilter(appHost.Environment.ApplicationName, LogLevel.Debug);
            logging.AddFilter("Aspire.", LogLevel.Debug);
        });
        appHost.Services.ConfigureHttpClientDefaults(clientBuilder =>
        {
            clientBuilder.AddStandardResilienceHandler();
        });

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        // Act
        var httpClient = app.CreateHttpClient("webfrontend");
        await app.ResourceNotifications.WaitForResourceHealthyAsync("webfrontend", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        var response = await httpClient.GetAsync("/", cancellationToken);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task CreateRespondAndGetResults_UsesLastWriteWinsForSameRespondent()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.DatumPrikker_AppHost>(cancellationToken);

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        var apiClient = app.CreateHttpClient("apiservice");
        const string ownerIdentityId = "google:owner-123";

        var createRequest = new CreatePollRequestDto(
            "Sprint planning",
            "Pick your slot",
            null,
            [
                new CreatePollOptionRequestDto(DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1)), new TimeOnly(9, 0), new TimeOnly(10, 0)),
                new CreatePollOptionRequestDto(DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1)), new TimeOnly(10, 0), new TimeOnly(11, 0)),
            ]);

        using var createMessage = new HttpRequestMessage(HttpMethod.Post, "/api/polls")
        {
            Content = JsonContent.Create(createRequest),
        };
        createMessage.Headers.Add("X-Owner-IdentityId", ownerIdentityId);

        using var createResponse = await apiClient.SendAsync(createMessage, cancellationToken);
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var createdPoll = await createResponse.Content.ReadFromJsonAsync<CreatePollResponseDto>(cancellationToken);
        Assert.That(createdPoll, Is.Not.Null);

        var details = await apiClient.GetFromJsonAsync<PollDetailsDto>($"/api/polls/{createdPoll!.ShareToken}", cancellationToken);
        Assert.That(details, Is.Not.Null);
        Assert.That(details!.Options.Count, Is.EqualTo(2));

        var firstOptionId = details.Options[0].PollOptionId;
        var secondOptionId = details.Options[1].PollOptionId;

        var firstSubmission = new SubmitResponsesRequestDto(
            "Alice",
            [
                new SubmitResponseInputDto(firstOptionId, 1, "Works for me"),
                new SubmitResponseInputDto(secondOptionId, 3, "Maybe"),
            ]);

        var firstRespondResponse = await apiClient.PostAsJsonAsync($"/api/polls/{createdPoll.ShareToken}/respond", firstSubmission, cancellationToken);
        Assert.That(firstRespondResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var secondSubmission = new SubmitResponsesRequestDto(
            "Alice",
            [
                new SubmitResponseInputDto(firstOptionId, 2, "Changed mind"),
                new SubmitResponseInputDto(secondOptionId, 3, "Still maybe"),
            ]);

        var secondRespondResponse = await apiClient.PostAsJsonAsync($"/api/polls/{createdPoll.ShareToken}/respond", secondSubmission, cancellationToken);
        Assert.That(secondRespondResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        using var resultsMessage = new HttpRequestMessage(HttpMethod.Get, $"/api/polls/{createdPoll.ShareToken}/results");
        resultsMessage.Headers.Add("X-Owner-IdentityId", ownerIdentityId);

        using var resultsResponse = await apiClient.SendAsync(resultsMessage, cancellationToken);
        Assert.That(resultsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = await resultsResponse.Content.ReadFromJsonAsync<PollResultsDto>(cancellationToken);
        Assert.That(results, Is.Not.Null);

        var firstOptionResult = results!.Options.Single(x => x.PollOptionId == firstOptionId);
        Assert.That(firstOptionResult.YesCount, Is.EqualTo(0));
        Assert.That(firstOptionResult.NoCount, Is.EqualTo(1));
        Assert.That(firstOptionResult.MaybeCount, Is.EqualTo(0));

        var secondOptionResult = results.Options.Single(x => x.PollOptionId == secondOptionId);
        Assert.That(secondOptionResult.YesCount, Is.EqualTo(0));
        Assert.That(secondOptionResult.NoCount, Is.EqualTo(0));
        Assert.That(secondOptionResult.MaybeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task GetResults_RequiresOwnerAndRestrictsToPollOwner()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.DatumPrikker_AppHost>(cancellationToken);

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        var apiClient = app.CreateHttpClient("apiservice");
        const string ownerIdentityId = "google:owner-authz";
        const string otherOwnerIdentityId = "google:other-owner";

        var createdPoll = await CreatePollAsync(apiClient, ownerIdentityId, closesAtUtc: null, cancellationToken);

        using var noHeaderRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/polls/{createdPoll.ShareToken}/results");
        using var noHeaderResponse = await apiClient.SendAsync(noHeaderRequest, cancellationToken);
        Assert.That(noHeaderResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        using var otherOwnerRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/polls/{createdPoll.ShareToken}/results");
        otherOwnerRequest.Headers.Add("X-Owner-IdentityId", otherOwnerIdentityId);
        using var otherOwnerResponse = await apiClient.SendAsync(otherOwnerRequest, cancellationToken);
        Assert.That(otherOwnerResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Respond_ReturnsConflict_WhenPollIsClosedByDeadline()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.DatumPrikker_AppHost>(cancellationToken);

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        var apiClient = app.CreateHttpClient("apiservice");
        const string ownerIdentityId = "google:owner-closed";

        var createdPoll = await CreatePollAsync(apiClient, ownerIdentityId, DateTimeOffset.UtcNow.AddMinutes(-1), cancellationToken);
        var details = await apiClient.GetFromJsonAsync<PollDetailsDto>($"/api/polls/{createdPoll.ShareToken}", cancellationToken);
        Assert.That(details, Is.Not.Null);

        var respondRequest = new SubmitResponsesRequestDto(
            "Alice",
            [new SubmitResponseInputDto(details!.Options[0].PollOptionId, 1, "I can make it")]);

        using var response = await apiClient.PostAsJsonAsync($"/api/polls/{createdPoll.ShareToken}/respond", respondRequest, cancellationToken);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task AnonymousRespond_IsRateLimited_AfterWindowLimit()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.DatumPrikker_AppHost>(cancellationToken);

        await using var app = await appHost.BuildAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.StartAsync(cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("apiservice", cancellationToken).WaitAsync(DefaultTimeout, cancellationToken);

        var apiClient = app.CreateHttpClient("apiservice");
        const string ownerIdentityId = "google:owner-rate-limit";

        var createdPoll = await CreatePollAsync(apiClient, ownerIdentityId, closesAtUtc: null, cancellationToken);
        var details = await apiClient.GetFromJsonAsync<PollDetailsDto>($"/api/polls/{createdPoll.ShareToken}", cancellationToken);
        Assert.That(details, Is.Not.Null);

        var respondRequest = new SubmitResponsesRequestDto(
            "Alice",
            [new SubmitResponseInputDto(details!.Options[0].PollOptionId, 1, "rate-limit")]);

        HttpStatusCode lastStatusCode = HttpStatusCode.OK;

        for (var i = 0; i < 31; i++)
        {
            using var response = await apiClient.PostAsJsonAsync($"/api/polls/{createdPoll.ShareToken}/respond", respondRequest, cancellationToken);
            lastStatusCode = response.StatusCode;
        }

        Assert.That(lastStatusCode, Is.EqualTo((HttpStatusCode)429));
    }

    private static async Task<CreatePollResponseDto> CreatePollAsync(
        HttpClient apiClient,
        string ownerIdentityId,
        DateTimeOffset? closesAtUtc,
        CancellationToken cancellationToken)
    {
        var createRequest = new CreatePollRequestDto(
            "Test poll",
            "Test description",
            closesAtUtc,
            [
                new CreatePollOptionRequestDto(DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1)), new TimeOnly(9, 0), new TimeOnly(10, 0)),
                new CreatePollOptionRequestDto(DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(1)), new TimeOnly(10, 0), new TimeOnly(11, 0)),
            ]);

        using var createMessage = new HttpRequestMessage(HttpMethod.Post, "/api/polls")
        {
            Content = JsonContent.Create(createRequest),
        };
        createMessage.Headers.Add("X-Owner-IdentityId", ownerIdentityId);

        using var createResponse = await apiClient.SendAsync(createMessage, cancellationToken);
        createResponse.EnsureSuccessStatusCode();

        var createdPoll = await createResponse.Content.ReadFromJsonAsync<CreatePollResponseDto>(cancellationToken);
        if (createdPoll is null)
        {
            throw new InvalidOperationException("Create poll response was empty.");
        }

        return createdPoll;
    }

    private sealed record CreatePollRequestDto(
        string Title,
        string Description,
        DateTimeOffset? ClosesAtUtc,
        List<CreatePollOptionRequestDto> Options);

    private sealed record CreatePollOptionRequestDto(DateOnly Date, TimeOnly StartTime, TimeOnly EndTime);

    private sealed record CreatePollResponseDto(Guid PollId, string ShareToken);

    private sealed record PollDetailsDto(
        Guid PollId,
        string ShareToken,
        string Title,
        string Description,
        DateTimeOffset? ClosesAtUtc,
        DateTimeOffset? ClosedAtUtc,
        bool IsOpen,
        List<PollOptionDto> Options);

    private sealed record PollOptionDto(Guid PollOptionId, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime);

    private sealed record SubmitResponsesRequestDto(string RespondentName, List<SubmitResponseInputDto> Responses);

    private sealed record SubmitResponseInputDto(Guid PollOptionId, int Availability, string Reason);

    private sealed record PollResultsDto(Guid PollId, string ShareToken, string Title, List<PollResultOptionDto> Options);

    private sealed record PollResultOptionDto(
        Guid PollOptionId,
        DateOnly Date,
        TimeOnly StartTime,
        TimeOnly EndTime,
        int YesCount,
        int MaybeCount,
        int NoCount,
        List<PollResultRespondentDto> Respondents);

    private sealed record PollResultRespondentDto(string RespondentName, int Availability, string Reason);
}
