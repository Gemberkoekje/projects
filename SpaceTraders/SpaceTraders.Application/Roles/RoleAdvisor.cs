using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Mining;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Roles;

/// <summary>
/// Whether a ship about to be bought would take the role it is bought for (PLAN.md slice 6.9). With the role board
/// on, a drone the mining plan buys becomes a trader when trading pays it more; the plan, finding no free miner, would
/// buy the next, and the next. So the mining and siphon plans buy a drone only when the board would give it their role.
/// </summary>
public interface IRoleAdvisor
{
    /// <summary>
    /// Whether the ship would take the role: it is one of its roles whose plan is on, and its best trip in it earns at
    /// least as much per hour as its best trip in any other (<see cref="RoleEstimator"/>), where it would be bought.
    /// </summary>
    /// <param name="ship">The ship as it would be bought: its type, tank and hold, at the shipyard.</param>
    /// <param name="role">The role it is bought for.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>True when the board would give it that role.</returns>
    Task<bool> WouldTakeAsync(ShipModel ship, FleetRole role, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class RoleAdvisor(
    IMiningContextReader miningContexts,
    ISettingsRepository settings,
    IGatheringRates rates) : IRoleAdvisor
{
    /// <inheritdoc />
    public Task<bool> WouldTakeAsync(ShipModel ship, FleetRole role, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ship);
        return WeighAsync(ship, role, cancellationToken);
    }

    private async Task<bool> WeighAsync(ShipModel ship, FleetRole role, CancellationToken cancellationToken)
    {
        // The plans buy no drone while the contract takes the miners (D23), so the contract doesn't come into it.
        var roleSettings = await RoleSettings.ReadAsync(settings, cancellationToken);
        var roles = roleSettings.Available(ship, contractWantsOre: false);
        if (!roles.Contains(role))
        {
            return false;
        }

        var others = roles.Where(other => other != role && other != FleetRole.Survey).ToList();
        if (others.Count == 0)
        {
            return true;
        }

        var mining = await miningContexts.ReadAsync(ship.SystemSymbol ?? string.Empty, cancellationToken);
        var context = new RoleContext(
            mining,
            roleSettings.MinProfitPerUnit,
            roleSettings.FuelReserveCredits,
            new ChainValues(mining.Map, roleSettings.ChainShare),
            rates);
        var own = Best(context, ship, role);
        return others.All(other => Best(context, ship, other) <= own);
    }

    private static double Best(RoleContext context, ShipModel ship, FleetRole role)
        => RoleEstimator.Options(context, ship, role, 1).Select(option => option.CreditsPerHour).DefaultIfEmpty(0).Max();
}
