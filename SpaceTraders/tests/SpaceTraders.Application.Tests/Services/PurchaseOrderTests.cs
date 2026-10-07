using FluentAssertions;
using NSubstitute;
using SpaceTraders.Application.Automation;
using SpaceTraders.Application.Interfaces.Repositories;
using SpaceTraders.Application.Services;
using SpaceTraders.Domain.Enums;

namespace SpaceTraders.Application.Tests.Services;

/// <summary>
/// Slice 6.10b (D43): ships are bought in a fixed order. Asked on 2026-10-03: "I'd like at least 1 drone per mineral that
/// is scarce or limited, then save up for cargo ships, then a mix based on if the minerals aren't going above LIMITED", the
/// mix being "Alternate drones and cargo ships, but probes first"; a designated surveyor comes first (D47), and the
/// contract's drone stays before everything (D23, D40).
/// </summary>
public sealed class PurchaseOrderTests
{
    private const string List = "SHIP_LIGHT_SHUTTLE,SHIP_LIGHT_HAULER,SHIP_LIGHT_HAULER";

    /// <summary>When the ledger's purchases were made: before anything this process buys during a test.</summary>
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddDays(-1);

    private readonly PurchaseNeeds _needs = new();
    private readonly ISettingsRepository _settings = Substitute.For<ISettingsRepository>();
    private readonly ILedgerRepository _ledger = Substitute.For<ILedgerRepository>();
    private readonly LogRecorder _log = new();

    public PurchaseOrderTests()
    {
        foreach (var plan in PurchaseOrder.BuyingPlans.Keys)
        {
            PlanIs(plan, on: true);
        }

        _settings.GetAsync<string>(TradingAutomationService.ShipPurchasesSetting, Arg.Any<CancellationToken>()).Returns(List);
        LedgerHolds();
    }

    [Fact]
    public async Task TheContractsDrone_ComesFirst()
    {
        EveryoneSays(PurchaseNeed.None);
        _needs.Report(AutomationPlan.Survey, Need(PurchaseTier.Surveyor, "SHIP_SURVEYOR"), DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.Contract, Need(PurchaseTier.Contract, "SHIP_MINING_DRONE"))).Should().BeTrue();
    }

    [Fact]
    public async Task AProbe_WaitsWhileACargoShipOfTheListIsStillToBuy_SoTheCreditsAreSavedUpForIt()
    {
        // "then save up for cargo ships": while a ship in Trade.ShipPurchases is still to buy, no probe is bought.
        EveryoneSays(PurchaseNeed.None);
        _needs.Report(AutomationPlan.Trading, Need(PurchaseTier.CargoShips, "SHIP_LIGHT_SHUTTLE"), DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.ProbeDeployment, Need(PurchaseTier.Probes, "SHIP_PROBE"))).Should().BeFalse();

        _needs.Report(AutomationPlan.Trading, PurchaseNeed.None, DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.ProbeDeployment, Need(PurchaseTier.Probes, "SHIP_PROBE"))).Should().BeTrue();
    }

    [Fact]
    public async Task UntilAPlanThatIsOnHasSaidWhatItNeeds_NothingItCouldNeedEarlierIsPassed()
    {
        // After a start the probe plan runs before the survey, mining, siphon and trading plans; it waits for their first
        // word, as any of them could need something that comes first.
        _needs.Report(AutomationPlan.Contract, PurchaseNeed.None, DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.ProbeDeployment, Need(PurchaseTier.Probes, "SHIP_PROBE"))).Should().BeFalse();
        (await MayBuyAsync(AutomationPlan.Survey, Need(PurchaseTier.Surveyor, "SHIP_SURVEYOR"))).Should().BeTrue("only the contract plan could need something earlier");
    }

    [Fact]
    public async Task APlanThatIsOff_DoesNotHoldAnyoneBack()
    {
        EveryoneSays(PurchaseNeed.None);
        _needs.Report(AutomationPlan.Trading, Need(PurchaseTier.CargoShips, "SHIP_LIGHT_SHUTTLE"), DateTimeOffset.UtcNow);
        PlanIs(AutomationPlan.Trading, on: false);
        PlanIs(AutomationPlan.Siphon, on: false);
        _needs.Report(AutomationPlan.Siphon, PurchaseNeed.None, DateTimeOffset.UtcNow.AddHours(-1));

        (await MayBuyAsync(AutomationPlan.ProbeDeployment, Need(PurchaseTier.Probes, "SHIP_PROBE"))).Should().BeTrue();
    }

    [Fact]
    public async Task AfterAPause_APlanNotHeardFromLately_HoldsBackWhatComesAfterIt_UntilItSaysAgain()
    {
        // A 502 pauses every plan for 3 minutes. The probe plan runs early in the first tick after it, when the trading
        // plan's word is older than its lifetime: the shuttle it saves up for still comes first.
        EveryoneSays(PurchaseNeed.None);
        _needs.Report(AutomationPlan.Trading, Need(PurchaseTier.CargoShips, "SHIP_LIGHT_SHUTTLE"), DateTimeOffset.UtcNow - PurchaseNeeds.Lifetime - TimeSpan.FromSeconds(1));

        (await MayBuyAsync(AutomationPlan.ProbeDeployment, Need(PurchaseTier.Probes, "SHIP_PROBE"))).Should().BeFalse();
        _log.Entries.Should().Contain(entry => entry.Message.Contains("the Trading plan, not heard from lately", StringComparison.Ordinal));

        _needs.Report(AutomationPlan.Trading, PurchaseNeed.None, DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.ProbeDeployment, Need(PurchaseTier.Probes, "SHIP_PROBE"))).Should().BeTrue();
    }

    [Fact]
    public async Task TheMiningAndSiphonPlansDrones_ForScarceMinerals_DoNotWaitForEachOther()
    {
        EveryoneSays(PurchaseNeed.None);
        _needs.Report(AutomationPlan.Siphon, Need(PurchaseTier.Coverage, "SHIP_SIPHON_DRONE"), DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.Mining, Need(PurchaseTier.Coverage, "SHIP_MINING_DRONE"))).Should().BeTrue();
    }

    [Fact]
    public async Task ADroneForAScarceMineral_ComesBeforeTheCargoShips_AndTheSurveyorBeforeThat()
    {
        EveryoneSays(PurchaseNeed.None);
        _needs.Report(AutomationPlan.Mining, Need(PurchaseTier.Coverage, "SHIP_MINING_DRONE"), DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.Trading, Need(PurchaseTier.CargoShips, "SHIP_LIGHT_SHUTTLE"))).Should().BeFalse();

        _needs.Report(AutomationPlan.Survey, Need(PurchaseTier.Surveyor, "SHIP_SURVEYOR"), DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.Mining, Need(PurchaseTier.Coverage, "SHIP_MINING_DRONE"))).Should().BeFalse();
        _log.Entries.Should().Contain(entry => entry.Message.Contains("waits for the Survey plan's SHIP_SURVEYOR (Surveyor)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheJumpGatesMaterials_ComeAfterTheCargoShips_AndTheProbesWaitForThem()
    {
        // Slice 6.6 (D64): supplying pays nothing back, so a load is judged as a purchase, after the cargo ships of the list;
        // while the gate needs materials the probes and further ships wait.
        EveryoneSays(PurchaseNeed.None);
        _needs.Report(AutomationPlan.Trading, Need(PurchaseTier.CargoShips, "SHIP_LIGHT_HAULER"), DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.Construction, Need(PurchaseTier.Construction, "FAB_MATS"))).Should().BeFalse();

        _needs.Report(AutomationPlan.Trading, PurchaseNeed.None, DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.Construction, Need(PurchaseTier.Construction, "FAB_MATS"))).Should().BeTrue();
        (await MayBuyAsync(AutomationPlan.ProbeDeployment, Need(PurchaseTier.Probes, "SHIP_PROBE"))).Should().BeFalse();
        (await MayBuyAsync(AutomationPlan.Mining, Need(PurchaseTier.Alternating, "SHIP_MINING_DRONE"))).Should().BeFalse();

        _needs.Report(AutomationPlan.Construction, PurchaseNeed.None, DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.ProbeDeployment, Need(PurchaseTier.Probes, "SHIP_PROBE"))).Should().BeTrue();
    }

    [Fact]
    public async Task TheGatesMiners_WaitForALoadThatCanBeBought_AndAreBoughtWhileItWaitsForItsMarkets()
    {
        // Slice 6.25 (D92), asked on 2026-10-05: "Same tier as gate loads, capped. If the gate can be built, it should be
        // built, otherwise extra miners can be built." The probes wait behind either.
        EveryoneSays(PurchaseNeed.None);
        _needs.Report(AutomationPlan.Construction, Need(PurchaseTier.Construction, "FAB_MATS"), DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.Mining, Need(PurchaseTier.Construction, "SHIP_MINING_DRONE"))).Should().BeFalse();
        _log.Entries.Should().Contain(entry => entry.Message.Contains("waits for the Construction plan's FAB_MATS (Construction)", StringComparison.Ordinal));

        _needs.Report(AutomationPlan.Construction, Need(PurchaseTier.Construction, "FAB_MATS") with { WaitsForMarkets = true }, DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.Mining, Need(PurchaseTier.Construction, "SHIP_MINING_DRONE"))).Should().BeTrue();
        (await MayBuyAsync(AutomationPlan.ProbeDeployment, Need(PurchaseTier.Probes, "SHIP_PROBE"))).Should().BeFalse();
    }

    [Fact]
    public async Task TheGatesMiners_NeverHoldTheGatesLoadBack()
    {
        EveryoneSays(PurchaseNeed.None);
        _needs.Report(AutomationPlan.Mining, Need(PurchaseTier.Construction, "SHIP_MINING_DRONE"), DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.Construction, Need(PurchaseTier.Construction, "FAB_MATS"))).Should().BeTrue();
    }

    [Fact]
    public async Task UntilTheConstructionPlanHasSaidWhatItNeeds_TheGatesMinersWait()
    {
        // After a start the mining plan runs before the construction plan, whose load could be one it may buy now (D92).
        EveryoneSays(PurchaseNeed.None);
        _needs.Report(AutomationPlan.Construction, PurchaseNeed.None, DateTimeOffset.UtcNow - PurchaseNeeds.Lifetime - TimeSpan.FromSeconds(1));

        (await MayBuyAsync(AutomationPlan.Mining, Need(PurchaseTier.Construction, "SHIP_MINING_DRONE"))).Should().BeFalse();
    }

    [Fact]
    public async Task ASecondSurveyor_ComesAfterTheDronesForScarceMinerals_AndBeforeTheCargoShips()
    {
        // D55, asked on 2026-10-03: "The second surveyor is lower priority than the first on the buy order": after the drones
        // per scarce mineral and area, before the cargo ships.
        EveryoneSays(PurchaseNeed.None);
        _needs.Report(AutomationPlan.Mining, Need(PurchaseTier.Coverage, "SHIP_MINING_DRONE"), DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.Survey, Need(PurchaseTier.SurveyorPerArea, "SHIP_SURVEYOR"))).Should().BeFalse();

        _needs.Report(AutomationPlan.Mining, PurchaseNeed.None, DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.Trading, Need(PurchaseTier.CargoShips, "SHIP_LIGHT_SHUTTLE"))).Should().BeFalse();
        (await MayBuyAsync(AutomationPlan.Survey, Need(PurchaseTier.SurveyorPerArea, "SHIP_SURVEYOR"))).Should().BeTrue();
    }

    [Fact]
    public async Task OnceEverythingElseIsBought_DronesAndCargoShipsTakeTurns_ADroneFirst()
    {
        // The list's last cargo ship was bought, then nothing: a drone's turn.
        LedgerHolds(
            ("SHIP-3", ShipType.ShipMiningDrone),
            ("SHIP-4", ShipType.ShipLightShuttle),
            ("SHIP-5", ShipType.ShipLightHauler),
            ("SHIP-6", ShipType.ShipSiphonDrone),
            ("SHIP-7", ShipType.ShipLightHauler));
        EveryoneSays(PurchaseNeed.None);
        _needs.Report(AutomationPlan.Trading, Need(PurchaseTier.Alternating, "SHIP_LIGHT_HAULER"), DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.Mining, Need(PurchaseTier.Alternating, "SHIP_MINING_DRONE"))).Should().BeTrue();
        (await MayBuyAsync(AutomationPlan.Trading, Need(PurchaseTier.Alternating, "SHIP_LIGHT_HAULER"))).Should().BeFalse("the drone's turn comes first");
    }

    [Fact]
    public async Task AfterADrone_ACargoShipsTurn_AndAPurchaseCountsAtOnce_ThoughTheLedgerHasNotGotItYet()
    {
        // The mining plan bought a drone this tick; the siphon plan, a moment later in the same tick, must not buy
        // another: the ledger's row comes after the purchase.
        LedgerHolds(
            ("SHIP-4", ShipType.ShipLightShuttle),
            ("SHIP-5", ShipType.ShipLightHauler),
            ("SHIP-7", ShipType.ShipLightHauler));
        EveryoneSays(PurchaseNeed.None);
        _needs.Report(AutomationPlan.Trading, Need(PurchaseTier.Alternating, "SHIP_LIGHT_HAULER"), DateTimeOffset.UtcNow);
        (await MayBuyAsync(AutomationPlan.Mining, Need(PurchaseTier.Alternating, "SHIP_MINING_DRONE"))).Should().BeTrue();

        _needs.Bought("SHIP-8", ShipType.ShipMiningDrone, DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.Siphon, Need(PurchaseTier.Alternating, "SHIP_SIPHON_DRONE"))).Should().BeFalse();
        (await MayBuyAsync(AutomationPlan.Trading, Need(PurchaseTier.Alternating, "SHIP_LIGHT_HAULER"))).Should().BeTrue();
        await _ledger.ReceivedWithAnyArgs(1).GetRangeAsync();
    }

    [Fact]
    public async Task AProbeBeyondTheTradeReach_WaitsForTheDronesAndCargoShipsThatTakeTurns()
    {
        // Slice 6.28 (D97): "Trade reach as a priority, all explored markets when money allows", the others "After
        // drones/cargo". A probe for home or a system within the reach still comes before them.
        EveryoneSays(PurchaseNeed.None);
        _needs.Report(AutomationPlan.Trading, Need(PurchaseTier.Alternating, "SHIP_LIGHT_HAULER"), DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.ProbeDeployment, Need(PurchaseTier.FarProbes, "SHIP_PROBE"))).Should().BeFalse();
        (await MayBuyAsync(AutomationPlan.ProbeDeployment, Need(PurchaseTier.Probes, "SHIP_PROBE"))).Should().BeTrue();

        _needs.Report(AutomationPlan.Trading, PurchaseNeed.None, DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.ProbeDeployment, Need(PurchaseTier.FarProbes, "SHIP_PROBE"))).Should().BeTrue();
    }

    [Fact]
    public async Task TheFirstExplorer_ComesAfterTheGatesLoads_AndBeforeTheProbes()
    {
        // Slice 6.30, D98: "Start with one before probes", after the jump gate's loads (D64).
        EveryoneSays(PurchaseNeed.None);
        _needs.Report(AutomationPlan.Explore, Need(PurchaseTier.Explorer, "SHIP_EXPLORER"), DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.ProbeDeployment, Need(PurchaseTier.Probes, "SHIP_PROBE"))).Should().BeFalse("the credits are saved up for the explorer");
        (await MayBuyAsync(AutomationPlan.Construction, Need(PurchaseTier.Construction, "FAB_MATS"))).Should().BeTrue("the gate's loads come first");
        (await MayBuyAsync(AutomationPlan.Explore, Need(PurchaseTier.Explorer, "SHIP_EXPLORER"))).Should().BeFalse("the gate's load waits to be bought");

        _needs.Report(AutomationPlan.Construction, PurchaseNeed.None, DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.Explore, Need(PurchaseTier.Explorer, "SHIP_EXPLORER"))).Should().BeTrue();
    }

    [Fact]
    public async Task AFurtherExplorer_ComesBeforeTheProbes_AndTheDronesAndCargoShipsThatTakeTurns()
    {
        // Slice 6.33 (D113), asked on 2026-10-07: "I'd like Explorers (order 10) to go in front of probes (order 8)". The further
        // explorers stood after the drones and cargo ships that take turns (D102: "First before probes, rest last"); every one
        // now stands where the first does, and the explore plan says so for each.
        EveryoneSays(PurchaseNeed.None);
        _needs.Report(AutomationPlan.Explore, Need(PurchaseTier.Explorer, "SHIP_EXPLORER"), DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.ProbeDeployment, Need(PurchaseTier.Probes, "SHIP_PROBE"))).Should().BeFalse();
        (await MayBuyAsync(AutomationPlan.Trading, Need(PurchaseTier.Alternating, "SHIP_LIGHT_HAULER"))).Should().BeFalse();
        (await MayBuyAsync(AutomationPlan.ProbeDeployment, Need(PurchaseTier.FarProbes, "SHIP_PROBE"))).Should().BeFalse();
        (await MayBuyAsync(AutomationPlan.Explore, Need(PurchaseTier.Explorer, "SHIP_EXPLORER"))).Should().BeTrue();
    }

    [Fact]
    public void EachTiersNumber_IsItsPositionInTheOrder_TheExplorersBeforeTheProbes()
    {
        // The SpaceTraders dashboard shows the number as the position (spacetraders_purchase_need_credits{position}), and you
        // name the tiers by it: "Explorers (order 10) ... in front of probes (order 8)". Since slice 6.33 (D113) every explorer
        // is 7, the probes stay 8 and the drones and cargo ships 9, and the far probes, 11 before, are 10.
        Enum.GetValues<PurchaseTier>().Should().Equal(
            PurchaseTier.None,
            PurchaseTier.Contract,
            PurchaseTier.Surveyor,
            PurchaseTier.Coverage,
            PurchaseTier.SurveyorPerArea,
            PurchaseTier.CargoShips,
            PurchaseTier.Construction,
            PurchaseTier.Explorer,
            PurchaseTier.Probes,
            PurchaseTier.Alternating,
            PurchaseTier.FarProbes);
        Enum.GetValues<PurchaseTier>().Select(tier => (int)tier).Should().Equal(Enumerable.Range(0, 11));
    }

    [Fact]
    public async Task ATurnPasses_WhenTheOtherKindHasNothingToBuy()
    {
        // A cargo ship's turn, but no new cargo ship would have a lucrative route: a drone may go.
        LedgerHolds(
            ("SHIP-4", ShipType.ShipLightShuttle),
            ("SHIP-5", ShipType.ShipLightHauler),
            ("SHIP-7", ShipType.ShipLightHauler),
            ("SHIP-8", ShipType.ShipMiningDrone));
        EveryoneSays(PurchaseNeed.None);
        _needs.Report(AutomationPlan.Trading, Need(PurchaseTier.Alternating, "SHIP_LIGHT_HAULER"), DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.Siphon, Need(PurchaseTier.Alternating, "SHIP_SIPHON_DRONE"))).Should().BeFalse();

        _needs.Report(AutomationPlan.Trading, PurchaseNeed.None, DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.Siphon, Need(PurchaseTier.Alternating, "SHIP_SIPHON_DRONE"))).Should().BeTrue();
    }

    [Theory]
    [InlineData("", PurchaseKind.Drone)]
    [InlineData("D", PurchaseKind.CargoShip)]
    [InlineData("DC", PurchaseKind.Drone)]
    [InlineData("DDC", PurchaseKind.Drone)]
    [InlineData("CCD", PurchaseKind.CargoShip)]
    [InlineData("PSD", PurchaseKind.CargoShip)]
    [InlineData("DCP", PurchaseKind.Drone)]
    [InlineData("DE", PurchaseKind.CargoShip)]
    public void TheTurn_GoesToTheKindNotBoughtLast_ADroneFirstAfterTheList(string since, PurchaseKind turn)
    {
        // D: a drone, C: a hauler, P: a probe, S: a surveyor, E: an explorer. Probes, surveyors and explorers don't take turns,
        // and a turn that passed because one kind had nothing to buy isn't made up later: after two drones in a row, a hauler,
        // then a drone again.
        List<PurchaseRecord> purchases =
        [
            Bought("SHIP-3", ShipType.ShipMiningDrone, 0),
            Bought("SHIP-4", ShipType.ShipLightShuttle, 1),
            Bought("SHIP-5", ShipType.ShipSiphonDrone, 2),
            Bought("SHIP-6", ShipType.ShipLightHauler, 3),
            Bought("SHIP-7", ShipType.ShipLightHauler, 4),
        ];
        foreach (var (letter, index) in since.Select((letter, index) => (letter, index)))
        {
            var type = letter switch
            {
                'D' => ShipType.ShipSiphonDrone,
                'C' => ShipType.ShipLightHauler,
                'P' => ShipType.ShipProbe,
                'E' => ShipType.ShipExplorer,
                _ => ShipType.ShipSurveyor,
            };
            purchases.Add(Bought($"NEW-{index}", type, 10 + index));
        }

        PurchaseOrder.Turn(purchases, [ShipType.ShipLightShuttle, ShipType.ShipLightHauler, ShipType.ShipLightHauler]).Should().Be(turn);
    }

    [Fact]
    public void AListChangedSinceItsShipsWereBought_StillGivesTheTurns()
    {
        // Found in review: counting from the list's last cargo ship, three shuttles bought before the list was changed to
        // light haulers left the haulers' turn waiting for good. The shuttles are cargo ships whatever the list says now.
        List<PurchaseRecord> purchases =
        [
            Bought("SHIP-4", ShipType.ShipLightShuttle, 0),
            Bought("SHIP-5", ShipType.ShipLightShuttle, 1),
            Bought("SHIP-6", ShipType.ShipLightShuttle, 2),
        ];

        PurchaseOrder.Turn(purchases, [ShipType.ShipLightHauler]).Should().Be(PurchaseKind.Drone);
        PurchaseOrder.Turn([.. purchases, Bought("SHIP-7", ShipType.ShipMiningDrone, 3)], [ShipType.ShipLightHauler]).Should().Be(PurchaseKind.CargoShip);
    }

    [Theory]
    [InlineData(ShipType.ShipHeavyFreighter)]
    [InlineData(ShipType.ShipBulkFreighter)]
    [InlineData(ShipType.ShipRefiningFreighter)]
    public void AfterACargoShipOfAnyType_ItIsTheDronesTurn(ShipType cargoShip)
    {
        // Slice 6.33 (D112): beyond the list the trading plan buys the largest hold, whatever its type. Counted by the list and
        // the shuttles and haulers alone, a refining or bulk freighter left the cargo ships' turn standing after it, and the
        // drones waited for good.
        List<PurchaseRecord> purchases = [Bought("SHIP-3", ShipType.ShipMiningDrone, 0), Bought("SHIP-4", cargoShip, 1)];

        PurchaseOrder.Turn(purchases, [ShipType.ShipLightShuttle, ShipType.ShipLightHauler]).Should().Be(PurchaseKind.Drone);
    }

    [Fact]
    public void WithoutAnyDroneOrCargoShipBought_ItIsTheDronesTurn()
    {
        PurchaseOrder.Turn([Bought("SHIP-2", ShipType.ShipProbe, 0)], []).Should().Be(PurchaseKind.Drone);
    }

    [Fact]
    public async Task NothingToBuy_IsNeverABuy_AndClearsWhatThePlanSaidBefore()
    {
        EveryoneSays(PurchaseNeed.None);
        _needs.Report(AutomationPlan.Trading, Need(PurchaseTier.CargoShips, "SHIP_LIGHT_SHUTTLE"), DateTimeOffset.UtcNow);

        (await MayBuyAsync(AutomationPlan.Trading, PurchaseNeed.None)).Should().BeFalse();

        _needs.Open(DateTimeOffset.UtcNow).Should().BeEmpty();
    }

    [Fact]
    public void TheOpenNeeds_ComeFirstInTheOrderFirst()
    {
        var now = DateTimeOffset.UtcNow;
        _needs.Report(AutomationPlan.ProbeDeployment, Need(PurchaseTier.Probes, "SHIP_PROBE"), now);
        _needs.Report(AutomationPlan.Trading, Need(PurchaseTier.CargoShips, "SHIP_LIGHT_SHUTTLE"), now);
        _needs.Report(AutomationPlan.Mining, PurchaseNeed.None, now);
        _needs.Report(AutomationPlan.Survey, Need(PurchaseTier.Surveyor, "SHIP_SURVEYOR"), now - PurchaseNeeds.Lifetime - TimeSpan.FromSeconds(1));

        _needs.Open(now).Select(open => open.Plan).Should().Equal(AutomationPlan.Trading, AutomationPlan.ProbeDeployment);
    }

    private static PurchaseNeed Need(PurchaseTier tier, string shipType) => new(tier, shipType, "X1-DC53-A2", 50_000);

    private static PurchaseRecord Bought(string ship, ShipType type, int minute) => new(ship, type, Start.AddMinutes(minute));

    private void EveryoneSays(PurchaseNeed need)
    {
        foreach (var plan in PurchaseOrder.BuyingPlans.Keys)
        {
            _needs.Report(plan, need, DateTimeOffset.UtcNow);
        }
    }

    private void PlanIs(AutomationPlan plan, bool on)
        => _settings.GetAsync<bool>(AutomationSwitches.PlanEnabledSetting(plan), Arg.Any<CancellationToken>()).Returns(on);

    /// <summary>The ledger's ship purchases, the newest first as it returns them, a minute apart.</summary>
    private void LedgerHolds(params (string Ship, ShipType Type)[] purchases)
        => _ledger.GetRangeAsync().ReturnsForAnyArgs(
            [
                .. purchases
                    .Select((purchase, index) => new LedgerEntryDto(index, Start.AddMinutes(index), purchase.Ship, null, nameof(LedgerCategory.ShipPurchase), -50_000, purchase.Type.ToString(), null, null, null))
                    .Reverse(),
            ]);

    private Task<bool> MayBuyAsync(AutomationPlan plan, PurchaseNeed need)
        => new PurchaseOrder(_needs, _settings, _ledger, _log.For<PurchaseOrder>()).ReportAsync(plan, need, CancellationToken.None);
}
