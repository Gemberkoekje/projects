using Microsoft.Extensions.Logging;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Automation;

public interface IScoutShipSelectionService
{
    Task<ShipModel> SelectAsync(CancellationToken cancellationToken = default);
}

public sealed class ScoutShipSelectionService(
    IShipRepository ships,
    ILogger<ScoutShipSelectionService> logger) : IScoutShipSelectionService
{
    public async Task<ShipModel> SelectAsync(CancellationToken cancellationToken = default)
    {
        var allShips = await ships.GetAllAsync(cancellationToken);

        var candidates = allShips
            .Where(ship => ship.FuelCapacity > 0 && ship.FuelCurrent > 0)
            .OrderBy(ship => ship.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        if (candidates.Count == 0)
        {
            const string message = "Scout ship selection failed: expected exactly one ship with fuel capacity/current fuel available, but found 0 candidates.";
            logger.LogError(message);
            throw new InvalidOperationException(message);
        }

        var symbols = string.Join(", ", candidates.Select(candidate => candidate.Symbol));
        var multiMessage =
            $"Scout ship selection failed: expected exactly one ship with fuel capacity/current fuel available, but found {candidates.Count} candidates ({symbols}).";

        logger.LogError(multiMessage);
        throw new InvalidOperationException(multiMessage);
    }
}
