using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;

namespace SpaceTraders.Application.Exploring;

/// <summary>
/// The jump gates between the systems the explore plan knows, as the ways between systems read them (PLAN.md slice 6.28,
/// D101): the explore plan's state, which keeps every gate it looked at, whether it is built and what it connects to, with
/// the jumps refused lately (<see cref="JumpRefusals"/>). <see cref="ExploreAtlas"/> finds the ways through it.
/// </summary>
public interface IGateNetwork
{
    /// <summary>Reads the gates.</summary>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The explore plan's state with the refusals; null while the explore plan never ran.</returns>
    Task<ExplorePlanState?> ReadAsync(CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class GateNetwork(IPlanRepository plans, JumpRefusals refusals) : IGateNetwork
{
    /// <inheritdoc />
    public async Task<ExplorePlanState?> ReadAsync(CancellationToken cancellationToken)
        => await plans.GetAsync<ExplorePlanState>(PlanTypes.Explore, cancellationToken) is { } state ? refusals.Apply(state) : null;
}
