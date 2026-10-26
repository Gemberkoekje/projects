using System.Linq;
using ByGalacticAccord.Engine.Actors;
using ByGalacticAccord.Engine.Domain;
using ByGalacticAccord.Engine.Simulation;

namespace ByGalacticAccord.Tests;

/// <summary>
/// The structured terms and their packages (design §3.4): what each package sets, that every figure scales from the
/// posted price and never from the hidden reservation, and how posters choose a package.
/// </summary>
public sealed class TermsTests
{
    [Theory]
    [InlineData(TermPackage.Strict, 6, 10.0, 250.0, 200.0, PartialDelivery.Failure, 0.8, 1.0, 0.6)]
    [InlineData(TermPackage.Standard, 24, 5.0, 150.0, 100.0, PartialDelivery.ProRata, 0.7, 0.5, 0.45)]
    [InlineData(TermPackage.Lenient, 72, 2.5, 80.0, 50.0, PartialDelivery.ProRata, 0.6, 0.3, null)]
    public void A_sales_terms_follow_its_package_and_scale_from_the_price(
        TermPackage package, long grace, double perHour, double cap, double breakFee, PartialDelivery partial, double min, double refund, double? floor)
    {
        ContractTerms terms = Terms.ForSale(package, GoodType.Food, Credits.Of(1000m));

        Assert.Equal(package, terms.Package);
        Assert.Equal(grace, terms.GraceHours);
        Assert.Equal(new PenaltyOnLateDelivery((decimal)perHour, Credits.Of((decimal)cap)), terms.LateFee);
        Assert.Equal(terms.LateFee!.MaxPenalty, terms.Bond); // the bond is the late fee's cap (design §16.4)
        Assert.Equal(Credits.Of((decimal)breakFee), terms.BreakFee);
        Assert.Equal(partial, terms.Partial);
        Assert.Equal(new QualityWarranty((decimal)min, (decimal)refund, (decimal?)floor), terms.Warranty);
        Assert.Null(terms.Liability);

        // Only perishables carry a warranty.
        Assert.Null(Terms.ForSale(package, GoodType.Ore, Credits.Of(1000m)).Warranty);
    }

    [Fact]
    public void A_hauls_liability_is_for_its_goods_at_their_declared_value()
    {
        ContractTerms standard = Terms.ForHaul(TermPackage.Standard, GoodType.Machinery, Credits.Of(200m), 20m, Credits.Of(55m));
        ContractTerms strict = Terms.ForHaul(TermPackage.Strict, GoodType.Machinery, Credits.Of(200m), 20m, Credits.Of(55m));

        Assert.Equal(new CargoLiability(Credits.Of(55m), Credits.Of(550m)), standard.Liability); // half the goods' value
        Assert.Equal(new CargoLiability(Credits.Of(55m), Credits.Of(1100m)), strict.Liability); // all of it
        Assert.Null(Terms.ForHaul(TermPackage.Lenient, GoodType.Machinery, Credits.Of(200m), 20m, Credits.Of(55m)).Liability);
        Assert.Equal(Credits.Of(110m), standard.Liability!.For(2m));
        Assert.Equal(Credits.Of(550m), standard.Liability.For(20m));
        Assert.Equal(Credits.Of(60m), standard.Bond); // 30% of the fee
    }

    [Fact]
    public void A_haul_for_a_sale_declares_the_sales_unit_price_and_any_other_the_base_value()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        ActorState farm = context.Actor("Verdant Agricombine");
        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 60m, 900m, hours: 200);
        Assert.Equal(Signing.Signed, Market.Supply(context, farm, wanted, wanted.OfferedPrice, Holding.StockAt(farm.Home)));
        HaulContract forSale = Market.PostHaul(
            context, farm, GoodType.Food, 60m, Holding.StockAt(farm.Home), habs.Home, Credits.Of(80m), Credits.Of(80m), wanted.DeadlineTick, wanted.Id, TermPackage.Standard);
        HaulContract plain = PlayerActionsTests.FarmHaul(context);

        Assert.Equal(Credits.Of(15m), forSale.Terms.Liability!.PerUnit); // ₵900 for 60
        Assert.Equal(Credits.Of(Goods.Info(GoodType.Food).BaseValue), plain.Terms.Liability!.PerUnit);
    }

    [Fact]
    public void Terms_give_nothing_away_about_the_hidden_reservation()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        SaleContract low = Market.PostSale(
            context, habs, SaleSide.Wanted, GoodType.Food, 50m, habs.Home, Credits.Of(700m), Credits.Of(710m), context.CurrentTick + 200, Holding.StockAt(habs.Home), TermPackage.Standard);
        SaleContract high = Market.PostSale(
            context, habs, SaleSide.Wanted, GoodType.Food, 50m, habs.Home, Credits.Of(700m), Credits.Of(2000m), context.CurrentTick + 200, Holding.StockAt(habs.Home), TermPackage.Standard);

        Assert.Equal(low.Terms, high.Terms);
    }

    [Fact]
    public void A_lot_in_storage_is_warranted_as_seen()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState sable = context.Actor("Sable Vance");
        ActorState farm = context.Actor("Verdant Agricombine");
        context.PutInStorage(sable, sable.Home, context.TakeFromStock(farm, GoodType.Food, 30m, reserved: false) with { Quality = 0.5m });

        SaleContract lot = Market.PostSale(
            context, sable, SaleSide.ForSale, GoodType.Food, 30m, sable.Home, Credits.Of(90m), Credits.Of(80m), context.CurrentTick + 96,
            Holding.StorageAt(sable.Home), TermPackage.Standard);
        SaleContract fresh = Market.PostSale(
            context, farm, SaleSide.ForSale, GoodType.Food, 30m, farm.Home, Credits.Of(90m), Credits.Of(80m), context.CurrentTick + 96,
            Holding.StockAt(farm.Home), TermPackage.Standard);

        // No more than the quality the goods will still have at the lot's deadline, the floor scaled with it.
        decimal seen = System.Math.Round(new Cargo(GoodType.Food, 1m, 0.5m).Decayed(96, 0m).Quality, 2);
        Assert.Equal(new QualityWarranty(seen, 0.5m, System.Math.Round(0.45m * seen / 0.7m, 2)), lot.Warranty);
        Assert.Equal(new QualityWarranty(0.7m, 0.5m, 0.45m), fresh.Warranty); // a warehouse's stock keeps
    }

    [Fact]
    public void Posters_pick_a_package_by_personality_and_the_player_posts_standard()
    {
        var careful = new ActorState(new ActorId(1), "Careful", ActorRole.Consumer, new LocationId(1), Personality.Balanced with { QualityBias = 0.8m, RiskTolerance = 0.2m }, Credits.Zero);
        var careless = new ActorState(new ActorId(2), "Careless", ActorRole.Consumer, new LocationId(1), Personality.Balanced with { QualityBias = 0.2m, RiskTolerance = 0.9m }, Credits.Zero);
        var player = new ActorState(new ActorId(3), "You", ActorRole.IndependentPilot, new LocationId(1), careful.Personality, Credits.Zero) { IsPlayer = true };

        Assert.Equal(TermPackage.Strict, Terms.PackageFor(careful));
        Assert.Equal(TermPackage.Lenient, Terms.PackageFor(careless));
        Assert.Equal(TermPackage.Standard, Terms.PackageFor(new ActorState(new ActorId(4), "Middling", ActorRole.Consumer, new LocationId(1), Personality.Balanced, Credits.Zero)));
        Assert.Equal(TermPackage.Standard, Terms.PackageFor(player));
    }

    [Fact]
    public void A_buyer_relaxes_terms_no_standard_hold_could_meet_but_keeps_a_floor_where_only_refrigeration_could()
    {
        // Food from Verdant to Edge Station, 220 h in a standard hold, arrives 61% fresh: only lenient terms are met by it.
        decimal frontierFood = Cargo.Fresh(GoodType.Food, 1m).Decayed(220, 0m).Quality;
        Assert.Equal(TermPackage.Lenient, Terms.Meetable(TermPackage.Strict, GoodType.Food, frontierFood));
        Assert.Equal(TermPackage.Standard, Terms.Meetable(TermPackage.Strict, GoodType.Food, Cargo.Fresh(GoodType.Food, 1m).Decayed(140, 0m).Quality));
        Assert.Equal(TermPackage.Strict, Terms.Meetable(TermPackage.Strict, GoodType.Food, Cargo.Fresh(GoodType.Food, 1m).Decayed(30, 0m).Quality));

        // Medicine spoils on any long run in a standard hold: only a refrigerated hold will do, so a floor stays to turn spoiled goods away.
        decimal longMedicine = Cargo.Fresh(GoodType.Medicine, 1m).Decayed(180, 0m).Quality;
        Assert.Equal(TermPackage.Strict, Terms.Meetable(TermPackage.Strict, GoodType.Medicine, longMedicine));
        Assert.Equal(TermPackage.Standard, Terms.Meetable(TermPackage.Lenient, GoodType.Medicine, longMedicine));

        // Goods that keep aren't affected.
        Assert.Equal(TermPackage.Strict, Terms.Meetable(TermPackage.Strict, GoodType.Ore, 0m));
    }

    [Fact]
    public void The_grace_ends_after_the_deadline_and_only_then_is_a_signed_contract_in_default()
    {
        SimulationContext context = TestWorld.Quiet();
        ActorState habs = context.Actor("Meridian Habs");
        ActorState farm = context.Actor("Verdant Agricombine");
        SaleContract wanted = context.PostWanted(habs, GoodType.Food, 60m, 900m, hours: 100, TermPackage.Lenient);
        Assert.Equal(wanted.DeadlineTick + 72, wanted.GraceEndsTick);
        Assert.False(wanted.InDefault(wanted.GraceEndsTick)); // open, not signed

        Market.FillViaHaul(context, farm, wanted, wanted.OfferedPrice, FillSource.FromStock(farm), Credits.Of(5m), Credits.Of(5m));
        Assert.False(wanted.InDefault(wanted.GraceEndsTick - 1));
        Assert.True(wanted.InDefault(wanted.GraceEndsTick));
    }
}
