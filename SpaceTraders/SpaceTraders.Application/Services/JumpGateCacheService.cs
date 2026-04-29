using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Services;

public interface IJumpGateCacheService
{
    Task TryRefreshAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken);
}

public sealed class JumpGateCacheService(
    ISpaceTradersPort port,
    ISettingsRepository settings,
    ILogger<JumpGateCacheService> logger) : IJumpGateCacheService
{
    private const string KeyPrefix = "Navigation.JumpGateConnections.";

    public async Task TryRefreshAsync(string systemSymbol, string waypointSymbol, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(systemSymbol) || string.IsNullOrWhiteSpace(waypointSymbol))
        {
            return;
        }

        try
        {
            var connection = await port.GetJumpGateConnectionsAsync(systemSymbol, waypointSymbol, cancellationToken);
            var entry = new JumpGateCacheEntry(
                connection.WaypointSymbol,
                connection.ConnectedSystems,
                TimeProvider.System.GetUtcNow());

            await settings.SetAsync(KeyPrefix + waypointSymbol, entry, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Skipping jump-gate cache refresh for {Waypoint} in {System}.", waypointSymbol, systemSymbol);
        }
    }

    private sealed record JumpGateCacheEntry
    {
        public required string WaypointSymbol { get; init; }

        public required IReadOnlyList<string> ConnectedSystems { get; init; }

        public required DateTimeOffset ObservedAt { get; init; }

        [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
        public JumpGateCacheEntry(string waypointSymbol, IReadOnlyList<string> connectedSystems, DateTimeOffset observedAt)
        {
            WaypointSymbol = waypointSymbol;
            ConnectedSystems = connectedSystems;
            ObservedAt = observedAt;
        }
    }
}
