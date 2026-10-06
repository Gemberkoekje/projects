using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Ports;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Adapters;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Clients;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Configuration;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Exceptions;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Agents;
using SpaceTraders.Infrastructure.SpaceTradersAPI.Models.Fleet;

namespace SpaceTraders.Application.Tests.Ports;

/// <summary>
/// Slice 6.31. The API (v2.3.0) warps a ship in orbit with a warp drive to a waypoint of another system, named by its waypoint
/// (the docs' guide shows a system, the spec a waypoint), and answers with its nav and fuel. Its sensor array scans for the
/// systems around it, each with its position and distance, and starts a cooldown. A bought ship comes back with its modules
/// and engine, which startup sync caches: an explorer's warp drive and speed.
/// </summary>
public sealed class WarpRequestTests
{
    [Fact]
    public async Task AWarp_GoesOut_WithTheDestinationsWaypoint_AndComesBackWithTheNavAndTheFuel()
    {
        string? sent = null;
        using var handler = new AnsweringHandler(body => sent = body, HttpStatusCode.OK, WarpAnswer);
        using var http = Http(handler);
        var port = new SpaceTradersPortAdapter(Client(http));

        var warp = await port.WarpShipAsync("SPECTER-50", "X1-ZZ69-I53");

        using var json = JsonDocument.Parse(sent!);
        json.RootElement.GetProperty("waypointSymbol").GetString().Should().Be("X1-ZZ69-I53");
        warp.Nav.Should().BeEquivalentTo(new { Status = "IN_TRANSIT", SystemSymbol = "X1-ZZ69", WaypointSymbol = "X1-ZZ69-I53", FlightMode = "CRUISE" });
        warp.Nav.ArrivesAt.Should().Be(DateTimeOffset.Parse("2026-10-06T14:12:33.000Z", System.Globalization.CultureInfo.InvariantCulture));
        warp.Fuel.Should().BeEquivalentTo(new { Current = 269, Capacity = 800 });
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, 4203, true)]
    [InlineData(HttpStatusCode.TooManyRequests, 429, false)]
    public async Task AWarpTheApiRefuses_IsAWarpRefusal_WithItsCode_ButNotTheRateLimitersAnswer(HttpStatusCode status, int code, bool refused)
    {
        var client = Substitute.For<ISpaceTradersApiClient>();
        client.WarpShipAsync("SPECTER-50", "X1-ZZ69-I53", Arg.Any<CancellationToken>())
            .ThrowsAsync(new SpaceTradersApiException("refused", status, "my/ships/SPECTER-50/warp", "{}", code));

        var warp = () => new SpaceTradersPortAdapter(client).WarpShipAsync("SPECTER-50", "X1-ZZ69-I53");

        if (refused)
        {
            (await warp.Should().ThrowAsync<WarpRefusedException>()).Which.ErrorCode.Should().Be(WarpRefusedException.InsufficientFuel);
        }
        else
        {
            await warp.Should().ThrowAsync<SpaceTradersApiException>();
        }
    }

    [Fact]
    public async Task AScan_ComesBack_WithTheSystemsAroundTheShip_AndTheCooldown()
    {
        const string scan = """
            {"data":{
              "cooldown":{"shipSymbol":"SPECTER-50","totalSeconds":70,"remainingSeconds":70,"expiration":"2026-10-06T14:01:10.000Z"},
              "systems":[
                {"symbol":"X1-NEW","sectorSymbol":"X1","type":"RED_STAR","x":17833,"y":3477,"distance":360},
                {"symbol":"X1-AFAR","sectorSymbol":"X1","type":"BLUE_STAR","x":16333,"y":3117,"distance":1500}]
            }}
            """;
        string? path = null;
        using var handler = new AnsweringHandler(_ => { }, HttpStatusCode.Created, scan, uri => path = uri);
        using var http = Http(handler);
        var port = new SpaceTradersPortAdapter(Client(http));

        var result = await port.ScanSystemsAsync("SPECTER-50");

        path.Should().EndWith("my/ships/SPECTER-50/scan/systems");
        result.Systems.Should().BeEquivalentTo(
        [
            new ScannedSystemModel("X1-NEW", "X1", "RED_STAR", 17833, 3477, 360),
            new ScannedSystemModel("X1-AFAR", "X1", "BLUE_STAR", 16333, 3117, 1500),
        ]);
        result.CooldownSeconds.Should().Be(70);
        result.CooldownExpiresAt.Should().Be(DateTimeOffset.Parse("2026-10-06T14:01:10.000Z", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ABoughtShip_ComesBack_WithItsMountsModulesAndEngine()
    {
        var client = Substitute.For<ISpaceTradersApiClient>();
        var nav = new ShipNav { SystemSymbol = "X1-GT9", WaypointSymbol = "X1-GT9-AE7B", Status = "DOCKED", FlightMode = "CRUISE" };
        client.PurchaseShipAsync("SHIP_EXPLORER", "X1-GT9-AE7B", Arg.Any<CancellationToken>()).Returns(new PurchaseShipResult
        {
            Agent = new Agent { Symbol = "SPECTER", StartingFaction = "COSMIC", Credits = 1_200_000 },
            Ship = new Ship { Symbol = "SPECTER-50", Nav = nav, Fuel = new ShipFuel { Current = 800, Capacity = 800 } },
            Transaction = new ShipyardTransaction { WaypointSymbol = "X1-GT9-AE7B", ShipSymbol = "SPECTER-50", ShipType = "SHIP_EXPLORER", AgentSymbol = "SPECTER", Price = 702_315 },
        });
        client.GetMyShipAsync("SPECTER-50", Arg.Any<CancellationToken>()).Returns(new Ship
        {
            Symbol = "SPECTER-50",
            Nav = nav,
            Mounts = [new ShipMount { Symbol = "MOUNT_SENSOR_ARRAY_II" }],
            Modules = [new ShipModule { Symbol = "MODULE_WARP_DRIVE_I", Range = 2000 }],
            Engine = new ShipComponent { Symbol = "ENGINE_ION_DRIVE_II", Speed = 36 },
        });

        var bought = await new SpaceTradersPortAdapter(client).PurchaseShipAsync("SHIP_EXPLORER", "X1-GT9-AE7B");

        bought.MountSymbols.Should().Equal("MOUNT_SENSOR_ARRAY_II");
        var ship = new ShipModel("SPECTER-50", "X1-GT9", "X1-GT9-AE7B", "DOCKED", "CRUISE", 800, 800, ModulesJson: bought.ModulesJson, EngineJson: bought.EngineJson);
        SpaceTraders.Application.Exploring.Warps.Range(ship).Should().Be(2000);
        SpaceTraders.Application.Automation.FleetRoles.EngineSpeed(ship, 9).Should().Be(36);
    }

    // The answer of 2.3.0's warp-ship: nav and fuel.
    private const string WarpAnswer = """
        {"data":{
          "nav":{"systemSymbol":"X1-ZZ69","waypointSymbol":"X1-ZZ69-I53","status":"IN_TRANSIT","flightMode":"CRUISE",
            "route":{"origin":{"symbol":"X1-GT9-E10Z","systemSymbol":"X1-GT9"},"destination":{"symbol":"X1-ZZ69-I53","systemSymbol":"X1-ZZ69"},
              "arrival":"2026-10-06T14:12:33.000Z","departureTime":"2026-10-06T14:00:00.000Z"}},
          "fuel":{"current":269,"capacity":800,"consumed":{"amount":531,"timestamp":"2026-10-06T14:00:00.000Z"}}
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

    // Records the request's body and path, and answers with the given status and body.
    private sealed class AnsweringHandler(Action<string> capture, HttpStatusCode status, string answer, Action<string>? path = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            capture(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            path?.Invoke(request.RequestUri?.ToString() ?? string.Empty);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(answer, Encoding.UTF8, "application/json"),
            };
        }
    }
}
