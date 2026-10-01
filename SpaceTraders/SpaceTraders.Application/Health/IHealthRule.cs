namespace SpaceTraders.Application.Health;

/// <summary>
/// One intended behaviour of the bot, written down so that the bot can check itself (PLAN.md,
/// phase 3). <see cref="HealthMonitorService"/> evaluates every rule once a minute against the bot's
/// own state. Each subject that breaks a rule is an anomaly until it no longer does.
/// </summary>
/// <remarks>
/// Rules are scoped: the monitor resolves them from the scope of one evaluation, so they can use the
/// repositories. What a rule has to remember between evaluations it asks the
/// <see cref="HealthCheckContext"/> for.
/// </remarks>
public interface IHealthRule
{
    /// <summary>
    /// The rule's name, such as <c>ShipStuck</c>: the anomaly's <c>rule</c> label and the journal's
    /// <c>Rule</c>.
    /// </summary>
    string Name { get; }

    /// <summary>Returns every subject that breaks the rule now, with what is wrong; none when the rule holds.</summary>
    /// <param name="context">The time, the automation switches, and what the monitor saw before.</param>
    /// <param name="cancellationToken">Stops the evaluation.</param>
    /// <returns>The violations, at most one per subject.</returns>
    Task<IReadOnlyList<HealthViolation>> EvaluateAsync(HealthCheckContext context, CancellationToken cancellationToken);
}
