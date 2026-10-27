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

    /// <summary>How long a new post takes to reach an actor where it is present (design §7.1: here, at once).</summary>
    public const long ReachHere = 0;

    /// <summary>One jump from its presence: "contracts a few hours after posting" (design §7.1).</summary>
    public const long ReachNear = 4;

    /// <summary>Further, where it sees the post at all (its home region, its reputation, a scout).</summary>
    public const long ReachFar = 8;

    /// <summary>
    /// Where an actor is present (design §7.1): its home, each of its ships (where it is docked, or where it is heading),
    /// and every place it rents storage. S5 uses it for how fast a post reaches the actor; S6 makes it decide what the actor
    /// sees at all.
    /// </summary>
    public static IReadOnlyList<LocationId> Presence(SimulationContext context, ActorState actor)
    {
        var places = new List<LocationId> { actor.Home };
        foreach (ShipId id in actor.Ships)
        {
            Ship ship = context.Ship(id);
            LocationId at = ship.Course?.To ?? ship.Location;
            if (!places.Contains(at))
            {
                places.Add(at);
            }
        }

        foreach (StorageContract storage in context.StoragesOf(actor.Id))
        {
            if (!places.Contains(storage.Location))
            {
                places.Add(storage.Location);
            }
        }

        return places;
    }

    /// <summary>
    /// How many game hours a new post takes to reach an actor that can see it (design §3.6: a post at a busy station is seen
    /// quickly, one at a remote outpost by few): at once where it is present, a few hours one jump away, longer otherwise.
    /// Null if it can't see the post.
    /// </summary>
    public static long? ReachHours(SimulationContext context, ActorState actor, Contract contract)
    {
        if (!CanSee(context, actor, contract))
        {
            return null;
        }

        IReadOnlyList<LocationId> presence = Presence(context, actor);
        IReadOnlyList<LocationId> places = contract.Places;
        if (places.Any(presence.Contains))
        {
            return ReachHere;
        }

        return places.Any(p => presence.Any(q => context.Adjacent(p, q))) ? ReachNear : ReachFar;
    }

    private static bool IsLocal(SimulationContext context, LocationId home, LocationId target)
    {
        if (home == target)
        {
            return true;
        }

        return context.Adjacent(home, target);
    }
}
