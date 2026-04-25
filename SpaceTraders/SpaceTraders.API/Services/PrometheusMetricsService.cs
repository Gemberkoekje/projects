using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prometheus;
using SpaceTraders.Application.Interfaces;

namespace SpaceTraders.API.Services;

/// <summary>
/// Background service that periodically exports SpaceTraders-specific metrics to Prometheus.
/// Runs on the same 10-second cadence as the credit history sparkline so that
/// the credit history gauge and sparkline share the same resolution.
///
/// Metrics exposed:
/// <list type="bullet">
///   <item><c>spacetraders_agent_credits</c> – current agent credit balance (Gauge)</item>
///   <item><c>spacetraders_api_calls_total</c> – cumulative API calls (Counter)</item>
///   <item><c>spacetraders_api_throttled_total</c> – cumulative throttled requests (Counter)</item>
/// </list>
/// </summary>
public sealed class PrometheusMetricsService(
    IServiceScopeFactory serviceScopeFactory,
    ICreditHistoryService creditHistory,
    IRateLimitStatus rateLimitStatus,
    ILogger<PrometheusMetricsService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;

    private static readonly TimeSpan ScrapeInterval = TimeSpan.FromSeconds(10);

    private static readonly Gauge AgentCreditsGauge = Metrics
        .CreateGauge("spacetraders_agent_credits", "Current agent credit balance.");

    private static readonly Counter ApiCallsCounter = Metrics
        .CreateCounter("spacetraders_api_calls_total", "Total SpaceTraders API calls made.");

    private static readonly Counter ApiThrottledCounter = Metrics
        .CreateCounter("spacetraders_api_throttled_total", "Total SpaceTraders API requests throttled by rate limiter.");

    private int _lastTotalRequests;
    private int _lastThrottledCount;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                UpdateMetrics();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to update Prometheus metrics.");
            }

            await Task.Delay(ScrapeInterval, stoppingToken);
        }
    }

    private void UpdateMetrics()
    {
        // Credit gauge – taken from most recent history entry
        var history = creditHistory.GetHistory();
        if (history.Count > 0)
            AgentCreditsGauge.Set(history[history.Count - 1].Credits);

        // API call counters – atomically snapshot and delta-encode the running totals.
        var currentTotal = rateLimitStatus.TotalRequests;
        var prevTotal = Interlocked.Exchange(ref _lastTotalRequests, currentTotal);
        var totalDelta = currentTotal - prevTotal;
        if (totalDelta > 0)
            ApiCallsCounter.Inc(totalDelta);

        var currentThrottled = rateLimitStatus.ThrottledCount;
        var prevThrottled = Interlocked.Exchange(ref _lastThrottledCount, currentThrottled);
        var throttledDelta = currentThrottled - prevThrottled;
        if (throttledDelta > 0)
            ApiThrottledCounter.Inc(throttledDelta);
    }
}
