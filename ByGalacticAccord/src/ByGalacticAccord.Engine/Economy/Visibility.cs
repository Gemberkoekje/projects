using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Economy;

/// <summary>
/// The information model (§4.6): the economy is legible but not omniscient. An actor sees the
/// local board (here + adjacent), anything on a route/region where it has reputation, and anything
/// it has recently scouted. Reach is reputation; scouting buys reach. Identical for AI and player.
/// </summary>
public static class Visibility
{
    private const decimal ReputationVisibilityThreshold = 0.05m;

    public static bool CanSee(SimulationContext context, ActorState actor, Contract contract)
    {
        if (contract.PostedBy == actor.Id || contract.CounterpartyId == actor.Id)
        {
            return true;
        }

        // Be there: the local board is home + directly adjacent locations. Your home region's boards are part of your
        // everyday reach too (§4.6).
        IReadOnlyList<LocationId> places = contract.Places;
        string homeRegion = context.Location(actor.Home).Region;
        if (contract.Region == homeRegion)
        {
            return true;
        }

        for (int i = 0; i < places.Count; i++)
        {
            if (IsLocal(context, actor.Home, places[i]) || context.Location(places[i]).Region == homeRegion)
            {
                return true;
            }
        }

        // Reach is reputation: a region you have standing in surfaces its board to you.
        if (context.Reputation.Get(actor.Id, ReputationContext.ForRegion(contract.Region)) > ReputationVisibilityThreshold)
        {
            return true;
        }

        // Scouted intel (§4.6, mechanism 2).
        for (int i = 0; i < places.Count; i++)
        {
            if (context.HasFreshScout(actor.Id, places[i]))
            {
                return true;
            }
        }

        return false;
    }

    public static IEnumerable<Contract> OpenContractsVisibleTo(SimulationContext context, ActorState actor) =>
        context.OpenContracts.Where(c => CanSee(context, actor, c));

    private static bool IsLocal(SimulationContext context, LocationId home, LocationId target)
    {
        if (home == target)
        {
            return true;
        }

        return context.Adjacent(home, target);
    }
}
