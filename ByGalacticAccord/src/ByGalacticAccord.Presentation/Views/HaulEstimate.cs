using System;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Economy;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Presentation.Views;

/// <summary>
/// What a haul is worth to the player's ship, computed with the engine's own rules and formulas
/// (design §11.2, §11.5): the fee, minus the costs the game knows for certain if nothing goes wrong
/// (running costs, docking, goods the pilot must pay for, a warranty refund the trip's length makes
/// certain, a late fee the arrival time makes certain). The incident odds stay separate, as the
/// uncertain input. It never collapses into one "expected value".
/// </summary>
public sealed record HaulEstimate(
    Ship Ship,
    LocationId StartAt,
    long StartTick,
    RoutePlan? Repositioning,
    long RepoTicks,
    RoutePlan Laden,
    long LadenTicks,
    Credits Fee,
    Credits Running,
    Credits Docking,
    Credits GoodsFronted,
    decimal ArrivalQuality,
    Credits QualityRefund,
    long TicksLate,
    Credits LateFee,
    Credits Bond,
    decimal IncidentOdds)
{
    public Credits TakeHome => Fee - Running - Docking - GoodsFronted - QualityRefund - LateFee;

    public long PickupTick => StartTick + RepoTicks;

    public long ArrivalTick => PickupTick + LadenTicks;

    /// <summary>How long the haul ties the ship up, from now: any wait until it is free, the empty leg and the laden one.</summary>
    public long TiedUpTicks(long now) => ArrivalTick - now;

    /// <summary>Take-home per game day of the ship's time.</summary>
    public Credits PerDay(long now)
    {
        long ticks = Math.Max(1, TiedUpTicks(now));
        return Credits.Of(TakeHome.Amount * GameTime.HoursPerDay / ticks);
    }

    public DangerBand Band => Laden.Band;

    /// <summary>
    /// The estimate for flying <paramref name="contract"/> with the player's ship, from where and when the
    /// ship is next free. Null if no route connects the places involved.
    /// </summary>
    public static HaulEstimate? For(SimulationContext context, ActorState player, Ship ship, Contract contract)
    {
        (LocationId startAt, long startTick) = NextFree(context, ship);

        RoutePlan? laden = context.FindPath(contract.OriginLocationId, contract.DestinationLocationId);
        if (laden is null)
        {
            return null;
        }

        RoutePlan? repo = null;
        long repoTicks = 0;
        if (startAt != contract.OriginLocationId)
        {
            repo = context.FindPath(startAt, contract.OriginLocationId);
            if (repo is null)
            {
                return null;
            }

            repoTicks = PriceModel.TravelTicks(repo.DistanceTicks, ship.Class.SpeedMultiplier);
        }

        ShipClassInfo cls = ship.Class;
        long ladenTicks = Math.Max(1, PriceModel.TravelTicks(laden.DistanceTicks, cls.SpeedMultiplier));
        Credits fee = contract.OfferedPayment;
        Credits running = Credits.Of(cls.RunningCostPerTick.Amount * (repoTicks + ladenTicks));
        Credits docking = context.Config.DockingFee;
        Credits goods = contract.UsesEscrow ? Credits.Zero : contract.GoodsCost;

        // Perishables decay over the laden leg, slowed by refrigeration; the warranty refunds part of the fee below its minimum.
        decimal quality = Cargo.Fresh(contract.Good, contract.Quantity).Decayed(ladenTicks, cls.Preservation).Quality;
        decimal refundFraction = contract.Warranty?.RefundFraction(quality) ?? 0m;
        Credits refund = Credits.Of(fee.Amount * refundFraction);

        // The bond is what signing would lock (escrow only); a late fee can draw no more than the bond.
        Credits bond = contract.UsesEscrow
            ? Credits.Of(Math.Min(contract.PerformanceBond.Amount, Math.Max(0m, player.Credits.Amount)))
            : Credits.Zero;
        long arrival = startTick + repoTicks + ladenTicks;
        long ticksLate = Math.Max(0, arrival - contract.DeadlineTick);
        Credits lateFee = contract.LatePenalty?.PenaltyFor(ticksLate) ?? Credits.Zero;
        if (lateFee > bond)
        {
            lateFee = bond;
        }

        decimal odds = Math.Clamp(context.Config.IncidentProbabilityScale * laden.MaxDanger * cls.IncidentChanceModifier, 0m, 1m);

        return new HaulEstimate(
            ship, startAt, startTick, repo, repoTicks, laden, ladenTicks,
            fee, running, docking, goods, quality, refund, ticksLate, lateFee, bond, odds);
    }

    /// <summary>Where and when a ship is next free: now where it sits, or when and where its current haul ends.</summary>
    public static (LocationId At, long Tick) NextFree(SimulationContext context, Ship ship)
    {
        long now = context.CurrentTick;
        if (ship.Status == ShipStatus.Docked || ship.ActiveContract is not { } active)
        {
            return (ship.Location, now);
        }

        Contract contract = context.Contract(active);
        if (ship.Manifest is not null && ship.Course is { } laden)
        {
            return (contract.DestinationLocationId, laden.ArrivalTick);
        }

        // Signed but not yet loaded: the empty leg (if any), then the laden one.
        long ladenTicks = 0;
        if (context.FindPath(contract.OriginLocationId, contract.DestinationLocationId) is { } plan)
        {
            ladenTicks = Math.Max(1, PriceModel.TravelTicks(plan.DistanceTicks, ship.Class.SpeedMultiplier));
        }

        long pickup = ship.Course?.ArrivalTick ?? now;
        return (contract.DestinationLocationId, pickup + ladenTicks);
    }
}
