using System;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Events;

namespace ByGalacticAccord.Engine.Simulation;

public static partial class Market
{
    /// <summary>Whether a contract's poster is the side that is paid: a lot for sale, or a ship for sale. Otherwise the poster pays.</summary>
    public static bool PosterSells(Contract contract) => contract is SaleContract { Side: SaleSide.ForSale } or ShipSaleContract;

    // ---- Builds (design §3.2, §6.3) ----

    /// <summary>
    /// Post a Build: a ship of <paramref name="spec"/>'s parts, at the grade it names, handed over at the yard that takes it by
    /// <paramref name="deadline"/>. It is shown at <paramref name="place"/> (design A.2, settled in S7: a Build goes on the board
    /// where its poster posts it) and further where its poster pays (D38). Its jurisdiction is the yard's, set when a yard signs
    /// it; until then it reads as the place's. AI yards that see it bid on their heartbeat; the player's post reaches the yards
    /// that can see it, which answer over game time (<see cref="DecisionEngine.NoticePost"/>).
    /// </summary>
    public static BuildContract PostBuild(
        SimulationContext context, ActorState client, ShipParts spec, LocationId place, Credits price, Credits reservation, long deadline,
        TermPackage? package = null, decimal lateFeeScale = 1m)
    {
        Location location = context.Location(place);
        var build = new BuildContract(
            context.NextContractId(), client.Id, context.CurrentTick, spec, place, price, reservation,
            Terms.TaxRate(context, location.Reach), deadline, location.Reach, location.Region,
            Terms.ForBuild(package ?? Terms.PackageFor(client), price, lateFeeScale));
        context.Add(build);
        context.Schedule(new ContractExpiryCheck(deadline, build.Id));
        context.Record(new ContractPostedEntry(context.CurrentTick, build.Id, client.Id));
        if (HagglesInGameTime(client))
        {
            DecisionEngine.NoticePost(context, build);
        }

        return build;
    }

    /// <summary>
    /// A yard takes a Build: it is performed under the yard's jurisdiction from now on (escrow and tax there); the client pays
    /// the deposit to the yard and locks the balance and the tax in escrow, the yard its bond (design §16.4); the build joins
    /// the yard's queue, behind the ship it is building (design D35: one ship at a time).
    /// </summary>
    public static Signing TakeBuild(SimulationContext context, ActorState yard, BuildContract build, Credits price)
    {
        if (yard.Yard is null || !build.IsOpen)
        {
            return build.IsOpen ? Signing.CannotPerform : NotOpen(context, yard, build);
        }

        (AccordReach reach, string region, decimal tax) = (build.Jurisdiction, build.Region, build.TaxRate);
        Location at = context.Location(yard.Home);
        build.PerformUnder(at.Reach, at.Region, Terms.TaxRate(context, at.Reach));
        Signing signed = Sign(context, build, yard, price);
        if (signed != Signing.Signed)
        {
            build.PerformUnder(reach, region, tax);
            return signed;
        }

        build.BuiltAt(yard.Home);
        if (yard.Works.SpecSells == false)
        {
            yard.Works.SpecSells = null; // someone ordered a ship: there is demand again, worth a ship on spec once more
        }

        yard.Works.Queue.Add(new YardJob(build.Id, build.Spec, context.CurrentTick));
        context.NoteBuildSigned();
        AwaitPerformance(context, build);
        return Signing.Signed;
    }

    /// <summary>
    /// The client answers a yard's supply-failure proposal (design §3.4): more time, with the compensation paid now; a part a
    /// grade lower, at the discount, by an earlier deadline; or cancellation, the deposit and escrow back with compensation.
    /// False if there is no proposal waiting, or it isn't the client's to answer.
    /// </summary>
    public static bool ChooseRemedy(SimulationContext context, ActorState client, BuildContract build, Remedy remedy)
    {
        if (build.Proposal is not { } proposal || build.Status != ContractStatus.Active || build.Client != client.Id
            || (remedy == Remedy.Substitute && proposal.SubstituteDiscount.Amount <= 0m))
        {
            return false;
        }

        ActorState yard = context.Actor(build.Builder!.Value);
        build.NoteRemedy(remedy);
        context.NoteRemedy(remedy);
        JournalEntry chosen = context.Record(new RemedyChosenEntry(context.CurrentTick, build.Id, client.Id, remedy));
        switch (remedy)
        {
            case Remedy.Extend:
                build.Amend(proposal.ExtendedDeadline, build.Terms);
                context.TransferUpTo(yard, client, proposal.Compensation);
                AwaitPerformance(context, build);
                break;
            case Remedy.Substitute:
                build.Substitute(proposal.Slot, proposal.SubstituteGrade, proposal.SubstituteDiscount);
                build.Amend(proposal.SubstituteDeadline, build.Terms);
                AwaitPerformance(context, build);
                break;
            default:
                Settlement.Cancel(context, build, client, CancelReason.SupplyFailure, chosen.Seq); // pays the proposal's compensation
                break;
        }

        build.Propose(null);
        return true;
    }

    // ---- ship Sales (design §3.2, §6.3: stock and second-hand) ----

    /// <summary>
    /// Post a ship for sale where it is docked, free and empty, on the seller's package. It takes no work while posted, and
    /// changes hands the moment someone signs.
    /// </summary>
    public static ShipSaleContract PostShipSale(
        SimulationContext context, ActorState seller, Ship ship, Credits price, Credits reservation, long deadline, long? cause = null)
    {
        if (ship.Owner != seller.Id || !ship.IsFree || ship.HoldQuantity > 0m)
        {
            throw new InvalidOperationException($"{ship.Id} isn't free, empty and {seller.Name}'s to sell.");
        }

        Location location = context.Location(ship.Location);
        var sale = new ShipSaleContract(
            context.NextContractId(), seller.Id, context.CurrentTick, ship.Id, ship.Location, price, reservation,
            Terms.TaxRate(context, location.Reach), deadline, location.Reach, location.Region, Terms.ForShipSale(Terms.PackageFor(seller)));
        ship.List(sale.Id);
        context.Add(sale);
        context.Schedule(new ContractExpiryCheck(deadline, sale.Id));
        context.Record(new ContractPostedEntry(context.CurrentTick, sale.Id, seller.Id) { Cause = cause });
        if (HagglesInGameTime(seller))
        {
            DecisionEngine.NoticePost(context, sale);
        }

        return sale;
    }

    /// <summary>The buyer takes a ship for sale: it signs, pays (through escrow in Accord space, directly outside it), and the ship is its.</summary>
    public static Signing BuyShip(SimulationContext context, ActorState buyer, ShipSaleContract sale, Credits price)
    {
        Signing signed = Sign(context, sale, buyer, price);
        if (signed == Signing.Signed)
        {
            Settlement.HandOverShip(context, sale);
        }

        return signed;
    }

    /// <summary>
    /// A ship flies empty to <paramref name="to"/> for its owner, under no contract (a ferry): a salvage yard bringing home a ship it
    /// bought. It is busy until it gets there, and no incident strikes an empty leg.
    /// </summary>
    public static void Ferry(SimulationContext context, Ship ship, LocationId to)
    {
        if (!ship.IsFree || ship.Location == to)
        {
            return;
        }

        Dispatch(context, ship, new ShipJob(ship.Owner, GoodType.Food, 0m, to, PickupSource.Aboard, Destination: null));
    }
}
