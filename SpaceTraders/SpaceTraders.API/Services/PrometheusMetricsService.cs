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

        // API call counters – delta-encode the running totals from RateLimitStatus
        var totalDelta = rateLimitStatus.TotalRequests - _lastTotalRequests;
        if (totalDelta > 0)
        {
            ApiCallsCounter.Inc(totalDelta);
            _lastTotalRequests = rateLimitStatus.TotalRequests;
        }

        var throttledDelta = rateLimitStatus.ThrottledCount - _lastThrottledCount;
        if (throttledDelta > 0)
        {
            ApiThrottledCounter.Inc(throttledDelta);
            _lastThrottledCount = rateLimitStatus.ThrottledCount;
        }
    }
}
