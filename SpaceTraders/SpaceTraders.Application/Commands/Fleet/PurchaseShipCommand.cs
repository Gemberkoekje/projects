using System.Text.Json;
using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Sync;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Orchestration;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using SpaceTraders.Domain.Events;
using Wolverine;

namespace SpaceTraders.Application.Commands.Fleet;

public sealed record PurchaseShipCommand
{
    public required string ShipType { get; init; }

    public required string ShipyardWaypoint { get; init; }

    public string TargetAssignmentType { get; init; } = string.Empty;

    public string TargetOriginWaypoint { get; init; } = string.Empty;

    /// <summary>
    /// When set, the handler will emit an <see cref="AssignShipToGoalCommand"/> with this goal
    /// for the newly-purchased ship immediately after purchase.
    /// </summary>
    public FleetGoal? TargetGoal { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public PurchaseShipCommand(
        string ShipType,
        string ShipyardWaypoint,
        string TargetAssignmentType = "",
        string TargetOriginWaypoint = "",
        FleetGoal? TargetGoal = null)
    {
        this.ShipType = ShipType;
        this.ShipyardWaypoint = ShipyardWaypoint;
        this.TargetAssignmentType = TargetAssignmentType;
        this.TargetOriginWaypoint = TargetOriginWaypoint;
        this.TargetGoal = TargetGoal;
    }
}

public sealed class PurchaseShipHandler(
    ISpaceTradersPort port,
    IAgentRepository agents,
    IShipRepository ships,
    ISettingsRepository settings,
    IMessageBus bus,
    ILogger<PurchaseShipHandler> logger)
{
    private sealed class ShipyardShipPrice
    {
        public string Type { get; init; } = string.Empty;

        public long PurchasePrice { get; init; }
    }

    private const long MinimumCreditReserve = 100_000;

    public async Task Handle(PurchaseShipCommand command, CancellationToken cancellationToken)
    {
        var systemSymbol = ExtractSystemSymbol(command.ShipyardWaypoint);
        await bus.SendAsync(new RefreshShipyardDataCommand(systemSymbol, command.ShipyardWaypoint, ForceRefresh: true));

        var agent = await agents.GetAsync(cancellationToken);
        if (agent is null)
        {
            logger.LogWarning("Skipping ship purchase for {ShipType} at {Shipyard}: agent not available.", command.ShipType, command.ShipyardWaypoint);
            return;
        }

        var configuredReserve = await settings.GetAsync<long>("FleetExpansion.MinCreditReserve", cancellationToken);
        var reserve = Math.Max(MinimumCreditReserve, configuredReserve);

        var shipyard = await port.GetShipyardAsync(systemSymbol, command.ShipyardWaypoint, cancellationToken);
        var estimatedCost = ResolveShipPurchasePrice(shipyard.ShipsDetailJson, command.ShipType);
        if (estimatedCost <= 0)
        {
            logger.LogWarning(
                "Skipping ship purchase for {ShipType} at {Shipyard}: unable to determine purchase price.",
                command.ShipType,
                command.ShipyardWaypoint);
            return;
        }

        if (agent.Credits - estimatedCost < reserve)
        {
            logger.LogInformation(
                "Skipping ship purchase for {ShipType}: credits {Credits} minus cost {Cost} would breach reserve {Reserve}.",
                command.ShipType,
                agent.Credits,
                estimatedCost,
                reserve);
            return;
        }

        var result = await port.PurchaseShipAsync(command.ShipType, command.ShipyardWaypoint, cancellationToken);

        await agents.UpsertAsync(result.Agent, cancellationToken);

        var newShip = new ShipModel(
            result.ShipSymbol,
            result.ShipNav.SystemSymbol,
            result.ShipNav.WaypointSymbol,
            result.ShipNav.Status,
            result.ShipNav.FlightMode,
            result.ShipFuel.Current,
            result.ShipFuel.Capacity,
            result.ShipNav.ArrivesAt,
            result.ShipNav.DestWaypointSymbol);

        await ships.UpsertAsync(newShip, cancellationToken);

        if (!string.IsNullOrWhiteSpace(command.TargetAssignmentType))
        {
            await bus.SendAsync(new SpaceTraders.Application.Commands.Ships.AssignShipCommand(
                result.ShipSymbol,
                command.TargetAssignmentType,
                OriginWaypoint: string.IsNullOrWhiteSpace(command.TargetOriginWaypoint) ? null : command.TargetOriginWaypoint,
                SystemSymbol: result.ShipNav.SystemSymbol,
                WaypointSymbol: result.ShipNav.WaypointSymbol));
        }

        if (command.TargetGoal is not null)
        {
            logger.LogInformation(
                "PurchaseShipHandler: assigning new ship {Symbol} to goal {GoalKind}.",
                result.ShipSymbol,
                command.TargetGoal.Kind);
            await bus.SendAsync(new AssignShipToGoalCommand(result.ShipSymbol, command.TargetGoal));
        }

        if (Enum.TryParse<ShipType>(command.ShipType, true, out var shipTypeEnum))
        {
            await bus.PublishAsync(new NewShipPurchasedEvent(result.ShipSymbol, shipTypeEnum, result.Cost));
        }

        logger.LogInformation("Purchased ship {Symbol} of type {Type} for {Cost} credits.",
            result.ShipSymbol, command.ShipType, result.Cost);
    }

    private static long ResolveShipPurchasePrice(string? shipsDetailJson, string shipType)
    {
        if (string.IsNullOrWhiteSpace(shipsDetailJson))
        {
            return 0;
        }

        try
        {
            var ships = JsonSerializer.Deserialize<List<ShipyardShipPrice>>(shipsDetailJson) ?? [];
            var match = ships.FirstOrDefault(s => shipType.Equals(s.Type, StringComparison.OrdinalIgnoreCase));
            return match?.PurchasePrice ?? 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    private static string ExtractSystemSymbol(string waypointSymbol)
    {
        var lastDash = waypointSymbol.LastIndexOf('-');
        return lastDash > 0 ? waypointSymbol[..lastDash] : waypointSymbol;
    }
}
