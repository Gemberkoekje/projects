using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Configuration;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Exceptions;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;

namespace SpaceTraders.Application.Tests.Ports;

/// <summary>
/// B51, seen on the cluster on 2026-10-02: the first extraction with a survey the bot had taken itself was
/// answered with 422 ("invalid payload"), on every step. A survey goes back as the API gave it out: its
/// expiry went with an offset (<c>+00:00</c>) instead of the API's own form.
/// </summary>
public sealed class SurveyRequestTests
{
    [Fact]
    public async Task ASurvey_GoesBack_WithItsExpiryAsTheApiWroteIt()
    {
        string? sent = null;
        using var handler = new CapturingHandler(body => sent = body);
#pragma warning disable IDISP014 // One client over a fake handler: there are no sockets to exhaust.
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.spacetraders.io/v2/") };
#pragma warning restore IDISP014
        var client = new SpaceTradersApiClient(
            http,
            Options.Create(new SpaceTradersApiOptions { AgentToken = "agent-token" }),
            new AgentTokenProvider(),
            Substitute.For<IApiEndpointUsageRecorder>(),
            Substitute.For<IServerResetMonitor>());
        var survey = new Survey
        {
            Signature = "X1-DC53-XB5C-FA497C",
            Symbol = "X1-DC53-XB5C",
            Deposits = [new SurveyDeposit { Symbol = "ICE_WATER" }, new SurveyDeposit { Symbol = "COPPER_ORE" }],
            Expiration = DateTimeOffset.Parse("2026-10-02T15:44:51.937Z", System.Globalization.CultureInfo.InvariantCulture),
            Size = "SMALL",
        };

        var extract = async () => await client.ExtractWithSurveyAsync("SPECTER-3", survey);

        await extract.Should().ThrowAsync<SpaceTradersApiException>();
        using var json = JsonDocument.Parse(sent!);
        json.RootElement.GetProperty("expiration").GetString().Should().Be("2026-10-02T15:44:51.937Z");
        json.RootElement.GetProperty("signature").GetString().Should().Be("X1-DC53-XB5C-FA497C");
        json.RootElement.GetProperty("symbol").GetString().Should().Be("X1-DC53-XB5C");
        json.RootElement.GetProperty("size").GetString().Should().Be("SMALL");
        var deposits = json.RootElement.GetProperty("deposits");
        deposits.GetArrayLength().Should().Be(2);
        deposits[0].GetProperty("symbol").GetString().Should().Be("ICE_WATER");
        deposits[1].GetProperty("symbol").GetString().Should().Be("COPPER_ORE");
    }

    // Records the request's body, and answers as the API did.
    private sealed class CapturingHandler(Action<string> capture) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            capture(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent(
                    """{"error":{"message":"The request could not be processed due to an invalid payload or application state.","code":422}}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }
}
