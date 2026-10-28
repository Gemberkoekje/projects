using System;
using System.Collections.Generic;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Economy;

/// <summary>
/// Paying to show a post further out (design §7.1, D38; plan S6b). A post is shown in its own system (at its places) from
/// the moment it is posted, and free as far as <see cref="SimulationConfig.AdFreeJumps"/> (none, by default). Further out,
/// its poster pays a fee for each system it is shown in (<see cref="FeeAt"/>: a small share of its price, four times dearer
/// for each further jump), paid to that system's station operator, and it is shown there a delay after it is paid for
/// (<see cref="DelayHours"/>). Whoever is present in a system where a post is shown sees it (<see cref="Sight.CanSee"/>).
/// AI actors pay a ring at a time for a post that sits unsold (<see cref="AdvertiseUnsold"/>); the player chooses the systems
/// (<see cref="PlayerActions.Advertise"/>).
/// </summary>
public static class Advertising
{
    /// <summary>The fewest jumps from any of a contract's places to a location; int.MaxValue if no route reaches it.</summary>
    public static int JumpsFrom(SimulationContext context, Contract contract, LocationId location) => JumpsFrom(context, contract.Places, location);

    /// <summary>The fewest jumps from any of these places to a location; int.MaxValue if no route reaches it.</summary>
    public static int JumpsFrom(SimulationContext context, IReadOnlyList<LocationId> places, LocationId location)
    {
        int nearest = int.MaxValue;
        for (int i = 0; i < places.Count; i++)
        {
            nearest = Math.Min(nearest, context.Jumps(places[i], location));
        }

        return nearest;
    }

    /// <summary>How many hours after it is paid for a post is shown that many jumps out: past the configured ones, a day more a jump.</summary>
    public static long DelayHours(SimulationContext context, int jumps)
    {
        IReadOnlyList<long> delays = context.Config.AdDelayHours;
        return jumps < delays.Count ? delays[jumps] : delays[^1] + (24L * (jumps - delays.Count + 1));
    }

    /// <summary>
    /// What it costs to show a contract at a location: nothing at its own places or within the free reach; further out, a share
    /// of its price (at least the floor), times <see cref="SimulationConfig.AdFeeGrowth"/> for each jump past the first.
    /// </summary>
    public static Credits FeeAt(SimulationContext context, Contract contract, LocationId location) =>
        FeeAt(context, contract.Places, contract.OfferedPrice, location);

    /// <summary>What it costs to show a post with these places and this price at a location (<see cref="FeeAt(SimulationContext, Contract, LocationId)"/>), before it is posted.</summary>
    public static Credits FeeAt(SimulationContext context, IReadOnlyList<LocationId> places, Credits price, LocationId location)
    {
        int jumps = JumpsFrom(context, places, location);
        if (jumps == int.MaxValue || jumps <= context.Config.AdFreeJumps)
        {
            return Credits.Zero;
        }

        SimulationConfig config = context.Config;
        decimal fee = Math.Max(config.AdFeeFloor.Amount, config.AdFeeShare * price.Amount);
        for (int i = 1; i < jumps; i++)
        {
            fee *= config.AdFeeGrowth;
        }

        return Credits.Of(Math.Round(fee, 2));
    }

    /// <summary>Where a contract isn't shown yet and could be, nearest first (then by location), with the jumps and the fee for each.</summary>
    public static IReadOnlyList<(LocationId At, int Jumps, Credits Fee)> Options(SimulationContext context, Contract contract) =>
        Options(context, contract.Places, contract.OfferedPrice, location => contract.ShownFrom(location) is not null);

    /// <summary>Where a post with these places and this price could be shown beyond them, with the jumps and the fee for each (<see cref="Options(SimulationContext, Contract)"/>).</summary>
    public static IReadOnlyList<(LocationId At, int Jumps, Credits Fee)> Options(
        SimulationContext context, IReadOnlyList<LocationId> places, Credits price, Func<LocationId, bool>? shown = null)
    {
        var options = new List<(LocationId At, int Jumps, Credits Fee)>();
        foreach (LocationId location in context.Locations.Keys.OrderBy(l => l.Value))
        {
            int jumps = JumpsFrom(context, places, location);
            bool already = shown is not null ? shown(location) : jumps <= context.Config.AdFreeJumps;
            if (jumps != int.MaxValue && jumps > 0 && !already)
            {
                options.Add((location, jumps, FeeAt(context, places, price, location)));
            }
        }

        return options.OrderBy(o => o.Jumps).ThenBy(o => o.At.Value).ToList();
    }

    /// <summary>A new contract is shown at its places at once, and within the free reach after its delays.</summary>
    internal static void ShowOnPosting(SimulationContext context, Contract contract)
    {
        foreach (LocationId place in contract.Places)
        {
            contract.ShowAt(place, contract.PostedTick);
        }

        int free = context.Config.AdFreeJumps;
        if (free <= 0)
        {
            return;
        }

        foreach (LocationId location in context.Locations.Keys.OrderBy(l => l.Value))
        {
            int jumps = JumpsFrom(context, contract, location);
            if (jumps >= 1 && jumps <= free)
            {
                contract.ShowAt(location, contract.PostedTick + DelayHours(context, jumps));
            }
        }
    }

    /// <summary>What showing a contract at these locations costs in all; null if it can't be shown at one of them.</summary>
    public static Credits? Quote(SimulationContext context, Contract contract, IEnumerable<LocationId> locations)
    {
        decimal total = 0m;
        foreach (LocationId location in locations.Distinct())
        {
            if (contract.ShownFrom(location) is not null || JumpsFrom(context, contract, location) == int.MaxValue)
            {
                return null;
            }

            total += FeeAt(context, contract, location).Amount;
        }

        return Credits.Of(total);
    }

    /// <summary>
    /// The poster pays to show its open contract at locations it isn't shown at yet: each fee to that system's station
    /// operator, all or nothing. It is shown at each a delay after (<see cref="DelayHours"/>). The player's post then reaches
    /// the AI actors there that can answer it (<see cref="DecisionEngine.NoticePost"/>). False, with nothing paid, if the
    /// poster isn't the contract's, it is closed, a location can't take it, or the poster can't pay.
    /// </summary>
    public static bool Show(SimulationContext context, ActorState poster, Contract contract, IReadOnlyCollection<LocationId> locations)
    {
        if (contract.PostedBy != poster.Id || !contract.IsOpen || contract is StorageContract || locations.Count == 0
            || Quote(context, contract, locations) is not { } total || poster.Credits.Amount < total.Amount)
        {
            return false;
        }

        var shown = new List<LocationId>();
        foreach (LocationId location in locations.Distinct().OrderBy(l => l.Value))
        {
            int jumps = JumpsFrom(context, contract, location);
            Credits fee = FeeAt(context, contract, location);
            if (fee.Amount > 0m)
            {
                ActorState? board = context.StationOperatorAt(location);
                bool paid = board is not null ? context.TryTransfer(poster, board, fee) : context.ApplySink(poster, fee).Amount >= fee.Amount;
                if (!paid)
                {
                    break; // can't happen: the total was checked above
                }

                context.NoteAdShown(jumps, fee);
            }

            contract.ShowAt(location, context.CurrentTick + DelayHours(context, jumps));
            shown.Add(location);
        }

        contract.NoteAdvertised(context.CurrentTick);
        if (Market.HagglesInGameTime(poster))
        {
            DecisionEngine.NoticePost(context, contract, newlyShown: shown);
        }

        return true;
    }

    /// <summary>
    /// An AI actor's rule (design §4.3, D38): a post of its own still open a day after it was posted or last shown further is
    /// shown one jump further, in every system at that distance, when that costs no more than
    /// <see cref="SimulationConfig.AdBudgetShare"/> of the post's price and the actor has the credits.
    /// </summary>
    public static void AdvertiseUnsold(SimulationContext context, ActorState actor)
    {
        if (actor.IsPlayer)
        {
            return;
        }

        SimulationConfig config = context.Config;
        foreach (Contract contract in context.LiveContractsOf(actor.Id))
        {
            if (contract.PostedBy != actor.Id || !contract.IsOpen || contract is StorageContract || contract is SaleContract { AtTheDoor: true }
                || context.CurrentTick - contract.LastAdvertised < config.AdWaitHours)
            {
                continue;
            }

            IReadOnlyList<(LocationId At, int Jumps, Credits Fee)> options = Options(context, contract);
            if (options.Count == 0)
            {
                continue;
            }

            int next = options[0].Jumps;
            var ring = options.Where(o => o.Jumps == next).ToList();
            decimal fee = ring.Sum(o => o.Fee.Amount);
            if (fee > config.AdBudgetShare * contract.OfferedPrice.Amount || actor.Credits.Amount < fee)
            {
                continue;
            }

            Show(context, actor, contract, ring.Select(o => o.At).ToList());
        }
    }

    /// <summary>Whether a taker sees a contract only where its poster paid to show it, not at its places or in the free reach.</summary>
    internal static bool SeenOnlyThroughAd(SimulationContext context, Contract contract, Sight sight)
    {
        bool paid = false;
        foreach (LocationId at in sight.Presence)
        {
            if (contract.ShownFrom(at) is not { } from || from > sight.Tick)
            {
                continue;
            }

            if (JumpsFrom(context, contract, at) <= context.Config.AdFreeJumps)
            {
                return false;
            }

            paid = true;
        }

        return paid;
    }
}
