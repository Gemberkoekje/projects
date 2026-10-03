using System.Globalization;
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SpaceTraders.API.Tests;

/// <summary>
/// B11: Prometheus couldn't scrape <c>/metrics</c>, because it needed the API key. It has a port of its
/// own now, which the Service and the ingress don't route, and that port needs no key.
/// </summary>
public sealed class MetricsEndpointTests
{
    private static readonly HttpClient Http = new();

    /// <summary>Every metric the dashboard and the alerts read (PLAN.md slice 2.1).</summary>
    public static readonly string[] ExpectedMetrics =
    [
        "spacetraders_agent_credits",
        "spacetraders_credits_earned_total",
        "spacetraders_credits_spent_total",
        "spacetraders_trips_total",
        "spacetraders_trip_profit_credits_total",
        "spacetraders_trip_loss_credits_total",
        "spacetraders_ships",
        "spacetraders_ship_status_since_timestamp_seconds",
        "spacetraders_ship_info",
        "spacetraders_ship_capabilities_info",
        "spacetraders_ship_arrival_timestamp_seconds",
        "spacetraders_ship_cargo_units",
        "spacetraders_ship_cargo_capacity_units",
        "spacetraders_extracted_units_total",
        "spacetraders_jettisoned_units_total",
        "spacetraders_extractions_total",
        "spacetraders_surveys_taken_total",
        "spacetraders_surveys_ended_total",
        "spacetraders_surveys_active",
        "spacetraders_market_observed_timestamp_seconds",
        "spacetraders_market_purchase_price",
        "spacetraders_market_sell_price",
        "spacetraders_market_trade_volume",
        "spacetraders_market_supply",
        "spacetraders_market_activity",
        "spacetraders_goods_sold_units_total",
        "spacetraders_goods_bought_units_total",
        "spacetraders_shipyard_observed_timestamp_seconds",
        "spacetraders_shipyard_ship_type",
        "spacetraders_shipyard_ship_price",
        "spacetraders_shipyard_ship_supply",
        "spacetraders_good_supply_chain",
        "spacetraders_contract_units_required",
        "spacetraders_contract_units_fulfilled",
        "spacetraders_contract_deadline_timestamp_seconds",
        "spacetraders_api_requests_total",
        "spacetraders_api_throttled_total",
        "spacetraders_api_rate_limit_wait_seconds_total",
        "spacetraders_messages_handled_total",
        "spacetraders_goal_steps_total",
        "spacetraders_goal_breaker_trips_total",
        "spacetraders_anomaly_active",
        "spacetraders_db_size_bytes",
        "spacetraders_server_next_reset_timestamp_seconds",
        "spacetraders_setting_info",
        "spacetraders_ship_role_info",
        "spacetraders_ship_role_credits_per_hour",
        "spacetraders_purchase_need_credits",
        "spacetraders_credit_reserve",
    ];

    [Fact]
    public async Task TheMetricsPort_ServesEveryMetric_WithoutTheApiKey()
    {
        var port = FreePort();
        await using var factory = new SpaceTradersApiFactory().WithWebHostBuilder(builder => builder
            .UseSetting("Metrics:Port", port.ToString(CultureInfo.InvariantCulture))
            .UseSetting("Metrics:Hostname", "127.0.0.1"));
        _ = factory.Server;

        using var response = await GetOnceListeningAsync(new Uri($"http://127.0.0.1:{port}/metrics"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var text = await response.Content.ReadAsStringAsync();
        foreach (var name in ExpectedMetrics)
        {
            text.Should().Contain($"# TYPE {name} ");
        }
    }

    [Fact]
    public async Task TheApiPort_ServesNoMetrics()
    {
        await using var factory = new SpaceTradersApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", SpaceTradersApiFactory.TestApiKey);

        using var response = await client.GetAsync(new Uri("/spacetraders/api/metrics", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// prometheus-net starts the metrics server in a background service, so the host can be up
    /// before the port listens (on CI the first request was refused).
    /// </summary>
    private static async Task<HttpResponseMessage> GetOnceListeningAsync(Uri uri)
    {
        var deadline = TimeProvider.System.GetUtcNow().AddSeconds(10);
        while (true)
        {
            try
            {
                return await Http.GetAsync(uri);
            }
            catch (HttpRequestException) when (TimeProvider.System.GetUtcNow() < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
            }
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
