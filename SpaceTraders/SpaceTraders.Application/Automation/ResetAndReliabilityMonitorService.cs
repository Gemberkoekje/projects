using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Automation;

public sealed class ResetAndReliabilityMonitorService(
    IServiceScopeFactory serviceScopeFactory,
    ILeaderElection leaderElection,
    ILogger<ResetAndReliabilityMonitorService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ResetWarningThreshold = TimeSpan.FromHours(2);
    private static readonly TimeSpan DivergenceShipStaleThreshold = TimeSpan.FromMinutes(15);
    private const int DivergenceSampleSize = 5;

    private bool _resetWarningSent;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in ResetAndReliabilityMonitorService tick.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    internal async Task TickAsync(CancellationToken cancellationToken)
    {
        if (!leaderElection.IsLeader)
        {
            logger.LogTrace("ResetAndReliabilityMonitorService: not the leader; skipping tick.");
            return;
        }

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        var port = scope.ServiceProvider.GetRequiredService<ISpaceTradersPort>();
        var ships = scope.ServiceProvider.GetRequiredService<IShipRepository>();
        var contracts = scope.ServiceProvider.GetRequiredService<IContractRepository>();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        try
        {
            await CheckServerResetAsync(settings, port, bus, cancellationToken);
            await settings.SetAsync("Runtime.ApiUnavailable", "false", cancellationToken);
            await settings.SetAsync("Runtime.Alert.ApiUnavailable", "false", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await settings.SetAsync("Runtime.ApiUnavailable", "true", cancellationToken);
            await settings.SetAsync("Runtime.Alert.ApiUnavailable", "true", cancellationToken);
            logger.LogWarning(ex, "Reset monitor failed to retrieve server status.");
        }

        await CheckDivergenceAsync(settings, ships, port, bus, cancellationToken);
        await CheckContractDeadlineAlertSignalAsync(settings, contracts, cancellationToken);
    }

    private async Task CheckServerResetAsync(
        ISettingsRepository settings,
        ISpaceTradersPort port,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var status = await port.GetStatusAsync(cancellationToken);
        var now = TimeProvider.System.GetUtcNow();

        await settings.SetAsync("Runtime.Reset.Next", status.NextResetAt?.ToString("O") ?? string.Empty, cancellationToken);

        if (!status.NextResetAt.HasValue)
        {
            _resetWarningSent = false;
            await settings.SetAsync("Runtime.Reset.Warning", "false", cancellationToken);
            await settings.SetAsync("Runtime.Alert.ResetUpcoming", "false", cancellationToken);
            return;
        }

        var remaining = status.NextResetAt.Value - now;

        if (remaining <= ResetWarningThreshold && remaining > TimeSpan.Zero && !_resetWarningSent)
        {
            _resetWarningSent = true;
            await settings.SetAsync("Runtime.Reset.Warning", "true", cancellationToken);
            await settings.SetAsync("Runtime.Alert.ResetUpcoming", "true", cancellationToken);
            await bus.PublishAsync(new ServerResetWarningEvent(status.NextResetAt.Value, remaining));
            logger.LogWarning("Server reset warning: reset in {Remaining}.", remaining);
        }
        else if (remaining > ResetWarningThreshold)
        {
            _resetWarningSent = false;
            await settings.SetAsync("Runtime.Reset.Warning", "false", cancellationToken);
            await settings.SetAsync("Runtime.Alert.ResetUpcoming", "false", cancellationToken);
        }

        var autoPause = await settings.GetAsync<bool>("Reliability.PauseAutomationBeforeReset", cancellationToken);
        var automationEnabled = await settings.GetAsync<bool>("Automation.Enabled", cancellationToken);

        if (autoPause == true && remaining <= TimeSpan.FromMinutes(10) && remaining > TimeSpan.Zero && automationEnabled == true)
        {
            await settings.SetAsync("Automation.Enabled", "false", cancellationToken);
            await settings.SetAsync("Runtime.AutomationPausedByReset", "true", cancellationToken);
            await settings.SetAsync("Runtime.Alert.AutomationDisabled", "true", cancellationToken);
            await bus.PublishAsync(new AutomationPausedForServerResetEvent(status.NextResetAt.Value, remaining));
            logger.LogWarning("Automation paused due to upcoming reset in {Remaining}.", remaining);
        }

        var pausedByReset = await settings.GetAsync<bool>("Runtime.AutomationPausedByReset", cancellationToken);
        if (pausedByReset == true && remaining < TimeSpan.Zero)
        {
            await settings.SetAsync("Automation.Enabled", "true", cancellationToken);
            await settings.SetAsync("Runtime.AutomationPausedByReset", "false", cancellationToken);
            await settings.SetAsync("Runtime.Alert.AutomationDisabled", "false", cancellationToken);
            await settings.SetAsync("Runtime.Alert.ResetUpcoming", "false", cancellationToken);
            await bus.PublishAsync(new AutomationResumedAfterServerResetEvent(now));
            logger.LogInformation("Automation resumed after server reset boundary passed.");
        }
    }

    private async Task CheckDivergenceAsync(
        ISettingsRepository settings,
        IShipRepository ships,
        ISpaceTradersPort port,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var fleet = await ships.GetAllAsync(cancellationToken);
        var now = TimeProvider.System.GetUtcNow();

        var staleCached = fleet.Where(s => now - s.LastSyncedAt > DivergenceShipStaleThreshold).ToList();

        var remoteShips = await LoadRemoteShipsAsync(port, cancellationToken);
        var remoteBySymbol = remoteShips.ToDictionary(s => s.Symbol, StringComparer.OrdinalIgnoreCase);

        var sample = fleet
            .OrderBy(s => s.Symbol, StringComparer.Ordinal)
            .Take(DivergenceSampleSize)
            .ToList();

        var apiMismatches = new List<string>();
        foreach (var cached in sample)
        {
            if (!remoteBySymbol.TryGetValue(cached.Symbol, out var remote))
            {
                apiMismatches.Add($"{cached.Symbol}: missing in API");
                continue;
            }

            if (!string.Equals(cached.Status ?? string.Empty, remote.Status ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(cached.WaypointSymbol ?? string.Empty, remote.WaypointSymbol ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(cached.DestWaypointSymbol ?? string.Empty, remote.DestWaypointSymbol ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                apiMismatches.Add($"{cached.Symbol}: cache nav differs from API");
            }
        }

        var mismatchCount = staleCached.Count + apiMismatches.Count;
        if (mismatchCount == 0)
        {
            await settings.SetAsync("Runtime.CacheDivergenceDetected", "false", cancellationToken);
            await settings.SetAsync("Runtime.Alert.CacheDivergence", "false", cancellationToken);
            return;
        }

        var summary = $"{mismatchCount} divergence signal(s): stale={staleCached.Count}, sampled-api={apiMismatches.Count}. {string.Join("; ", apiMismatches.Take(3))}";

        await settings.SetAsync("Runtime.CacheDivergenceDetected", "true", cancellationToken);
        await settings.SetAsync("Runtime.Alert.CacheDivergence", "true", cancellationToken);
        await bus.PublishAsync(new CacheDivergenceDetectedEvent(mismatchCount, summary));
        logger.LogWarning("Cache divergence detected: {Summary}", summary);
    }

    private static async Task<IReadOnlyList<ShipModel>> LoadRemoteShipsAsync(ISpaceTradersPort port, CancellationToken cancellationToken)
    {
        var page = 1;
        var ships = new List<ShipModel>();

        while (true)
        {
            var response = await port.GetMyShipsAsync(page, 20, cancellationToken);
            ships.AddRange(response.Items);

            if (ships.Count >= response.Total || response.Items.Count == 0)
            {
                break;
            }

            page++;
        }

        return ships;
    }

    private static async Task CheckContractDeadlineAlertSignalAsync(
        ISettingsRepository settings,
        IContractRepository contracts,
        CancellationToken cancellationToken)
    {
        var active = await contracts.GetActiveAsync(cancellationToken);
        var now = TimeProvider.System.GetUtcNow();

        var criticalExists = active
            .Where(c => c.IsAccepted && !c.IsFulfilled)
            .Select(c => c.TermsDeadline ?? c.Expiration)
            .Where(d => d.HasValue)
            .Any(d => d.Value > now && d.Value - now <= TimeSpan.FromHours(6));

        await settings.SetAsync("Runtime.Alert.ContractDeadlinesApproaching", criticalExists ? "true" : "false", cancellationToken);
    }
}
