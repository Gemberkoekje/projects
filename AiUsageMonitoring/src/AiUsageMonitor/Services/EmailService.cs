using AiUsageMonitor.Events;
using AiUsageMonitor.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using Qowaiv;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AiUsageMonitor.Services;

public sealed class EmailService(IConfiguration configuration, ILogger<EmailService> logger) : IEmailService
{
    private readonly string _smtpHost = configuration["Smtp:Host"]
        ?? throw new InvalidOperationException("Smtp:Host not configured");

    private readonly int _smtpPort = configuration.GetValue("Smtp:Port", 587);
    private readonly string _smtpUser = configuration["Smtp:User"] ?? string.Empty;
    private readonly string _smtpPassword = configuration["Smtp:Password"] ?? string.Empty;
    private readonly string _fromAddress = configuration["Email:From"]
        ?? throw new InvalidOperationException("Email:From not configured");

    private readonly string _toAddress = configuration["Email:To"]
        ?? throw new InvalidOperationException("Email:To not configured");

    public async Task SendAlertAsync(
        IReadOnlyList<AlertFired> alerts,
        BurndownReport report,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Sending alert email for {Provider} with {Count} alert(s)",
            report.Provider,
            alerts.Count);

        var subject = $"AI Usage Alert - {report.Provider}";
        var body = BuildAlertHtml(alerts, report);
        await SendEmailAsync(subject, body, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendDailyReportAsync(
        BurndownReport copilotReport,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(Clock.UtcNow());
        logger.LogInformation("Sending daily report email for {Date}", today);

        var subject = $"AI Usage Daily Report — {today:yyyy-MM-dd}";
        var body = BuildDailyReportHtml(copilotReport, today);
        await SendEmailAsync(subject, body, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendEmailAsync(string subject, string htmlBody, CancellationToken cancellationToken)
    {
        using var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(_fromAddress));
        message.To.Add(MailboxAddress.Parse(_toAddress));
        message.Subject = subject;
        message.Body = new TextPart("html") { Text = htmlBody };

        using var client = new SmtpClient();
        await client.ConnectAsync(_smtpHost, _smtpPort, SecureSocketOptions.Auto, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(_smtpUser))
        {
            await client.AuthenticateAsync(_smtpUser, _smtpPassword, cancellationToken).ConfigureAwait(false);
        }

        await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildAlertHtml(IReadOnlyList<AlertFired> alerts, BurndownReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<html><body style=\"font-family: Arial, sans-serif; max-width: 700px; margin: auto; color: #333;\">");
        sb.AppendLine($"<h2 style=\"color: #c0392b;\">⚠️ AI Usage Alert — {report.Provider}</h2>");
        sb.AppendLine($"<p>The following alert(s) were triggered at <strong>{report.CalculatedAt:yyyy-MM-dd HH:mm} UTC</strong>:</p>");

        foreach (var alert in alerts)
        {
            sb.AppendLine("<div style=\"border: 1px solid #e74c3c; border-radius: 4px; padding: 12px; margin-bottom: 12px; background: #fdf3f2;\">");

            if (alert.Reason == AlertReasons.SpikeDetected)
            {
                var ratio = alert.AverageUsage > 0 ? alert.DeltaUsage / alert.AverageUsage : 0.0;
                sb.AppendLine("<h3 style=\"margin-top: 0; color: #c0392b;\">Spike Detected</h3>");
                sb.AppendLine(
                    $"<p>Consumption in the last interval was <strong>{alert.DeltaUsage:N0}</strong> tokens, " +
                    $"which is <strong>{ratio:F1}×</strong> your rolling average of <strong>{alert.AverageUsage:N0}</strong> " +
                    $"(threshold: {alert.ThresholdMultiplier:F1}×).</p>");
            }
            else if (alert.Reason == AlertReasons.ProjectionExceeded)
            {
                var projectedPercent = alert.Budget > 0 ? (double)alert.ProjectedUsage / alert.Budget * 100 : 0;
                sb.AppendLine("<h3 style=\"margin-top: 0; color: #c0392b;\">Projection Exceeded</h3>");
                sb.AppendLine(
                    $"<p>At current pace you are projected to use <strong>{alert.ProjectedUsage:N0}</strong> tokens " +
                    $"(<strong>{projectedPercent:F1}%</strong> of budget {alert.Budget:N0}) by end of period " +
                    $"(threshold: {alert.ProjectionThreshold:P0}).</p>");
            }

            sb.AppendLine("</div>");
        }

        sb.AppendLine("<h3>Current Status</h3>");
        sb.AppendLine("<table style=\"border-collapse: collapse; width: 100%;\">");
        sb.AppendLine(TableRow("Budget", $"{report.Budget:N0}"));
        sb.AppendLine(TableRow("Used", $"{report.ActualUsage:N0} ({report.PercentUsed:F1}%)"));
        sb.AppendLine(TableRow("Remaining", $"{report.RemainingBudget:N0}"));
        sb.AppendLine(TableRow("Days Remaining", $"{report.DaysRemaining}"));
        sb.AppendLine("</table>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    private static string BuildDailyReportHtml(BurndownReport copilot, DateOnly reportDate)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<html><body style=\"font-family: Arial, sans-serif; max-width: 800px; margin: auto; color: #333;\">");
        sb.AppendLine($"<h2>AI Usage Daily Report — {reportDate:yyyy-MM-dd}</h2>");

        AppendProviderSection(sb, copilot);

        sb.AppendLine("<h3>Summary</h3>");
        sb.AppendLine($"<p>{BuildSummaryText(copilot)}</p>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    private static void AppendProviderSection(StringBuilder sb, BurndownReport report)
    {
        var (statusColor, statusText) = GetTrafficLight(report);

        sb.AppendLine($"<h3>{report.Provider} <span style=\"color: {statusColor}; font-size: 0.85em;\">● {statusText}</span></h3>");
        sb.AppendLine(
            $"<p>Period: {report.PeriodStart:yyyy-MM-dd} – {report.PeriodEnd:yyyy-MM-dd} &nbsp;|&nbsp; " +
            $"Day {report.DaysElapsed} of {report.TotalDaysInPeriod}</p>");

        sb.AppendLine("<table style=\"border-collapse: collapse; width: 100%; margin-bottom: 16px;\">");
        sb.AppendLine(TableRow("Budget", $"{report.Budget:N0}"));
        sb.AppendLine(TableRow("Used", $"{report.ActualUsage:N0} ({report.PercentUsed:F1}%)"));
        sb.AppendLine(TableRow("Ideal (on pace)", $"{report.IdealUsage:N0}"));
        sb.AppendLine(TableRow("Remaining", $"{report.RemainingBudget:N0}"));
        sb.AppendLine(TableRow("Projected End-of-Period", $"{report.ProjectedUsage:N0}"));
        sb.AppendLine(TableRow("Days Remaining", $"{report.DaysRemaining}"));

        var deltaDisplay = report.DeltaSampleCount > 0 ? $"{report.CurrentDelta:N0}" : "no data yet";
        var avgDisplay = report.DeltaSampleCount > 0
            ? $"{report.RollingAverageDelta:N0} (over {report.DeltaSampleCount} sample(s))"
            : "no data yet";

        sb.AppendLine(TableRow("Current Interval Delta", deltaDisplay));
        sb.AppendLine(TableRow("Rolling Avg Delta", avgDisplay));
        sb.AppendLine("</table>");
    }

    private static string BuildSummaryText(BurndownReport report)
    {
        var (_, statusText) = GetTrafficLight(report);
        var paceWord = report.PercentOfIdeal switch
        {
            > 110 => "ahead of",
            < 90 => "behind",
            _ => "on",
        };

        return
            $"<strong>{report.Provider}:</strong> {statusText}. " +
            $"You have used {report.PercentUsed:F1}% of your monthly budget with {report.DaysRemaining} day(s) left. " +
            $"You are {paceWord} pace. " +
            $"At current usage you are projected to use {report.ProjectedUsage:N0} tokens by end of period.";
    }

    private static (string color, string text) GetTrafficLight(BurndownReport report)
    {
        var projectedRatio = report.Budget > 0 ? (double)report.ProjectedUsage / report.Budget : 0;

        return projectedRatio switch
        {
            > 1.10 => ("#c0392b", "Over budget"),
            > 0.90 => ("#f39c12", "At risk"),
            _ => ("#27ae60", "On track"),
        };
    }

    private static string TableRow(string label, string value) =>
        $"<tr>" +
        $"<td style=\"padding: 6px 12px; border: 1px solid #ddd; background: #f9f9f9;\"><strong>{label}</strong></td>" +
        $"<td style=\"padding: 6px 12px; border: 1px solid #ddd;\">{value}</td>" +
        $"</tr>";
}
