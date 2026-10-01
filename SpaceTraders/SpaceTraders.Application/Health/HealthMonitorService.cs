using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Health;

/// <summary>
/// The bot checks itself (PLAN.md, phase 3): every minute it evaluates each <see cref="IHealthRule"/>
/// against its own state. A subject that breaks a rule is an anomaly: when it starts, the journal
/// logs <c>AnomalyRaised</c> (Warning, with <c>Rule</c>, <c>Subject</c> and <c>Details</c>) and
/// <c>spacetraders_anomaly_active{rule,subject}</c> turns 1; when the rule holds again, the journal
/// logs <c>AnomalyCleared</c> and the metric turns 0.
/// </summary>
/// <remarks>
/// <para>
/// Anomalies live in memory: after a restart the first evaluation raises the ones that are still
/// there again. A rule that throws is logged at Error; its anomalies stay as they were until it runs
/// again, and the other rules carry on.
/// </para>
/// <para>
/// It runs on every instance, like the size guard and the metrics: the rules read the shared database,
/// and an instance that isn't the leader has nothing of its own to report.
/// </para>
/// <para>
/// The database size limits aren't rules here: <see cref="DatabaseSizeGuardService"/> raises
/// <c>DbSizeSoftLimit</c> and <c>DbSizeHardLimit</c> itself, because the hard limit has to act before
/// the tick starts.
/// </para>
/// </remarks>
public sealed class HealthMonitorService(
    IServiceScopeFactory serviceScopeFactory,
    IAutomationMetrics metrics,
    IApiAvailabilityState apiAvailability,
    ILogger<HealthMonitorService> logger) : BackgroundService
{
    /// <summary>How often the rules are evaluated.</summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly HealthClock _clock = new();
    private readonly Dictionary<string, Dictionary<string, DateTimeOffset>> _active = new(StringComparer.Ordinal);
    private DateTimeOffset _startedAt = DateTimeOffset.MinValue;

    /// <summary>Evaluates every rule once, as of <paramref name="now"/>, and raises or clears what changed.</summary>
    /// <param name="now">When the evaluation runs.</param>
    /// <param name="cancellationToken">Stops the evaluation.</param>
    /// <returns>A task that completes when every rule has run.</returns>
    internal async Task EvaluateAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_startedAt == DateTimeOffset.MinValue)
        {
            _startedAt = now;
        }

        await using var scope = serviceScopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var plansOn = await PlansOnAsync(services.GetRequiredService<ISettingsRepository>(), cancellationToken);
        var context = new HealthCheckContext(now, _startedAt, plansOn, now < apiAvailability.PausedUntil, _clock);

        foreach (var rule in services.GetServices<IHealthRule>())
        {
            IReadOnlyList<HealthViolation> violations;
            try
            {
                violations = await rule.EvaluateAsync(context, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Health rule {Rule} failed; its anomalies stay as they are until it runs again.", rule.Name);
                continue;
            }

            Apply(rule.Name, violations, now);
        }

        _clock.EndEvaluation();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateAsync(TimeProvider.System.GetUtcNow(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Evaluating the health rules failed; the next evaluation is in {Interval}.", Interval);
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private static async Task<IReadOnlySet<AutomationPlan>> PlansOnAsync(ISettingsRepository settings, CancellationToken cancellationToken)
    {
        var plansOn = new HashSet<AutomationPlan>();
        if (!await settings.IsAutomationEnabledAsync(cancellationToken))
        {
            return plansOn;
        }

        foreach (var plan in Enum.GetValues<AutomationPlan>())
        {
            if (await settings.IsPlanEnabledAsync(plan, cancellationToken))
            {
                plansOn.Add(plan);
            }
        }

        return plansOn;
    }

    /// <summary>Raises the violations that are new and clears the anomalies whose subject no longer breaks the rule.</summary>
    private void Apply(string rule, IReadOnlyList<HealthViolation> violations, DateTimeOffset now)
    {
        if (!_active.TryGetValue(rule, out var active))
        {
            active = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
            _active[rule] = active;
        }

        var current = new Dictionary<string, HealthViolation>(StringComparer.Ordinal);
        foreach (var violation in violations)
        {
            current.TryAdd(violation.Subject, violation);
        }

        foreach (var (subject, violation) in current)
        {
            if (active.TryAdd(subject, now))
            {
                metrics.Anomaly(rule, subject, true);
                logger.LogWarning(
                    "{EventKind:l}: {Rule} on {Subject}: {Details:l}.",
                    JournalEvents.AnomalyRaised,
                    rule,
                    subject,
                    violation.Details);
            }
        }

        foreach (var (subject, since) in active.Where(anomaly => !current.ContainsKey(anomaly.Key)).ToList())
        {
            active.Remove(subject);
            metrics.Anomaly(rule, subject, false);
            logger.LogInformation(
                "{EventKind:l}: {Rule} on {Subject} ended after {ActiveMinutes} minutes.",
                JournalEvents.AnomalyCleared,
                rule,
                subject,
                (int)(now - since).TotalMinutes);
        }
    }
}
