using System;
using System.Collections.Generic;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Economy;

/// <summary>How close a location is to an actor's presence (design §7.1), and so how much the actor knows of its prices.</summary>
public enum Tier
{
    /// <summary>A location where it has a presence: live prices and stock levels, and every post shown there.</summary>
    Here,

    /// <summary>One jump from such a location: prices updated daily, no internal state.</summary>
    Near,

    /// <summary>Anywhere else: prices as of the last time it was there.</summary>
    Far,
}

/// <summary>
/// What one actor can see at one moment (design §7.1, D38): where it is present, the tier of every location from there (for
/// prices), and so which contracts it sees live: those shown where it is present (<see cref="Advertising"/>). Built once per
/// question (<see cref="Visibility.Of"/>) and asked many times; it goes stale as soon as a ship moves.
/// </summary>
public sealed class Sight
{
    // The tier of each location, by its id's value; ids past the end are far.
    private readonly Tier[] _tiers;

    internal Sight(ActorId actor, long tick, IReadOnlyList<LocationId> presence, Tier[] tiers)
    {
        Actor = actor;
        Tick = tick;
        Presence = presence;
        _tiers = tiers;
    }

    public ActorId Actor { get; }

    /// <summary>When it was taken.</summary>
    public long Tick { get; }

    /// <summary>Where the actor is present: its home, its ships (where they are, or where they are heading), its storage.</summary>
    public IReadOnlyList<LocationId> Presence { get; }

    public Tier TierOf(LocationId location) => location.Value >= 0 && location.Value < _tiers.Length ? _tiers[location.Value] : Tier.Far;

    /// <summary>
    /// Whether the actor sees a contract live (design §7.1, D38): its own; or one shown, by now, at a location where it is
    /// present. A post is shown in its own system at once, and further out only where its poster paid to show it
    /// (<see cref="Advertising"/>). Anything else reaches it only through the news (<see cref="Knowledge"/>).
    /// </summary>
    public bool CanSee(Contract contract)
    {
        if (contract.PostedBy == Actor || contract.CounterpartyId == Actor)
        {
            return true;
        }

        for (int i = 0; i < Presence.Count; i++)
        {
            if (contract.ShownFrom(Presence[i]) is { } from && from <= Tick)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>How many game hours until a post reaches the actor (design §3.6): until it is shown where the actor is present; null if it isn't shown there.</summary>
    public long? ReachHours(Contract contract)
    {
        long? soonest = null;
        for (int i = 0; i < Presence.Count; i++)
        {
            if (contract.ShownFrom(Presence[i]) is { } from)
            {
                long hours = Math.Max(0L, from - Tick);
                soonest = soonest is { } s ? Math.Min(s, hours) : hours;
            }
        }

        return soonest;
    }
}

/// <summary>
/// The information model's geometry (design §7.1, D18, D31): an actor knows what its presence lets it see. Presence is its
/// home, the locations of its ships (for a ship in transit, where it's heading), and anywhere it rents storage. The same
/// rules apply to every actor, the player too; a company with five ships knows more than a pilot with one. What the actor
/// remembers of places it no longer sees is <see cref="Knowledge"/>'s.
/// </summary>
public static class Visibility
{
    /// <summary>What <paramref name="actor"/> can see now.</summary>
    public static Sight Of(SimulationContext context, ActorState actor)
    {
        IReadOnlyList<LocationId> presence = Presence(context, actor);
        var tiers = new Tier[context.LocationIdLimit];
        System.Array.Fill(tiers, Tier.Far);
        for (int i = 0; i < presence.Count; i++)
        {
            tiers[presence[i].Value] = Tier.Here;
        }

        foreach (LocationId location in context.Locations.Keys)
        {
            if (tiers[location.Value] == Tier.Far)
            {
                for (int i = 0; i < presence.Count; i++)
                {
                    if (context.Adjacent(presence[i], location))
                    {
                        tiers[location.Value] = Tier.Near;
                        break;
                    }
                }
            }
        }

        return new Sight(actor.Id, context.CurrentTick, presence, tiers);
    }

    /// <summary>
    /// Where an actor is present (design §7.1): its home, each of its ships (where it is docked, or where it is heading), and
    /// every place it rents storage. Home first, then in the order of its ships and stores.
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

        IReadOnlyList<StorageContract> stores = context.OpenStorages;
        for (int i = 0; i < stores.Count; i++)
        {
            if (stores[i].Renter == actor.Id && !places.Contains(stores[i].Location))
            {
                places.Add(stores[i].Location);
            }
        }

        return places;
    }

    /// <summary>Whether an actor sees a contract live now (<see cref="Sight.CanSee"/>), or knows of it from the news (<see cref="Knowledge.FromNews"/>).</summary>
    public static bool CanSee(SimulationContext context, ActorState actor, Contract contract) =>
        Of(context, actor).CanSee(contract) || Knowledge.FromNews(context, actor, contract);
}
