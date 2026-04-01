using AiUsageMonitor.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Qowaiv;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AiUsageMonitor.Clients;

public sealed class CopilotUsageClient : ICopilotUsageClient
{
    private const string DefaultEndpointTemplate = "https://api.github.com/users/{username}/settings/billing/premium_request/usage";

    private readonly HttpClient _http;
    private readonly ILogger<CopilotUsageClient> _logger;
    private readonly string _endpoint;

    public CopilotUsageClient(HttpClient http, IConfiguration config, ILogger<CopilotUsageClient> logger)
    {
        _http = http;
        _logger = logger;

        var apiToken = config["CoPilot:ApiToken"] ?? string.Empty;
        var configuredEndpoint = config["CoPilot:Endpoint"];
        var username = config["CoPilot:Username"];

        _endpoint = BuildEndpoint(configuredEndpoint, username);

        if (string.IsNullOrWhiteSpace(apiToken))
        {
            logger.LogWarning("CoPilot API token is not configured");
        }

        if (!string.IsNullOrWhiteSpace(apiToken))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        }

        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
    }

    public async Task<UsageSnapshotRecorded> GetLatestUsageAsync(CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, _endpoint);
        req.Headers.UserAgent.ParseAdd("AiUsageMonitor/1.0");

        using var res = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
        res.EnsureSuccessStatusCode();

        using var stream = await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var timestamp = Clock.UtcNow();
        var periodStart = new DateTime(timestamp.Year, timestamp.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1).AddTicks(-1);

        ExtractUsageMetrics(doc.RootElement, ref periodStart, ref periodEnd, out var tokensUsed, out var seatsUsed, out var costIncurred);

        var snapshot = new UsageSnapshotRecorded
        {
            Provider = "CoPilot",
            Timestamp = timestamp,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            SeatsUsed = seatsUsed,
            TokensUsed = tokensUsed,
            CostIncurred = costIncurred,
        };

        _logger.LogInformation("Fetched CoPilot usage: {Requests} premium requests, ${Cost:F2} incurred", tokensUsed, costIncurred);
        return snapshot;
    }

    private string BuildEndpoint(string? endpoint, string? username)
    {
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            if (!string.IsNullOrWhiteSpace(username) && endpoint.Contains("{USERNAME_PLACEHOLDER}", StringComparison.Ordinal))
            {
                return endpoint.Replace("{USERNAME_PLACEHOLDER}", Uri.EscapeDataString(username), StringComparison.Ordinal);
            }

            return endpoint;
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            _logger.LogWarning("CoPilot username is not configured; using a placeholder endpoint until CoPilot:Username is set");
            return DefaultEndpointTemplate.Replace("{username}", "{USERNAME_PLACEHOLDER}", StringComparison.Ordinal);
        }

        return DefaultEndpointTemplate.Replace("{username}", Uri.EscapeDataString(username), StringComparison.Ordinal);
    }

    private static void ExtractUsageMetrics(JsonElement root, ref DateTime periodStart, ref DateTime periodEnd, out long tokensUsed, out int seatsUsed, out decimal costIncurred)
    {
        tokensUsed = 0;
        seatsUsed = 0;
        costIncurred = 0m;

        if (root.ValueKind != JsonValueKind.Object)
            return;

        if (root.TryGetProperty("timePeriod", out var timePeriod) && timePeriod.ValueKind == JsonValueKind.Object)
        {
            var year = timePeriod.TryGetProperty("year", out var yearEl) && yearEl.TryGetInt32(out var y) ? y : periodStart.Year;
            var month = timePeriod.TryGetProperty("month", out var monthEl) && monthEl.TryGetInt32(out var m) ? m : periodStart.Month;
            periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            periodEnd = periodStart.AddMonths(1).AddTicks(-1);
        }

        if (!root.TryGetProperty("usageItems", out var usageItems) || usageItems.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in usageItems.EnumerateArray())
        {
            if (TryGetLong(item, "grossQuantity", out var qty))
                tokensUsed += qty;

            if (TryGetDecimal(item, "grossAmount", out var amount))
                costIncurred += amount;
        }

        seatsUsed = tokensUsed > 0 ? 1 : 0;
    }

    private static bool TryGetLong(JsonElement parent, string propertyName, out long value)
    {
        value = 0;
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(propertyName, out var el) || el.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        try
        {
            value = el.GetInt64();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetDecimal(JsonElement parent, string propertyName, out decimal value)
    {
        value = 0m;
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(propertyName, out var el) || el.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        return el.TryGetDecimal(out value);
    }
}
