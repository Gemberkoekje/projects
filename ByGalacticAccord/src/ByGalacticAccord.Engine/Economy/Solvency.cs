using System;
using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Engine.Economy;

/// <summary>How an actor stands with its debts (design §5.4, §16.8).</summary>
public enum SolvencyStatus
{
    /// <summary>Nothing wrong: in credit, or owing less than half of what it owns.</summary>
    None,

    /// <summary>Its debt is above half of what it owns: insolvent if that lasts seven days.</summary>
    Warned,

    /// <summary>Insolvent: the Accord clearing house is selling what it owns.</summary>
    Liquidating,

    /// <summary>Liquidated and gone. It stays in the world for the record.</summary>
    Dissolved,
}

/// <summary>
/// Debt and insolvency (design §5.4, §16.8). An actor's balance may fall below ₵0: running costs, fees and interest keep
/// charging (<see cref="SimulationContext.Charge"/>, <see cref="SimulationContext.TransferOwed"/>), and debt costs interest daily
/// (<see cref="ChargeInterest"/>). On every heartbeat <see cref="Review"/> weighs the debt against what the actor owns
/// (<see cref="Assets"/>): above half it is warned, and it becomes insolvent when its debt passes all of it or stays above half
/// for seven days. Then <see cref="Liquidation"/> takes over. The same rules for every actor, the player too.
/// </summary>
public static class Solvency
{
    /// <summary>How long debt may stay above half of what an actor owns before it is insolvent (design §16.8).</summary>
    public const long WarnedHoursLimit = 7 * 24;

    /// <summary>
    /// What an actor owns, at value (design §16.8's "asset value"): its plant (<see cref="Plant"/>), its ships at their value, its goods
    /// wherever they are at their base value, and its own money held in escrow (a payer's price and tax, a performer's bond). Cash is
    /// not counted: an actor in credit owes nothing.
    /// </summary>
    public static Credits Assets(SimulationContext context, ActorState actor)
    {
        decimal plant = Plant(context, actor).Amount;
        decimal ships = actor.Ships.Select(context.Ship).Where(s => !s.Scrapped).Sum(s => s.Stats.Value.Amount);
        decimal goods = Goods.All.Sum(g => actor.Stock(g) * Goods.Info(g).BaseValue);
        foreach (StorageContract storage in context.StoragesOf(actor.Id))
        {
            goods += storage.Lots.Sum(l => l.Quantity * Goods.Info(l.Good).BaseValue);
        }

        foreach (Ship ship in context.Ships.Values)
        {
            foreach (Lot lot in ship.Hold)
            {
                if (lot.Owner == actor.Id)
                {
                    goods += lot.Quantity * Goods.Info(lot.Good).BaseValue;
                }
            }
        }

        decimal escrow = 0m;
        foreach (Contract contract in context.LiveContractsOf(actor.Id))
        {
            if (contract.Status != ContractStatus.Active || contract.EscrowHeld.Amount <= 0m)
            {
                continue;
            }

            if (contract.Payer == actor.Id)
            {
                escrow += contract.EscrowHeld.Amount - contract.LockedBond.Amount;
            }
            else if (contract.Performer == actor.Id)
            {
                escrow += contract.LockedBond.Amount;
            }
        }

        return Credits.Of(Math.Round(plant + ships + goods + escrow, 2));
    }

    /// <summary>
    /// What a business's facility is worth (a farm, a mine, a factory, a yard, a station): what it keeps to run it, its working capital
    /// (<see cref="DecisionEngine.WorkingCapital"/>). Slice 1 sells no facilities, so a facility can't be fire-sold; it counts so that a
    /// business that touches ₵0 with its warehouse empty isn't insolvent at once. Traders, consumers and the player have none: a
    /// trader's capital is its ships.
    /// </summary>
    public static Credits Plant(SimulationContext context, ActorState actor) =>
        actor.Producer is not null || actor.Factory is not null || actor.Yard is not null || actor.Salvage is not null || actor.Station is not null
            ? DecisionEngine.WorkingCapital(context, actor)
            : Credits.Zero;

    /// <summary>
    /// Whether an actor can lock <paramref name="bond"/> in escrow, having first spent <paramref name="spending"/> in cash (design §16.4,
    /// §16.8). The bond is always locked in full; what the actor hasn't got the Accord clearing house lends, as debt, so long as what it
    /// then owes stays within half of what it owns (where a warning would begin). The cash it spends first (goods, a price) it must have:
    /// the clearing house lends a bond, not a purchase. An actor not in business can bond nothing.
    /// </summary>
    public static bool CanBond(SimulationContext context, ActorState actor, decimal bond, decimal spending = 0m)
    {
        if (!actor.InBusiness || actor.Credits.Amount < spending)
        {
            return false;
        }

        decimal after = actor.Credits.Amount - spending - bond;
        if (bond <= 0m || after >= 0m)
        {
            return true;
        }

        if (!context.Config.BondsOnCredit)
        {
            return false;
        }

        // The bond locked counts among what it owns (its money in escrow), so it is added to the assets as it stands now.
        decimal assets = Assets(context, actor).Amount - spending + bond;
        return -after * 2m <= assets;
    }

    /// <summary>
    /// A heartbeat's interest on what an actor owes (design §16.8: "debt costs interest daily"), to the Accord clearing house: a
    /// sink, charged whether or not it can pay. An insolvent actor's interest stops: the clearing house is selling it up.
    /// </summary>
    public static void ChargeInterest(SimulationContext context, ActorState actor) => ChargeInterest(context, actor, context.Config.HeartbeatInterval);

    /// <summary>Interest for <paramref name="hours"/> on what an actor owes (a heartbeat's, unless a test says otherwise).</summary>
    public static void ChargeInterest(SimulationContext context, ActorState actor, long hours)
    {
        if (!actor.InBusiness || actor.Debt.Amount <= 0m)
        {
            return;
        }

        decimal days = hours / 24m;
        decimal interest = Math.Round(actor.Debt.Amount * context.Config.DebtInterestPerDay * days, 2);
        context.Charge(actor, Credits.Of(interest), SinkKind.Interest);
    }

    /// <summary>
    /// Weigh an actor's debt against what it owns (design §16.8): warn it above half, lift the warning at half or less, and declare
    /// it insolvent above all of it or after seven days above half. Returns true if it became insolvent.
    /// </summary>
    public static bool Review(SimulationContext context, ActorState actor)
    {
        if (!actor.InBusiness)
        {
            return false;
        }

        Credits debt = actor.Debt;
        context.NoteDebt(actor);
        if (debt.Amount <= 0m && actor.Solvency == SolvencyStatus.None)
        {
            return false;
        }

        Credits assets = Assets(context, actor);
        long now = context.CurrentTick;
        if (debt > assets)
        {
            Liquidation.Begin(context, actor, debt, assets, InsolvencyReason.DebtAboveAssets);
            return true;
        }

        if (debt.Amount * 2m > assets.Amount)
        {
            if (actor.Solvency == SolvencyStatus.Warned && actor.WarnedSince is { } since && now - since >= WarnedHoursLimit)
            {
                Liquidation.Begin(context, actor, debt, assets, InsolvencyReason.StayedAboveHalf);
                return true;
            }

            if (actor.Solvency != SolvencyStatus.Warned)
            {
                actor.Solvency = SolvencyStatus.Warned;
                actor.WarnedSince = now;
                JournalEntry warning = context.Record(new DebtWarningEntry(now, actor.Id, debt, assets, now + WarnedHoursLimit));
                actor.SolvencyEntry = warning.Seq;
                context.NoteDebtWarning();
                if (actor.IsPlayer)
                {
                    context.RaisePlayerAlert(warning);
                }
            }

            return false;
        }

        if (actor.Solvency == SolvencyStatus.Warned)
        {
            actor.Solvency = SolvencyStatus.None;
            actor.WarnedSince = null;
            JournalEntry lifted = context.Record(new DebtWarningLiftedEntry(now, actor.Id, debt, assets) { Cause = actor.SolvencyEntry });
            actor.SolvencyEntry = lifted.Seq;
            if (actor.IsPlayer)
            {
                context.RaisePlayerAlert(lifted);
            }
        }

        return false;
    }
}
