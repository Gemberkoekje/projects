using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Construction;

/// <summary>
/// What the construction plan and its trips know of the construction sites (PLAN.md slice 6.6): the cache
/// (<c>cached_construction_sites</c>), fetched from the API when a jump gate is first seen under construction and every
/// <see cref="ConstructionSiteWatch.Interval"/> while it needs materials (other agents in the system may supply it too), and
/// stored from every supply's answer. Only the jump gate of the home system, where the headquarters are, counts (D68:
/// "Only the home base jump gate construction should be high priority, any other jump gate construction should be low
/// priority or maybe not even considered at all"): a gate elsewhere is never fetched, built or given a role.
/// </summary>
public interface IConstructionSites
{
    /// <summary>The home system: the system of the agent's headquarters; empty before the agent is cached.</summary>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The system, such as <c>X1-DC53</c>.</returns>
    Task<string> HomeSystemAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The home system's jump gate while it needs materials, fetching it when due. A gate the waypoint cache doesn't list
    /// as under construction, and that isn't cached, costs no call: startup sync stores the flag when a system is first
    /// seen, and a gate is only ever built after that.
    /// </summary>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The sites that need materials: the home gate, or none.</returns>
    Task<IReadOnlyList<ConstructionSiteModel>> NeedingMaterialsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The home system's jump gate while it needs materials, as cached, without a call: for the role board and the trading
    /// plan, which read what the construction plan fetched.
    /// </summary>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>The sites that need materials: the home gate, or none.</returns>
    Task<IReadOnlyList<ConstructionSiteModel>> CachedNeedingMaterialsAsync(CancellationToken cancellationToken);

    /// <summary>The site as cached; null when it isn't.</summary>
    /// <param name="waypointSymbol">The site.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The site, or null.</returns>
    Task<ConstructionSiteModel?> FindAsync(string waypointSymbol, CancellationToken cancellationToken);

    /// <summary>Fetches the site from the API now, and stores it; a failure is logged at Warning.</summary>
    /// <param name="systemSymbol">The site's system.</param>
    /// <param name="waypointSymbol">The site.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The site, or null when the fetch failed.</returns>
    Task<ConstructionSiteModel?> FetchAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken);

    /// <summary>
    /// Stores a site as the API answered with it (a fetch, or a supply), and journals a change: <c>PlanStarted</c> when the
    /// plan first sees it need materials, <c>PlanCompleted</c> when a site it saw under construction is complete.
    /// </summary>
    /// <param name="site">The site.</param>
    /// <param name="systemSymbol">Its system.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>A task that completes once the site is stored.</returns>
    Task RecordAsync(ConstructionSiteModel site, string systemSymbol, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class ConstructionSites(
    IConstructionRepository constructions,
    IWaypointRepository waypoints,
    IAgentRepository agents,
    ISpaceTradersPort port,
    ConstructionSiteWatch watch,
    ILogger<ConstructionSites> logger) : IConstructionSites
{
    /// <inheritdoc />
    public async Task<string> HomeSystemAsync(CancellationToken cancellationToken)
        => await agents.GetAsync(cancellationToken) is { HeadquartersSymbol: { Length: > 0 } headquarters }
            ? ConstructionPlanner.SystemOf(headquarters)
            : string.Empty;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConstructionSiteModel>> CachedNeedingMaterialsAsync(CancellationToken cancellationToken)
    {
        var home = await HomeSystemAsync(cancellationToken);
        return home.Length == 0
            ? []
            : [.. (await constructions.GetIncompleteAsync(cancellationToken))
                .Where(site => ConstructionPlanner.SystemOf(site.WaypointSymbol).Equals(home, StringComparison.OrdinalIgnoreCase)
                    && ConstructionPlanner.NeedsMaterials(site))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConstructionSiteModel>> NeedingMaterialsAsync(CancellationToken cancellationToken)
    {
        var systemSymbol = await HomeSystemAsync(cancellationToken);
        var now = TimeProvider.System.GetUtcNow();
        var sites = new List<ConstructionSiteModel>();
        if (systemSymbol.Length == 0)
        {
            return sites;
        }

        foreach (var gate in (await waypoints.GetBySystemAsync(systemSymbol, cancellationToken))
            .Where(waypoint => waypoint.Type.Equals(ConstructionPlanner.JumpGateType, StringComparison.OrdinalIgnoreCase))
            .OrderBy(waypoint => waypoint.Symbol, StringComparer.Ordinal))
        {
            var site = await constructions.FindAsync(gate.Symbol, cancellationToken);
            if (site is { IsComplete: true } || (site is null && !gate.IsUnderConstruction))
            {
                continue;
            }

            // Due when never fetched in this process, or 10 minutes since; a fetch that failed waits as long.
            if (watch.IsDue(gate.Symbol, now))
            {
                site = await FetchAsync(systemSymbol, gate.Symbol, cancellationToken) ?? site;
            }

            if (site is not null && ConstructionPlanner.NeedsMaterials(site))
            {
                sites.Add(site);
            }
        }

        return sites;
    }

    /// <inheritdoc />
    public Task<ConstructionSiteModel?> FindAsync(string waypointSymbol, CancellationToken cancellationToken)
        => constructions.FindAsync(waypointSymbol, cancellationToken);

    /// <inheritdoc />
    public async Task<ConstructionSiteModel?> FetchAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken)
    {
        watch.Attempted(waypointSymbol, TimeProvider.System.GetUtcNow());
        ConstructionSiteModel site;
        try
        {
            site = await port.GetConstructionSiteAsync(systemSymbol, waypointSymbol, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "Couldn't fetch construction site {WaypointSymbol}; tried again in {Minutes} minutes.",
                waypointSymbol,
                ConstructionSiteWatch.Interval.TotalMinutes);
            return null;
        }

        await RecordAsync(site, systemSymbol, cancellationToken);
        return site;
    }

    /// <inheritdoc />
    public Task RecordAsync(ConstructionSiteModel site, string systemSymbol, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(site);
        return RecordSiteAsync(site, systemSymbol, cancellationToken);
    }

    private async Task RecordSiteAsync(ConstructionSiteModel site, string systemSymbol, CancellationToken cancellationToken)
    {
        var before = await constructions.FindAsync(site.WaypointSymbol, cancellationToken);
        await constructions.UpsertAsync(site, systemSymbol, cancellationToken);
        watch.Attempted(site.WaypointSymbol, TimeProvider.System.GetUtcNow());

        if (before is null && ConstructionPlanner.NeedsMaterials(site))
        {
            logger.LogInformation(
                "{EventKind:l}: {Plan} plan for {WaypointSymbol}: it needs {Materials}.",
                JournalEvents.PlanStarted,
                AutomationPlan.Construction,
                site.WaypointSymbol,
                Describe(site));
        }
        else if (site.IsComplete && before is { IsComplete: false })
        {
            logger.LogInformation(
                "{EventKind:l}: {Plan} plan for {WaypointSymbol}: the construction is complete.",
                JournalEvents.PlanCompleted,
                AutomationPlan.Construction,
                site.WaypointSymbol);
        }
    }

    /// <summary>The site's materials, as <c>FAB_MATS 200/1600, ADVANCED_CIRCUITRY 0/400</c>.</summary>
    private static string Describe(ConstructionSiteModel site)
        => string.Join(", ", site.Materials.Select(material => $"{material.TradeSymbol} {material.Fulfilled}/{material.Required}"));
}

/// <summary>
/// When each construction site was last fetched or stored, in memory (PLAN.md slice 6.6): a site that needs materials is
/// fetched again after <see cref="Interval"/>, as other agents may supply it; a fetch that failed waits as long, so it can't
/// cost a call on every tick. A restart forgets it, and the first pass after it fetches each site once.
/// </summary>
/// <remarks>A singleton; thread-safe.</remarks>
public sealed class ConstructionSiteWatch
{
    /// <summary>How long a site's materials, as last seen, count before it is fetched again.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _attemptedAt = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the site is due to be fetched: never in this process, or at least <see cref="Interval"/> ago.</summary>
    /// <param name="waypointSymbol">The site.</param>
    /// <param name="now">The time.</param>
    /// <returns>True when it is due.</returns>
    public bool IsDue(string waypointSymbol, DateTimeOffset now)
    {
        lock (_gate)
        {
            return !_attemptedAt.TryGetValue(waypointSymbol, out var at) || now - at >= Interval;
        }
    }

    /// <summary>Records a fetch, or a site stored from a supply's answer.</summary>
    /// <param name="waypointSymbol">The site.</param>
    /// <param name="now">When.</param>
    public void Attempted(string waypointSymbol, DateTimeOffset now)
    {
        lock (_gate)
        {
            _attemptedAt[waypointSymbol] = now;
        }
    }
}
