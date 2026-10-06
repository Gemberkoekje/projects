using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Ports;
using SpaceTraders.Domain.Goals;

namespace SpaceTraders.Application.Automation;

/// <summary>
/// The fleet and each ship's active goal, read so that they agree (B71). A trip that ends in an arrival handler, outside the
/// tick, updates its ship first (the cargo it sold or supplied) and ends its goal after. A pass that read the ships and then
/// each goal could see a goal that had just ended beside the ship as it was before: free, with cargo it no longer holds. A
/// gathering plan leaves such a ship to the trading plan (B63), or sends it to sell what it no longer has: on 2026-10-06
/// SPECTER-D, which had just supplied its load to the jump gate, was sent on an 18-minute trade while its next load waited.
/// So every goal is read first, and the ships after: a goal read as ended comes with the ship as its trip left it, and one
/// read as running keeps its ship busy for the pass.
/// </summary>
public sealed class FleetGoals
{
    private readonly IReadOnlyDictionary<string, ShipGoal> _goals;

    private FleetGoals(IReadOnlyList<ShipModel> fleet, IReadOnlyDictionary<string, ShipGoal> goals)
    {
        Fleet = fleet;
        _goals = goals;
    }

    /// <summary>The ships, read after their goals.</summary>
    public IReadOnlyList<ShipModel> Fleet { get; }

    /// <summary>Reads every ship's active goal, then the ships.</summary>
    /// <param name="ships">The ship cache.</param>
    /// <param name="goals">The ships' goals.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>The fleet and its goals, as of one moment as far as a trip's end goes.</returns>
    public static Task<FleetGoals> ReadAsync(IShipRepository ships, IShipGoalRepository goals, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ships);
        ArgumentNullException.ThrowIfNull(goals);
        return ReadGoalsThenShipsAsync(ships, goals, cancellationToken);
    }

    /// <summary>The ship's active goal as read, if it has one.</summary>
    /// <param name="shipSymbol">The ship.</param>
    /// <returns>The goal, or null for a ship without one.</returns>
    public ShipGoal? GoalOf(string shipSymbol) => _goals.GetValueOrDefault(shipSymbol);

    private static async Task<FleetGoals> ReadGoalsThenShipsAsync(IShipRepository ships, IShipGoalRepository goals, CancellationToken cancellationToken)
    {
        var read = new Dictionary<string, ShipGoal>(StringComparer.OrdinalIgnoreCase);
        var symbols = (await ships.GetAllAsync(cancellationToken)).Select(ship => ship.Symbol).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols)
        {
            await ReadGoalAsync(goals, symbol, read, cancellationToken);
        }

        var fleet = await ships.GetAllAsync(cancellationToken);

        // A ship that joined the fleet between the two reads has its goal read now.
        foreach (var ship in fleet.Where(ship => !symbols.Contains(ship.Symbol)))
        {
            await ReadGoalAsync(goals, ship.Symbol, read, cancellationToken);
        }

        return new FleetGoals(fleet, read);
    }

    private static async Task ReadGoalAsync(IShipGoalRepository goals, string shipSymbol, Dictionary<string, ShipGoal> read, CancellationToken cancellationToken)
    {
        if (await goals.GetActiveGoalAsync(shipSymbol, cancellationToken) is { } goal)
        {
            read[shipSymbol] = goal;
        }
    }
}
