using SpaceTraders.Application.Automation;

namespace SpaceTraders.Application.Health;

/// <summary>
/// What a health rule judges by besides the bot's state: the time, the automation switches, and what
/// the monitor saw in earlier evaluations. The monitor builds one per evaluation.
/// </summary>
/// <remarks>Not thread-safe: the monitor evaluates one rule at a time.</remarks>
public sealed class HealthCheckContext
{
    private readonly HealthClock _clock;
    private readonly IReadOnlySet<AutomationPlan> _plansOn;
    private readonly Dictionary<AutomationPlan, DateTimeOffset> _workingSince;

    /// <summary>Creates the context of one evaluation.</summary>
    /// <param name="now">When the evaluation runs.</param>
    /// <param name="monitorStartedAt">When the monitor's first evaluation ran.</param>
    /// <param name="plansOn">The plans that are switched on, with automation on; none while automation is off.</param>
    /// <param name="apiPaused">Whether API calls are paused after a 502, so no ship can act.</param>
    /// <param name="clock">The monitor's memory of earlier evaluations.</param>
    internal HealthCheckContext(
        DateTimeOffset now,
        DateTimeOffset monitorStartedAt,
        IReadOnlySet<AutomationPlan> plansOn,
        bool apiPaused,
        HealthClock clock)
    {
        Now = now;
        MonitorStartedAt = monitorStartedAt;
        _plansOn = plansOn;
        _clock = clock;
        _workingSince = Enum.GetValues<AutomationPlan>().ToDictionary(
            plan => plan,
            plan => clock.HeldSince($"working:{plan}", plansOn.Contains(plan) && !apiPaused, now));
    }

    /// <summary>When this evaluation runs.</summary>
    public DateTimeOffset Now { get; }

    /// <summary>
    /// When the monitor's first evaluation ran, at the end of startup. What happened before belongs to
    /// startup, such as the old tokens agent bootstrap tries on purpose.
    /// </summary>
    public DateTimeOffset MonitorStartedAt { get; }

    /// <summary>Whether automation and <paramref name="plan"/> are both switched on.</summary>
    /// <param name="plan">The plan.</param>
    /// <returns><c>true</c> when the plan may work.</returns>
    public bool IsOn(AutomationPlan plan) => _plansOn.Contains(plan);

    /// <summary>
    /// Since when the bot has been able to work on <paramref name="plan"/> without a break: automation
    /// and the plan switched on, and API calls not paused after a 502, at every evaluation since (at
    /// most since the monitor started). <see cref="DateTimeOffset.MaxValue"/> while it can't. A rule
    /// that expects something to happen within a time starts counting here at the earliest, so a ship
    /// that waited while automation was off doesn't look stuck once it is back on.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <returns>The start of the current working spell.</returns>
    public DateTimeOffset WorkingSince(AutomationPlan plan) => _workingSince[plan];

    /// <summary>
    /// Since when <paramref name="condition"/> has held under <paramref name="key"/>, at every
    /// evaluation since (at most since the monitor started); <see cref="DateTimeOffset.MaxValue"/>
    /// when it doesn't hold now. A key that no rule asks about in an evaluation is forgotten.
    /// </summary>
    /// <param name="key">What the condition is, unique across rules, such as <c>idle:SHIP-1</c>.</param>
    /// <param name="condition">Whether it holds now.</param>
    /// <returns>The start of the current spell.</returns>
    public DateTimeOffset HeldSince(string key, bool condition) => _clock.HeldSince(key, condition, Now);

    /// <summary>How long ago <paramref name="since"/> was; zero when it lies ahead, such as <see cref="DateTimeOffset.MaxValue"/>.</summary>
    /// <param name="since">The moment.</param>
    /// <returns>The time from <paramref name="since"/> to <see cref="Now"/>.</returns>
    public TimeSpan Elapsed(DateTimeOffset since) => since >= Now ? TimeSpan.Zero : Now - since;

    /// <summary>The latest of <paramref name="moments"/>: where a clock that waits for all of them starts.</summary>
    /// <param name="moments">The moments.</param>
    /// <returns>The latest one.</returns>
    public static DateTimeOffset Latest(params ReadOnlySpan<DateTimeOffset> moments)
    {
        var latest = DateTimeOffset.MinValue;
        foreach (var moment in moments)
        {
            if (moment > latest)
            {
                latest = moment;
            }
        }

        return latest;
    }
}

/// <summary>
/// The monitor's memory between evaluations: per key, since when a condition has held at every
/// evaluation.
/// </summary>
/// <remarks>Not thread-safe: the monitor evaluates one rule at a time.</remarks>
internal sealed class HealthClock
{
    private readonly Dictionary<string, DateTimeOffset> _since = new(StringComparer.Ordinal);
    private readonly HashSet<string> _asked = new(StringComparer.Ordinal);

    /// <summary>Records whether <paramref name="condition"/> holds at <paramref name="now"/>, and returns since when it has.</summary>
    /// <param name="key">What the condition is.</param>
    /// <param name="condition">Whether it holds now.</param>
    /// <param name="now">When the evaluation runs.</param>
    /// <returns>The start of the current spell, or <see cref="DateTimeOffset.MaxValue"/> when it doesn't hold.</returns>
    public DateTimeOffset HeldSince(string key, bool condition, DateTimeOffset now)
    {
        _asked.Add(key);
        if (!condition)
        {
            _since.Remove(key);
            return DateTimeOffset.MaxValue;
        }

        if (!_since.TryGetValue(key, out var since))
        {
            since = now;
            _since[key] = since;
        }

        return since;
    }

    /// <summary>Forgets every key that wasn't asked about since the previous call: their subjects are gone.</summary>
    public void EndEvaluation()
    {
        foreach (var key in _since.Keys.Where(key => !_asked.Contains(key)).ToList())
        {
            _since.Remove(key);
        }

        _asked.Clear();
    }
}
