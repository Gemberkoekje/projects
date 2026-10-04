using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Adapters;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Configuration;

namespace SpaceTraders.Application.Tests.Ports;

/// <summary>
/// Exploring through jump gates (asked on 2026-10-04). The API (v2.3.0) jumps a ship in orbit at a gate to a gate the
/// first is connected to, named by its waypoint, and buys one unit of ANTIMATTER for it at the gate's market. The client
/// sent the destination's system instead, a call nothing had made yet; and the gate's connections came back as systems,
/// which a jump can't name.
/// </summary>
public sealed class JumpRequestTests
{
    [Fact]
    public async Task AJump_GoesOut_WithTheDestinationGatesWaypoint()
    {
        string? sent = null;
        using var handler = new AnsweringHandler(body => sent = body, HttpStatusCode.OK, JumpAnswer);
        using var http = Http(handler);
        var client = Client(http);

        await client.JumpShipAsync("SPECTER-1", "X1-KR90-AF5F");

        using var json = JsonDocument.Parse(sent!);
        json.RootElement.GetProperty("waypointSymbol").GetString().Should().Be("X1-KR90-AF5F");
        json.RootElement.TryGetProperty("systemSymbol", out _).Should().BeFalse();
    }

    [Fact]
    public async Task AJump_ComesBack_WithWhereTheShipIsWhatTheAntimatterCostAndTheCredits()
    {
        using var handler = new AnsweringHandler(_ => { }, HttpStatusCode.OK, JumpAnswer);
        using var http = Http(handler);
        var port = new SpaceTradersPortAdapter(Client(http));

        var jump = await port.JumpShipAsync("SPECTER-1", "X1-KR90-AF5F");

        jump.Nav.SystemSymbol.Should().Be("X1-KR90");
        jump.Nav.WaypointSymbol.Should().Be("X1-KR90-AF5F");
        jump.Nav.Status.Should().Be("IN_ORBIT");
        jump.CooldownSeconds.Should().Be(64);
        jump.CooldownExpiresAt.Should().Be(DateTimeOffset.Parse("2026-10-04T08:01:04.000Z", System.Globalization.CultureInfo.InvariantCulture));
        jump.Cost.Should().Be(4_520);
        jump.AgentCredits.Should().Be(145_480);
    }

    [Fact]
    public async Task AJumpGate_ListsTheGatesItConnectsTo()
    {
        const string gate = """
            {"data":{"symbol":"X1-DC53-I55","connections":["X1-HZ59-I59","X1-BG54-I54","X1-KR90-AF5F","X1-MT49-DX8X"]}}
            """;
        using var handler = new AnsweringHandler(_ => { }, HttpStatusCode.OK, gate);
        using var http = Http(handler);
        var port = new SpaceTradersPortAdapter(Client(http));

        var connections = await port.GetJumpGateConnectionsAsync("X1-DC53", "X1-DC53-I55");

        connections.WaypointSymbol.Should().Be("X1-DC53-I55");
        connections.Connections.Should().Equal("X1-HZ59-I59", "X1-BG54-I54", "X1-KR90-AF5F", "X1-MT49-DX8X");
        connections.ConnectedSystems.Should().Equal("X1-HZ59", "X1-BG54", "X1-KR90", "X1-MT49");
    }

    // The answer of 2.3.0's jump-ship: nav, cooldown, transaction and agent, all required.
    private const string JumpAnswer = """
        {"data":{
          "nav":{"systemSymbol":"X1-KR90","waypointSymbol":"X1-KR90-AF5F","status":"IN_ORBIT","flightMode":"CRUISE",
            "route":{"origin":{"symbol":"X1-DC53-I55","systemSymbol":"X1-DC53"},"destination":{"symbol":"X1-KR90-AF5F","systemSymbol":"X1-KR90"},
              "arrival":"2026-10-04T08:00:00.000Z","departureTime":"2026-10-04T08:00:00.000Z"}},
          "cooldown":{"shipSymbol":"SPECTER-1","totalSeconds":64,"remainingSeconds":64,"expiration":"2026-10-04T08:01:04.000Z"},
          "transaction":{"waypointSymbol":"X1-DC53-I55","shipSymbol":"SPECTER-1","tradeSymbol":"ANTIMATTER","type":"PURCHASE","units":1,
            "pricePerUnit":4520,"totalPrice":4520,"timestamp":"2026-10-04T08:00:00.000Z"},
          "agent":{"accountId":"acc","symbol":"SPECTER","headquarters":"X1-DC53-A1","credits":145480,"startingFaction":"COSMIC","shipCount":21}
        }}
        """;

#pragma warning disable IDISP014 // One client over a fake handler: there are no sockets to exhaust.
    private static HttpClient Http(HttpMessageHandler handler)
        => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.spacetraders.io/v2/") };
#pragma warning restore IDISP014

    private static SpaceTradersApiClient Client(HttpClient http)
        => new(
            http,
            Options.Create(new SpaceTradersApiOptions { AgentToken = "agent-token" }),
            new AgentTokenProvider(),
            Substitute.For<IApiEndpointUsageRecorder>(),
            Substitute.For<IServerResetMonitor>());

    // Records the request's body, and answers with the given status and body.
    private sealed class AnsweringHandler(Action<string> capture, HttpStatusCode status, string answer) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            capture(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(answer, Encoding.UTF8, "application/json"),
            };
        }
    }
}
