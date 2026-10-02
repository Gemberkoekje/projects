using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;

namespace SpaceTraders.Application.Automation;

/// <summary>Keeps the prices fresh where the fleet is (PLAN.md slice 6.5).</summary>
public interface IMarketWatchService
{
    /// <summary>
    /// Fetches the market that has waited longest for a refresh, among those where one of our ships
    /// is, when it is due: one market per call.
    /// </summary>
    /// <param name="cancellationToken">Stops the refresh.</param>
    /// <returns>A task that completes when the market has been fetched, or none was due.</returns>
    Task RefreshDueMarketAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Every market that has one of our ships at its waypoint (docked or in orbit) is fetched again once
/// <c>Market.RefreshMinutes</c> (5) have passed since its prices were last seen, by this watch or by an
/// arrival. The API shows a market's prices only while a ship is there, so a probe parked at a market
/// keeps it current, and so does a trader waiting at one. Each refresh publishes
/// <see cref="MarketDataRefreshedEvent"/>, like an arrival's: the price history records it.
/// </summary>
/// <remarks>
/// A refresh is a read, which loses nothing by going a little later (D19): the tick runs the watch
/// last, after its moves and trades, and one market per tick, the one that has waited longest, so the
/// watch never holds the fleet up. Twelve ticks a minute refresh 25 markets in about two minutes, well
/// within the 5. The tick runs it only on the leader, with automation on and the API not paused.
/// </remarks>
public sealed class MarketWatchService(
    IShipRepository ships,
    IWaypointRepository waypoints,
    IMarketRepository markets,
    IMarketRefresher refresher,
    ISettingsRepository settings,
    MarketWatchAttempts attempts,
    ILogger<MarketWatchService> logger) : IMarketWatchService
{
    /// <summary>The setting that holds the minutes between refreshes of one market; 0 switches the watch off.</summary>
    public const string RefreshMinutesSetting = "Market.RefreshMinutes";

    /// <inheritdoc />
    public async Task RefreshDueMarketAsync(CancellationToken cancellationToken)
    {
        var minutes = await settings.GetAsync<int>(RefreshMinutesSetting, cancellationToken);
        if (minutes <= 0)
        {
            return;
        }

        var interval = TimeSpan.FromMinutes(minutes);
        var now = TimeProvider.System.GetUtcNow();
        var shipsAtWaypoints = (await ships.GetAllAsync(cancellationToken))
            .Where(ship => ship.LocalStatus != ShipLocalStatus.InTransit
                && !string.IsNullOrWhiteSpace(ship.WaypointSymbol)
                && !string.IsNullOrWhiteSpace(ship.SystemSymbol))
            .GroupBy(ship => ship.WaypointSymbol!, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First());

        var due = new List<(ShipModel Ship, DateTimeOffset LastAsked)>();
        foreach (var ship in shipsAtWaypoints)
        {
            var waypointSymbol = ship.WaypointSymbol!;
            if ((await waypoints.FindAsync(waypointSymbol, cancellationToken))?.HasMarket != true)
            {
                continue;
            }

            var lastSeen = await markets.GetLastObservedAtAsync(waypointSymbol, cancellationToken) ?? DateTimeOffset.MinValue;
            var lastAsked = Max(lastSeen, attempts.LastAttempt(waypointSymbol));
            if (now - lastAsked >= interval)
            {
                due.Add((ship, lastAsked));
            }
        }

        if (due.Count == 0)
        {
            return;
        }

        var (next, _) = due
            .OrderBy(market => market.LastAsked)
            .ThenBy(market => market.Ship.WaypointSymbol, StringComparer.Ordinal)
            .First();

        // A market that fails, or comes back without prices, waits an interval like one that was
        // refreshed: otherwise it would stay the most overdue and be asked for on every tick.
        attempts.Record(next.WaypointSymbol!, now);
        try
        {
            await RefreshAsync(next.SystemSymbol!, next.WaypointSymbol!, next.Symbol, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "Market watch: couldn't refresh the market at {WaypointSymbol}, where ship {ShipSymbol} is; trying again in {Minutes} minutes.",
                next.WaypointSymbol,
                next.Symbol,
                minutes);
        }
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private async Task RefreshAsync(string systemSymbol, string waypointSymbol, string shipSymbol, CancellationToken cancellationToken)
    {
        // Without a ship of ours there the API leaves the prices out, and the refresher keeps the cached ones.
        if (!await refresher.RefreshAsync(systemSymbol, waypointSymbol, cancellationToken))
        {
            logger.LogDebug(
                "Market watch: the market at {WaypointSymbol} came back without prices, though ship {ShipSymbol} should be there; kept the cached prices.",
                waypointSymbol,
                shipSymbol);
            return;
        }

        logger.LogDebug(
            "Market watch: refreshed the market at {WaypointSymbol}, where ship {ShipSymbol} is.",
            waypointSymbol,
            shipSymbol);
    }
}

/// <summary>
/// When the market watch last asked for each market, in memory: a singleton, so it lasts from tick to
/// tick. After a restart the watch may ask once more for a market that failed.
/// </summary>
public sealed class MarketWatchAttempts
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastAttempts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When the watch last asked for the market at <paramref name="waypointSymbol"/>.</summary>
    /// <param name="waypointSymbol">The market's waypoint.</param>
    /// <returns>The time, or <see cref="DateTimeOffset.MinValue"/> when it hasn't asked yet.</returns>
    public DateTimeOffset LastAttempt(string waypointSymbol)
        => _lastAttempts.TryGetValue(waypointSymbol, out var at) ? at : DateTimeOffset.MinValue;

    /// <summary>Records that the watch asks for the market at <paramref name="waypointSymbol"/> now.</summary>
    /// <param name="waypointSymbol">The market's waypoint.</param>
    /// <param name="at">The time of the attempt.</param>
    public void Record(string waypointSymbol, DateTimeOffset at) => _lastAttempts[waypointSymbol] = at;
}
