using System.Globalization;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Health;

/// <summary>
/// Credits change at least once in 24 hours while automation is enabled (PLAN.md 3.2), while the fleet
/// has work (D13): when ships have had a goal or a contract assignment of a plan that is on, at every
/// evaluation for <c>Health.Credits.MaxHoursUnchanged</c> hours (24), the credits changed in that
/// time. The subject is the agent.
/// </summary>
/// <remarks>
/// A fleet at work spends and earns: refuels, sales, purchases and contract payments all change the
/// credits, and each change is a credit sample (B7). An idle fleet is idle by design in the first run
/// (D9, D1), so it isn't expected to change them; <c>ShipLeftIdle</c> watches ships that are idle while
/// work waits. The clock is the monitor's own, so a restart starts it afresh.
/// </remarks>
public sealed class CreditsUnchangedRule(
    HealthFleet fleet,
    IAgentCreditsSampleRepository creditSamples,
    IAgentRepository agents,
    ISettingsRepository settings) : IHealthRule
{
    /// <summary>The setting that holds the hours credits may go unchanged while the fleet works.</summary>
    public const string Setting = "Health.Credits.MaxHoursUnchanged";

    /// <summary>The hours when the setting gives none.</summary>
    public const int DefaultHours = 24;

    /// <inheritdoc />
    public string Name => "CreditsUnchanged";

    /// <inheritdoc />
    public async Task<IReadOnlyList<HealthViolation>> EvaluateAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var ships = await fleet.LoadAsync(cancellationToken);
        var working = ships.Where(ship => ship.WorkPlans.Any(context.IsOn)).Select(ship => ship.Symbol).ToList();
        var workingSince = context.HeldSince("credits:fleet-working", working.Count > 0);

        var limit = TimeSpan.FromHours(await settings.ThresholdAsync(Setting, DefaultHours, cancellationToken));
        if (context.Elapsed(workingSince) <= limit)
        {
            return [];
        }

        var changes = await creditSamples.GetRangeAsync(context.Now - limit, context.Now, cancellationToken);
        if (changes.Count > 0)
        {
            return [];
        }

        var agent = await agents.GetAsync(cancellationToken);
        return
        [
            new HealthViolation(
                agent?.Symbol ?? "agent",
                string.Create(CultureInfo.InvariantCulture,
                    $"the credits ({agent?.Credits ?? 0:N0}) haven't changed for {limit.TotalHours:0} hours, while the fleet had work all that time (now {string.Join(", ", working)}); limit {limit.TotalHours:0} hours ({Setting})")),
        ];
    }
}
