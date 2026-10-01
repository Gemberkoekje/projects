using System.Globalization;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Health;

/// <summary>
/// A ship with a goal changes state within N minutes, unless it's in transit (PLAN.md 3.2): a ship that
/// has work (a goal, or a contract assignment, whose plan is on) gets updated by the bot (its nav,
/// cargo, fuel or cooldown) at least every <c>Health.Ship.MaxMinutesWithoutChange</c> minutes (30).
/// The subject is the ship.
/// </summary>
/// <remarks>
/// <para>
/// "State" is everything the bot stores about the ship (its <c>LastSyncedAt</c>), not just the nav
/// state the metrics show: in the soak test (1.14) a contract drone stayed in orbit with the same
/// assignment for up to 69 minutes while it extracted every 71 seconds. A ship whose executor keeps
/// waiting, or whose step fails before it acts, isn't updated at all.
/// </para>
/// <para>
/// The clock starts no earlier than the ship's arrival and than the monitor saw its plan working
/// (automation and the plan on, API calls not paused), and the ship has to look stuck at two
/// evaluations in a row. A goal the circuit breaker blocked is <c>CircuitBreakerTripped</c>'s.
/// </para>
/// </remarks>
public sealed class ShipStuckRule(HealthFleet fleet, ISettingsRepository settings) : IHealthRule
{
    /// <summary>The setting that holds the minutes a working ship may go without an update.</summary>
    public const string Setting = "Health.Ship.MaxMinutesWithoutChange";

    /// <summary>The minutes when the setting gives none.</summary>
    public const int DefaultMinutes = 30;

    /// <inheritdoc />
    public string Name => "ShipStuck";

    /// <inheritdoc />
    public async Task<IReadOnlyList<HealthViolation>> EvaluateAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var ships = await fleet.LoadAsync(cancellationToken);
        var limit = TimeSpan.FromMinutes(await settings.ThresholdAsync(Setting, DefaultMinutes, cancellationToken));
        var violations = new List<HealthViolation>();
        foreach (var ship in ships)
        {
            var plans = ship.WorkPlans;
            if (plans.Count == 0 || ship.IsBlocked || ship.InTransitAt(context.Now))
            {
                continue;
            }

            var since = HealthCheckContext.Latest(
                ship.Ship.LastSyncedAt,
                ship.Ship.ArrivesAt ?? DateTimeOffset.MinValue,
                plans.Min(context.WorkingSince));
            var unchanged = context.Elapsed(since);

            // A ship that has just arrived from a long trip looks unchanged since it left until the
            // arrival handler docks it, moments later: the fleet is loaded with arrivals dead-reckoned,
            // which drops the arrival time. So it has to look stuck at two evaluations in a row.
            var stuckSince = context.HeldSince($"stuck:{ship.Symbol}", unchanged > limit);
            if (context.Elapsed(stuckSince) > TimeSpan.Zero)
            {
                violations.Add(new HealthViolation(
                    ship.Symbol,
                    string.Create(CultureInfo.InvariantCulture,
                        $"the bot hasn't updated the ship (nav, cargo, fuel or cooldown) for {unchanged.TotalMinutes:0} minutes, since {since:u}, though it has a {ship.Work}; it is {ship.Whereabouts}; limit {limit.TotalMinutes:0} minutes ({Setting})")));
            }
        }

        return violations;
    }
}
