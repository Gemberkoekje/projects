using System.Globalization;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Health;

/// <summary>
/// The same error repeats at most N times in 10 minutes (PLAN.md 3.2): no log statement logs a warning
/// or an error more than <c>Health.Errors.MaxRepeatsIn10Minutes</c> times (5) in ten minutes. The
/// subject is the statement: its source and message template.
/// </summary>
/// <remarks>
/// <para>
/// Warnings count too: this codebase logs "something is wrong, carrying on" at Warning, such as the
/// contract plan's missing deliverable, which repeated on every tick in the soak test (B31), or a ship
/// that can't find a route (slice 1.9). A healthy run logs each warning once (soak test, 1.14).
/// </para>
/// <para>
/// Journal lines stay out (<see cref="ErrorLog"/>), and so does what was logged before the monitor
/// started: startup has its own failure handling.
/// </para>
/// </remarks>
public sealed class RepeatingErrorRule(ErrorLog errors, ISettingsRepository settings) : IHealthRule
{
    /// <summary>The setting that holds how often one statement may log in ten minutes.</summary>
    public const string Setting = "Health.Errors.MaxRepeatsIn10Minutes";

    /// <summary>The number when the setting gives none.</summary>
    public const int DefaultRepeats = 5;

    private const int MaxSubjectLength = 120;
    private const int MaxMessageLength = 300;

    /// <inheritdoc />
    public string Name => "RepeatingError";

    /// <inheritdoc />
    public async Task<IReadOnlyList<HealthViolation>> EvaluateAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var limit = await settings.ThresholdAsync(Setting, DefaultRepeats, cancellationToken);
        var since = HealthCheckContext.Latest(context.Now - ErrorLog.Window, context.MonitorStartedAt);
        return
        [
            .. errors.Since(since)
                .Where(statement => statement.Count > limit)
                .Select(statement => new HealthViolation(
                    Shorten(statement.Statement, MaxSubjectLength),
                    string.Create(CultureInfo.InvariantCulture,
                        $"logged {statement.Count} warnings or errors in the last 10 minutes, the last at {statement.LastAt:u}: {Shorten(statement.LastMessage, MaxMessageLength)}; limit {limit} ({Setting})"))),
        ];
    }

    private static string Shorten(string text, int maxLength)
        => text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength - 3), "...");
}
