using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Infrastructure.SpaceTradersAPI.Notifications;

/// <summary>
/// Sends alert notifications to a configurable webhook URL (Slack-compatible JSON payload).
/// The webhook URL is read from the <c>Alerts.WebhookUrl</c> setting at send-time so
/// operators can change it without restarting the service.
/// No-op when the setting is absent or empty.
/// </summary>
public sealed class WebhookAlertNotifier(
    IHttpClientFactory httpClientFactory,
    ISettingsRepository settings,
    ILogger<WebhookAlertNotifier> logger) : IAlertNotifier
{
    private const string SettingKey = "Alerts.WebhookUrl";

    public async Task NotifyAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        try
        {
            var webhookUrl = await settings.GetAsync<string>(SettingKey, cancellationToken);
            if (string.IsNullOrWhiteSpace(webhookUrl)) return;

            var payload = JsonSerializer.Serialize(new
            {
                text = $"*{title}*\n{message}",
            });

            using var client = httpClientFactory.CreateClient("webhook");
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(webhookUrl, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Webhook alert POST to {Url} returned {StatusCode}.",
                    webhookUrl,
                    (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to send webhook alert '{Title}'.", title);
        }
    }
}
