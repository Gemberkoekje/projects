using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Commands.Ships.SubCommands;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Enums;
using Wolverine;

namespace SpaceTraders.Application.Commands.Ships;

public sealed record MineResourceVolumeCommand
{
    public required string ShipSymbol { get; init; }

    public required string TradeSymbol { get; init; }

    public required string SourceWaypoint { get; init; }

    public required int RequiredUnitsTotal { get; init; }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MineResourceVolumeCommand(string ShipSymbol, string TradeSymbol, string SourceWaypoint, int RequiredUnitsTotal)
    {
        this.ShipSymbol = ShipSymbol;
        this.TradeSymbol = TradeSymbol;
        this.SourceWaypoint = SourceWaypoint;
        this.RequiredUnitsTotal = RequiredUnitsTotal;
    }
}

public sealed class MineResourceVolumeHandler(
    ISpaceTradersPort port,
    IShipRepository ships,
    IWaypointRepository waypoints,
    IRefuelSubCommand refuel,
    IOrbitSubCommand orbit,
    INavigateSubCommand navigate,
    IMessageBus bus,
    ILogger<MineResourceVolumeHandler> logger)
{
    public Task Handle(MineResourceVolumeCommand command, CancellationToken cancellationToken)
        => ExecuteAsync(command, cancellationToken);

    public async Task<ShipCommandResult> ExecuteAsync(MineResourceVolumeCommand command, CancellationToken cancellationToken)
    {
        var ship = await ships.FindAsync(command.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            return ShipCommandResult.Rejected(command.ShipSymbol, ShipLocalStatus.None, string.Empty, string.Empty);
        }

        ship = await ApplyArrivalDeadReckoningIfDueAsync(ship, cancellationToken);

        if (ship.LocalStatus == ShipLocalStatus.InTransit)
        {
            return ShipCommandResult.Rejected(
                ship.Symbol,
                ship.LocalStatus,
                ship.SystemSymbol ?? string.Empty,
                ship.WaypointSymbol ?? string.Empty);
        }

        var atSource = string.Equals(ship.WaypointSymbol, command.SourceWaypoint, StringComparison.OrdinalIgnoreCase);

        if (!atSource)
        {
            if (ship.LocalStatus == ShipLocalStatus.Docked)
            {
                await TryRefuelBeforeUndockingAsync(ship, cancellationToken);
                await orbit.ExecuteAsync(ship.Symbol, cancellationToken);
                ship = await ships.FindAsync(ship.Symbol, cancellationToken) ?? ship;
            }

            if (ship.LocalStatus != ShipLocalStatus.InOrbit)
            {
                await bus.PublishMismatchAndTickAsync(
                    command.ShipSymbol,
                    nameof(MineResourceVolumeCommand),
                    "IN_ORBIT",
                    ship.Status ?? "UNKNOWN",
                    "Ship must be in orbit to navigate to source asteroid.");

                return ShipCommandResult.Rejected(
                    ship.Symbol,
                    ship.LocalStatus,
                    ship.SystemSymbol ?? string.Empty,
                    ship.WaypointSymbol ?? string.Empty);
            }

            await navigate.ExecuteAsync(ship.Symbol, command.SourceWaypoint, Guid.Empty, cancellationToken);

            return new ShipCommandResult(
                ship.Symbol,
                ShipLocalStatus.InTransit,
                ship.SystemSymbol ?? string.Empty,
                ship.WaypointSymbol ?? string.Empty,
                Accepted: true);
        }

        if (ship.LocalStatus == ShipLocalStatus.Docked)
        {
            await TryRefuelBeforeUndockingAsync(ship, cancellationToken);
            await orbit.ExecuteAsync(ship.Symbol, cancellationToken);
            ship = await ships.FindAsync(ship.Symbol, cancellationToken) ?? ship;
        }

        if (ship.LocalStatus != ShipLocalStatus.InOrbit)
        {
            await bus.PublishMismatchAndTickAsync(
                command.ShipSymbol,
                nameof(MineResourceVolumeCommand),
                "IN_ORBIT",
                ship.Status ?? "UNKNOWN",
                "Ship must be in orbit before extraction.");

            return ShipCommandResult.Rejected(
                ship.Symbol,
                ship.LocalStatus,
                ship.SystemSymbol ?? string.Empty,
                ship.WaypointSymbol ?? string.Empty);
        }

        var now = TimeProvider.System.GetUtcNow();
        if (ship.CooldownExpiresAt.HasValue && ship.CooldownExpiresAt.Value > now)
        {
            return new ShipCommandResult(
                ship.Symbol,
                ship.LocalStatus,
                ship.SystemSymbol ?? string.Empty,
                ship.WaypointSymbol ?? string.Empty,
                FuelCurrent: ship.FuelCurrent,
                FuelCapacity: ship.FuelCapacity,
                CargoCurrent: ship.CargoCurrent,
                CargoCapacity: ship.CargoCapacity,
                Accepted: true);
        }

        var cargoInventory = ship.CargoInventory ?? [];
        var targetUnitsInCargo = cargoInventory
            .FirstOrDefault(i => i.Symbol.Equals(command.TradeSymbol, StringComparison.OrdinalIgnoreCase))?
            .Units ?? 0;

        var maxTargetForTrip = ship.CargoCapacity > 0
            ? Math.Min(Math.Max(command.RequiredUnitsTotal, 0), ship.CargoCapacity)
            : Math.Max(command.RequiredUnitsTotal, 0);

        if (targetUnitsInCargo >= maxTargetForTrip || ship.CargoCurrent >= ship.CargoCapacity)
        {
            await JettisonNonTargetCargoAsync(ship.Symbol, cargoInventory, command.TradeSymbol, cancellationToken);

            return new ShipCommandResult(
                ship.Symbol,
                ship.LocalStatus,
                ship.SystemSymbol ?? string.Empty,
                ship.WaypointSymbol ?? string.Empty,
                FuelCurrent: ship.FuelCurrent,
                FuelCapacity: ship.FuelCapacity,
                CargoCurrent: ship.CargoCurrent,
                CargoCapacity: ship.CargoCapacity,
                Accepted: true);
        }

        var extractResult = await port.ExtractResourcesAsync(ship.Symbol, cancellationToken);
        await ships.UpdateCargoAsync(ship.Symbol, extractResult.Cargo, cancellationToken);

        var cooldownAt = extractResult.CooldownExpiresAt ?? now.AddSeconds(extractResult.CooldownSeconds);
        await ships.UpdateCooldownAsync(ship.Symbol, cooldownAt, cancellationToken);

        var updatedShip = await ships.FindAsync(ship.Symbol, cancellationToken) ?? ship;
        await JettisonNonTargetCargoAsync(ship.Symbol, updatedShip.CargoInventory ?? [], command.TradeSymbol, cancellationToken);

        logger.LogInformation(
            "MineResourceVolume: ship {ShipSymbol} extracted {Units} {YieldSymbol} while targeting {TradeSymbol}.",
            ship.Symbol,
            extractResult.YieldUnits,
            extractResult.YieldSymbol,
            command.TradeSymbol);

        return new ShipCommandResult(
            ship.Symbol,
            ShipLocalStatus.InOrbit,
            ship.SystemSymbol ?? string.Empty,
            ship.WaypointSymbol ?? string.Empty,
            FuelCurrent: ship.FuelCurrent,
            FuelCapacity: ship.FuelCapacity,
            CargoCurrent: extractResult.Cargo.Units,
            CargoCapacity: extractResult.Cargo.Capacity,
            Accepted: true);
    }

    private async Task TryRefuelBeforeUndockingAsync(ShipModel ship, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ship.WaypointSymbol)
            || ship.FuelCapacity <= 0
            || ship.FuelCurrent >= ship.FuelCapacity)
        {
            return;
        }

        var waypoint = await waypoints.FindAsync(ship.WaypointSymbol, cancellationToken);
        if (waypoint?.HasMarket != true)
        {
            return;
        }

        await refuel.ExecuteAsync(ship.Symbol, fromCargo: false, cancellationToken);
    }

    private async Task<ShipModel> ApplyArrivalDeadReckoningIfDueAsync(ShipModel ship, CancellationToken cancellationToken)
    {
        if (ship.LocalStatus != ShipLocalStatus.InTransit
            || !ship.ArrivesAt.HasValue
            || ship.ArrivesAt.Value > TimeProvider.System.GetUtcNow())
        {
            return ship;
        }

        var waypoint = ship.DestWaypointSymbol ?? ship.WaypointSymbol ?? string.Empty;
        var nav = new NavModel(
            Status: "IN_ORBIT",
            SystemSymbol: ship.SystemSymbol ?? string.Empty,
            WaypointSymbol: waypoint,
            FlightMode: ship.FlightMode ?? "CRUISE",
            DestWaypointSymbol: null,
            ArrivesAt: null);

        await ships.UpdateNavAsync(ship.Symbol, nav, null, cancellationToken);
        return await ships.FindAsync(ship.Symbol, cancellationToken) ?? ship;
    }

    private async Task JettisonNonTargetCargoAsync(
        string shipSymbol,
        IReadOnlyList<CargoItemModel> cargo,
        string targetTradeSymbol,
        CancellationToken cancellationToken)
    {
        foreach (var item in cargo.Where(i => i.Units > 0 && !i.Symbol.Equals(targetTradeSymbol, StringComparison.OrdinalIgnoreCase)))
        {
            var result = await port.JettisonCargoAsync(shipSymbol, item.Symbol, item.Units, cancellationToken);
            await ships.UpdateCargoAsync(shipSymbol, result.Cargo, cancellationToken);
        }
    }
}
