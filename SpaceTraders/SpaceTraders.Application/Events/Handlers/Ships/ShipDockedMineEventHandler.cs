using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles mining-role ships that are docked. Sells cargo per policy, then orbits to continue the cycle.
/// </summary>
public sealed class ShipDockedMineEventHandler(
    IShipAssignmentRepository assignments,
    IShipRepository ships,
    IContractRepository contracts,
    IMarketRepository markets,
    ISettingsRepository settings,
    IFleetMaintenancePlanner maintenance,
    IDockedCommandAcceptor dockedCommands,
    IMessageBus bus) : IChainOfCommandEventHandler<ShipDockedEvent>
{

    public async Task<ChainOfCommandHandlerResult> HandleAsync(ShipDockedEvent @event, CancellationToken cancellationToken)
    {
        var assignment = await assignments.FindAsync(@event.ShipSymbol, cancellationToken);
        if (assignment is null ||
            (!assignment.AssignmentType.Equals("Mine", StringComparison.OrdinalIgnoreCase) &&
             !assignment.AssignmentType.Equals("Siphon", StringComparison.OrdinalIgnoreCase)))
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var ship = await ships.FindAsync(@event.ShipSymbol, cancellationToken);
        if (ship is null)
        {
            return ChainOfCommandHandlerResult.Skipped();
        }

        var deliveredAnyContractCargo = await TryDeliverContractCargoAtCurrentWaypointAsync(ship, contracts, dockedCommands, cancellationToken);

        if (!deliveredAnyContractCargo)
        {
            var reserveHydrocarbonUnits = await settings.GetAsync<int>("Mining.ReserveHydrocarbonUnits", cancellationToken);
            var minimumSellPrice = await settings.GetAsync<int>("Mining.MinimumSellPriceToKeepCargo", cancellationToken);
            var jettisonLowValueWhenFull = await settings.GetAsync<bool>("Mining.JettisonLowValueWhenFull", cancellationToken);
            var market = string.IsNullOrWhiteSpace(ship.WaypointSymbol)
                ? null
                : await markets.FindSnapshotByWaypointAsync(ship.WaypointSymbol, cancellationToken);
            var protectedCargo = await GetProtectedContractCargoAsync(contracts, cancellationToken);
            var soldHydrocarbon = false;

            if (ship.CargoInventory is not null)
            {
                foreach (var cargo in ship.CargoInventory.Where(c => c.Units > 0))
                {
                    if (protectedCargo.Contains(cargo.Symbol))
                    {
                        continue;
                    }

                    if (cargo.Symbol.Equals("HYDROCARBON", StringComparison.OrdinalIgnoreCase) && cargo.Units <= reserveHydrocarbonUnits)
                    {
                        continue;
                    }

                    var sellPrice = market?.TradeGoods.FirstOrDefault(g => g.Symbol.Equals(cargo.Symbol, StringComparison.OrdinalIgnoreCase))?.SellPrice ?? 0;
                    if (sellPrice >= minimumSellPrice)
                    {
                        var sellUnits = cargo.Symbol.Equals("HYDROCARBON", StringComparison.OrdinalIgnoreCase)
                            ? Math.Max(0, cargo.Units - reserveHydrocarbonUnits)
                            : cargo.Units;

                        if (sellUnits > 0)
                        {
                            await dockedCommands.SellCargoAsync(@event.ShipSymbol, cargo.Symbol, sellUnits, cancellationToken);
                            if (cargo.Symbol.Equals("HYDROCARBON", StringComparison.OrdinalIgnoreCase))
                            {
                                soldHydrocarbon = true;
                            }
                        }

                        continue;
                    }

                    if (jettisonLowValueWhenFull && ship.CargoCurrent >= ship.CargoCapacity)
                    {
                        var jettisonUnits = cargo.Symbol.Equals("HYDROCARBON", StringComparison.OrdinalIgnoreCase)
                            ? Math.Max(0, cargo.Units - reserveHydrocarbonUnits)
                            : cargo.Units;

                        if (jettisonUnits > 0)
                        {
                            await bus.InvokeAsync(new JettisonCargoCommand(@event.ShipSymbol, cargo.Symbol, jettisonUnits), cancellationToken);
                        }
                    }
                }
            }

            if (soldHydrocarbon)
            {
                await dockedCommands.RefuelAsync(@event.ShipSymbol, fromCargo: true, cancellationToken);
            }
        }

        var maintenanceDecision = await maintenance.DecideAsync(ship, assignment.AssignmentType, cancellationToken);
        if (maintenanceDecision.ShouldScrap)
        {
            await dockedCommands.ScrapAsync(@event.ShipSymbol, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        if (maintenanceDecision.ShouldRepair)
        {
            await dockedCommands.RepairAsync(@event.ShipSymbol, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        await dockedCommands.OrbitAsync(@event.ShipSymbol, cancellationToken);
        return ChainOfCommandHandlerResult.Handled();
    }

    private static async Task<bool> TryDeliverContractCargoAtCurrentWaypointAsync(
        ShipModel ship,
        IContractRepository contractRepository,
        IDockedCommandAcceptor dockedCommands,
        CancellationToken cancellationToken)
    {
        if (ship.CargoInventory is null || string.IsNullOrWhiteSpace(ship.WaypointSymbol))
        {
            return false;
        }

        var activeContracts = await contractRepository.GetActiveAsync(cancellationToken);
        var deliveredAny = false;

        foreach (var contract in activeContracts.Where(c => c.IsAccepted && !c.IsFulfilled && !string.IsNullOrWhiteSpace(c.DeliverablesJson)))
        {
            var deliverables = DeserializeDeliverables(contract.DeliverablesJson);
            foreach (var deliverable in deliverables)
            {
                if (!deliverable.DestinationSymbol.Equals(ship.WaypointSymbol, StringComparison.OrdinalIgnoreCase)
                    || deliverable.UnitsRequired <= deliverable.UnitsFulfilled)
                {
                    continue;
                }

                var cargoUnits = ship.CargoInventory
                    .FirstOrDefault(i => i.Symbol.Equals(deliverable.TradeSymbol, StringComparison.OrdinalIgnoreCase))?
                    .Units ?? 0;
                if (cargoUnits <= 0)
                {
                    continue;
                }

                var pending = deliverable.UnitsRequired - deliverable.UnitsFulfilled;
                var unitsToDeliver = Math.Min(cargoUnits, pending);
                if (unitsToDeliver <= 0)
                {
                    continue;
                }

                await dockedCommands.DeliverContractAsync(
                    contract.Id,
                    ship.Symbol,
                    deliverable.TradeSymbol,
                    unitsToDeliver,
                    ship.WaypointSymbol,
                    cancellationToken);

                deliveredAny = true;
            }
        }

        return deliveredAny;
    }

    private static async Task<HashSet<string>> GetProtectedContractCargoAsync(
        IContractRepository contractRepository,
        CancellationToken cancellationToken)
    {
        var activeContracts = await contractRepository.GetActiveAsync(cancellationToken);
        return activeContracts
            .Where(c => c.IsAccepted && !c.IsFulfilled)
            .SelectMany(c => DeserializeDeliverables(c.DeliverablesJson))
            .Where(d => d.UnitsRequired > d.UnitsFulfilled)
            .Select(d => d.TradeSymbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<SpaceTraders.Application.DTOs.ContractDeliverableDto> DeserializeDeliverables(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<SpaceTraders.Application.DTOs.ContractDeliverableDto>>(json) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
