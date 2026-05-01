using System.Text.Json;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.API.Services;

/// <summary>
/// Manages the run lifecycle:
/// <list type="bullet">
///   <item>On startup: promotes any pending <c>ScheduledRun</c>s, then ensures an active run is open.</item>
///   <item>Background loop: every 60 s promotes <c>ScheduledRun</c>s whose <c>ActivatesAt</c> has passed.</item>
///   <item>On strategy-relevant settings change: closes the current run and opens a new one.</item>
/// </list>
/// </summary>
public sealed class RunLifecycleService(
    IServiceScopeFactory serviceScopeFactory,
    IActiveRunIdProvider activeRunIdProvider,
    ILogger<RunLifecycleService> logger)
    : IHostedService, IRunLifecycleManager
{
    private static readonly IReadOnlySet<string> StrategyPrefixes = new HashSet<string>(StringComparer.Ordinal)
    {
        "FleetExpansion.",
        "Trade.",
        "Contract.",
        "Scout.",
        "Navigation.",
        "Automation.",
        "Mining.",
        "Maintenance.",
        "Outfitting.",
        "Reliability.",
    };

    private readonly CancellationTokenSource _cts = new();
    private Task? _backgroundTask;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await InitialiseAsync(cancellationToken);
        _backgroundTask = RunPromotionLoopAsync(_cts.Token);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();
        if (_backgroundTask is not null)
        {
            try
            {
                await _backgroundTask;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        _cts.Dispose();
    }

    /// <inheritdoc />
    public async Task RotateForSettingsChangeAsync(string changedKey, CancellationToken cancellationToken = default)
    {
        if (!IsStrategyRelevant(changedKey))
        {
            return;
        }

        logger.LogInformation("Strategy-relevant setting '{Key}' changed — rotating run.", changedKey);

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var runRepo = scope.ServiceProvider.GetRequiredService<IRunRepository>();
        var agentRepo = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();

        var currentCredits = await GetCurrentCreditsAsync(agentRepo, cancellationToken);
        await CloseActiveRunAsync(runRepo, currentCredits, $"settings-change:{changedKey}", cancellationToken);
        await OpenNewRunAsync(runRepo, settingsRepo, currentCredits, $"settings change: {changedKey}", cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task RunPromotionLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PromoteTimedScheduledRunsAsync(stoppingToken);
        }
    }

    private async Task InitialiseAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var runRepo = scope.ServiceProvider.GetRequiredService<IRunRepository>();
        var agentRepo = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
        var settingsRepo = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();

        var now = TimeProvider.System.GetUtcNow();

        // 1. Promote any pending scheduled run (restart-triggered or time-triggered).
        var pending = await runRepo.GetPendingScheduledRunsAsync(now, cancellationToken);
        if (pending.Count > 0)
        {
            var toPromote = pending[0];
            logger.LogInformation(
                "Promoting ScheduledRun '{Name}' ({Id}) on startup.", toPromote.Name, toPromote.Id);

            var currentCredits = await GetCurrentCreditsAsync(agentRepo, cancellationToken);
            await CloseActiveRunAsync(runRepo, currentCredits, "scheduled-run-promotion", cancellationToken);

            var settingsJson = toPromote.ScheduledSettingsJson
                ?? await SnapshotSettingsAsync(settingsRepo, cancellationToken);

            var runCount = await runRepo.GetRunCountAsync(cancellationToken);
            var newRunId = await runRepo.OpenRunAsync(
                toPromote.Name, toPromote.StrategyLabel, currentCredits, settingsJson, cancellationToken);
            await runRepo.DeletePromotedScheduledRunAsync(toPromote.Id, cancellationToken);
            activeRunIdProvider.SetActiveRunId(newRunId);

            await runRepo.AppendCreditHighlightAsync(
                newRunId, currentCredits, 0, "run-start",
                $"Run {runCount + 1} started (promoted from ScheduledRun '{toPromote.Name}')",
                cancellationToken);

            logger.LogInformation(
                "Opened run '{Name}' ({RunId}) from ScheduledRun promotion.", toPromote.Name, newRunId);
            return;
        }

        // 2. Re-hydrate the active run ID after a restart (no scheduled run queued).
        var activeRun = await runRepo.GetActiveRunAsync(cancellationToken);
        if (activeRun is not null)
        {
            activeRunIdProvider.SetActiveRunId(activeRun.Id);
            logger.LogInformation(
                "Resumed existing active run '{Name}' ({RunId}).", activeRun.Name, activeRun.Id);
            return;
        }

        // 3. No active run — open a fresh one.
        var credits = await GetCurrentCreditsAsync(agentRepo, cancellationToken);
        await OpenNewRunAsync(runRepo, settingsRepo, credits, "startup", cancellationToken);
    }

    private async Task PromoteTimedScheduledRunsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = serviceScopeFactory.CreateAsyncScope();
            var runRepo = scope.ServiceProvider.GetRequiredService<IRunRepository>();
            var agentRepo = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
            var settingsRepo = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();

            var now = TimeProvider.System.GetUtcNow();
            var pending = await runRepo.GetPendingScheduledRunsAsync(now, cancellationToken);

            // Only promote timed runs from the background loop; restart-triggered ones are handled in StartAsync.
            var timed = pending.Where(r => !r.ActivatesOnNextRestart).ToList();
            if (timed.Count == 0)
            {
                return;
            }

            var toPromote = timed[0];
            logger.LogInformation(
                "Promoting timed ScheduledRun '{Name}' ({Id}).", toPromote.Name, toPromote.Id);

            var credits = await GetCurrentCreditsAsync(agentRepo, cancellationToken);
            await CloseActiveRunAsync(runRepo, credits, "scheduled-run-promotion", cancellationToken);

            var settingsJson = toPromote.ScheduledSettingsJson
                ?? await SnapshotSettingsAsync(settingsRepo, cancellationToken);

            var runCount = await runRepo.GetRunCountAsync(cancellationToken);
            var newRunId = await runRepo.OpenRunAsync(
                toPromote.Name, toPromote.StrategyLabel, credits, settingsJson, cancellationToken);
            await runRepo.DeletePromotedScheduledRunAsync(toPromote.Id, cancellationToken);
            activeRunIdProvider.SetActiveRunId(newRunId);

            await runRepo.AppendCreditHighlightAsync(
                newRunId, credits, 0, "run-start",
                $"Run {runCount + 1} started (promoted from ScheduledRun '{toPromote.Name}')",
                cancellationToken);

            logger.LogInformation(
                "Opened run '{Name}' ({RunId}) from timed ScheduledRun.", toPromote.Name, newRunId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error while promoting timed scheduled runs.");
        }
    }

    private async Task OpenNewRunAsync(
        IRunRepository runRepo,
        ISettingsRepository settingsRepo,
        long currentCredits,
        string reason,
        CancellationToken cancellationToken)
    {
        var settingsJson = await SnapshotSettingsAsync(settingsRepo, cancellationToken);
        var strategyLabel = DeriveStrategyLabel(settingsJson);
        var runCount = await runRepo.GetRunCountAsync(cancellationToken);
        var name = $"Run {runCount + 1}";

        var newRunId = await runRepo.OpenRunAsync(name, strategyLabel, currentCredits, settingsJson, cancellationToken);
        activeRunIdProvider.SetActiveRunId(newRunId);

        await runRepo.AppendCreditHighlightAsync(
            newRunId, currentCredits, 0, "run-start", $"{name} started ({reason})", cancellationToken);

        logger.LogInformation(
            "Opened run '{Name}' ({RunId}), strategy='{Strategy}', reason='{Reason}'.",
            name, newRunId, strategyLabel, reason);
    }

    private async Task CloseActiveRunAsync(
        IRunRepository runRepo,
        long currentCredits,
        string reason,
        CancellationToken cancellationToken)
    {
        var active = await runRepo.GetActiveRunAsync(cancellationToken);
        if (active is null)
        {
            return;
        }

        await runRepo.CloseRunAsync(active.Id, currentCredits, cancellationToken);
        var delta = currentCredits - active.StartingCredits;
        await runRepo.AppendCreditHighlightAsync(
            active.Id, currentCredits, delta, "run-end", $"Run ended ({reason})", cancellationToken);

        activeRunIdProvider.SetActiveRunId(null);
        logger.LogInformation(
            "Closed run '{Name}' ({RunId}), reason='{Reason}', ΔCredits={Delta}.",
            active.Name, active.Id, reason, delta);
    }

    private static async Task<long> GetCurrentCreditsAsync(
        IAgentRepository agentRepo,
        CancellationToken cancellationToken)
    {
        var agent = await agentRepo.GetAsync(cancellationToken);
        return agent?.Credits ?? 0L;
    }

    private static async Task<string> SnapshotSettingsAsync(
        ISettingsRepository settingsRepo,
        CancellationToken cancellationToken)
    {
        var all = await settingsRepo.GetAllAsync(cancellationToken);
        var dict = all.ToDictionary(s => s.Key, s => s.Value);
        return JsonSerializer.Serialize(dict);
    }

    private static string DeriveStrategyLabel(string settingsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            var root = doc.RootElement;

            var automationEnabled = root.TryGetProperty("Automation.Enabled", out var ae)
                && ae.GetString() == "true";
            if (!automationEnabled)
            {
                return "manual";
            }

            var parts = new List<string>();
            if (root.TryGetProperty("Automation.MiningShipPercentage", out var mp))
            {
                parts.Add($"mining-{mp.GetString()}");
            }

            if (root.TryGetProperty("FleetExpansion.PreferredShipType", out var st))
            {
                var shipType = st.GetString() ?? string.Empty;
                parts.Add(shipType.ToLowerInvariant().Replace("ship_", string.Empty, StringComparison.OrdinalIgnoreCase));
            }

            return parts.Count > 0 ? string.Join("|", parts) : "default";
        }
        catch
        {
            return "default";
        }
    }

    private static bool IsStrategyRelevant(string key)
    {
        foreach (var prefix in StrategyPrefixes)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
