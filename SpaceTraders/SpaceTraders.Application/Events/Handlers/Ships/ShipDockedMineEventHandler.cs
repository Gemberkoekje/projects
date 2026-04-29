using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

/// <summary>
/// Handles mining-role ships that are docked. Sells cargo per policy, refuels if low, then orbits to continue the cycle.
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
    public int Priority => 100;

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

        var reserveHydrocarbonUnits = await settings.GetAsync<int>("Mining.ReserveHydrocarbonUnits", cancellationToken);
        var minimumSellPrice = await settings.GetAsync<int>("Mining.MinimumSellPriceToKeepCargo", cancellationToken);
        var jettisonLowValueWhenFull = await settings.GetAsync<bool>("Mining.JettisonLowValueWhenFull", cancellationToken);
        var market = string.IsNullOrWhiteSpace(ship.WaypointSymbol)
            ? null
            : await markets.FindSnapshotByWaypointAsync(ship.WaypointSymbol, cancellationToken);
        var protectedCargo = await GetProtectedContractCargoAsync(contracts, cancellationToken);

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

        if (ship.FuelCapacity > 0 && ship.FuelCurrent < (ship.FuelCapacity / 4))
        {
            var hasHydrocarbonCargo = ship.CargoInventory?.Any(c =>
                c.Units > reserveHydrocarbonUnits && c.Symbol.Equals("HYDROCARBON", StringComparison.OrdinalIgnoreCase)) == true;

            await dockedCommands.RefuelAsync(@event.ShipSymbol, hasHydrocarbonCargo, cancellationToken);
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

        var preferredMiningMount = await settings.GetAsync<string>("Outfitting.PreferredMiningMount", cancellationToken);
        if (!string.IsNullOrWhiteSpace(preferredMiningMount) &&
            (ship.MountSymbols ?? []).All(m => !m.Equals(preferredMiningMount, StringComparison.OrdinalIgnoreCase)))
        {
            await dockedCommands.InstallMountAsync(@event.ShipSymbol, preferredMiningMount, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        var preferredMineralProcessor = await settings.GetAsync<string>("Outfitting.PreferredMineralProcessor", cancellationToken);
        if (!string.IsNullOrWhiteSpace(preferredMineralProcessor) && !ship.HasMineralProcessor)
        {
            await dockedCommands.InstallModuleAsync(@event.ShipSymbol, preferredMineralProcessor, cancellationToken);
            return ChainOfCommandHandlerResult.Handled();
        }

        await dockedCommands.OrbitAsync(@event.ShipSymbol, cancellationToken);
        return ChainOfCommandHandlerResult.Handled();
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
