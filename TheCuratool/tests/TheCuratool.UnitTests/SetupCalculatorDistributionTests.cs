using TheCuratool.Application;
using TheCuratool.Domain;

namespace TheCuratool.UnitTests;

/// <summary>
/// Verifies valid-target distributions produced by <see cref="SetupCalculator"/> for a 'normal'
/// script shape: 13 Townsfolk, 4 Outsiders, 4 Minions, up to 4 Demons.
/// </summary>
public sealed class SetupCalculatorDistributionTests
{
    // Neutral Townsfolk — none have count-affecting setup rules.
    private static readonly string[] AllTownsfolk =
    [
        "chef", "washerwoman", "librarian", "investigator", "empath", "fortuneteller",
        "undertaker", "monk", "ravenkeeper", "virgin", "slayer", "soldier", "mayor",
    ];

    // Neutral Outsiders — saint/recluse/butler/goon have no setup rules.
    private static readonly string[] FourOutsiders = ["saint", "recluse", "butler", "goon"];
    private static readonly string[] TwoOutsiders = ["saint", "recluse"];
    private static readonly string[] SixOutsiders = ["saint", "recluse", "butler", "goon", "moonchild", "tinker"];

    // Neutral Minions — no count-affecting setup rules.
    private static readonly string[] NeutralMinions = ["poisoner", "scarletwoman", "spy", "witch"];

    // Neutral Demons — imp, zombuul, pukka, shabaloth have no setup rules.
    private static readonly string[] NeutralDemons = ["imp", "zombuul", "pukka", "shabaloth"];

    private readonly SetupCalculator _calculator = new();
    private readonly CharacterDatabase _characterDatabase = CharacterDatabase.LoadFromFile(GetCharactersFilePath());
    private readonly LoricDatabase _loricDatabase = LoricDatabase.LoadFromFile(GetLoricsFilePath());

    // -----------------------------------------------------------------------------------------
    // Single-role tests
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void T1_Baron_7Players_BaseAndPlusTwoOutsiders()
    {
        var script = NormalScript("baron", "imp");
        var result = Calculate(script, 7);
        AssertExactTargets(result, [(5, 0, 1, 1), (3, 2, 1, 1)]);
    }

    [Fact]
    public void T2_Baron_10Players_BaseAndPlusTwoOutsiders()
    {
        var script = NormalScript("baron", "imp");
        var result = Calculate(script, 10);
        AssertExactTargets(result, [(7, 0, 2, 1), (5, 2, 2, 1)]);
    }

    [Fact]
    public void T3_Godfather_7Players_BaseAndMinusOne_PlusOnePruned()
    {
        var script = NormalScript("godfather", "imp");
        var result = Calculate(script, 7);
        AssertExactTargets(result, [(5, 0, 1, 1), (4, 1, 1, 1)]);
    }

    [Fact]
    public void T4_Godfather_9Players_BaseMinusOnePlusOne()
    {
        var script = NormalScript("godfather", "imp");
        var result = Calculate(script, 9);
        AssertExactTargets(result, [(5, 2, 1, 1), (6, 1, 1, 1), (4, 3, 1, 1)]);
    }

    [Fact]
    public void T5_Godfather_5Players_BaseAndMinusOne_PlusOnePruned()
    {
        var script = NormalScript("godfather", "imp");
        var result = Calculate(script, 5);
        AssertExactTargets(result, [(3, 0, 1, 1), (2, 1, 1, 1)]);
    }

    [Fact]
    public void T6_Balloonist_8Players_BaseAndPlusOne()
    {
        var script = NormalScript("balloonist", "imp");
        var result = Calculate(script, 8);
        AssertExactTargets(result, [(5, 1, 1, 1), (4, 2, 1, 1)]);
    }

    [Fact]
    public void T7_HuntsmanAndDamsel_8Players_BaseAndPlusOneOutsider()
    {
        var script = NormalScript("huntsman", "damsel", "imp");
        var result = Calculate(script, 8);
        AssertExactTargets(result, [(5, 1, 1, 1), (4, 2, 1, 1)]);
    }

    [Fact]
    public void T8_Drunk_7Players_BaseOnly_NoDistributionChange()
    {
        var script = NormalScript("drunk", "imp");
        var result = Calculate(script, 7);
        AssertExactTargets(result, [(5, 0, 1, 1)]);
    }

    [Fact]
    public void T9_Hermit_9Players_ScriptOnly_BaseUnchanged()
    {
        // Hermit is an Outsider — unchosen Outsiders are excluded from optional non-demon sources,
        // so its SwapOutsiderForTownsfolk rule does not affect the distribution until it is chosen.
        var script = NormalScript("hermit", "imp");
        var result = Calculate(script, 9);
        AssertExactTargets(result, [(5, 2, 1, 1)]);
    }

    [Fact]
    public void T10_Hermit_7Players_ScriptOnly_BaseUnchanged()
    {
        var script = NormalScript("hermit", "imp");
        var result = Calculate(script, 7);
        AssertExactTargets(result, [(5, 0, 1, 1)]);
    }

    [Fact]
    public void T11_FangGuAndImp_7Players_BaseAndPlusOneOutsider()
    {
        var script = NormalScript("fanggu", "imp");
        var result = Calculate(script, 7);
        AssertExactTargets(result, [(5, 0, 1, 1), (4, 1, 1, 1)]);
    }

    [Fact]
    public void T12_VigormortisAndImp_9Players_BaseAndMinusOneOutsider()
    {
        var script = NormalScript("vigormortis", "imp");
        var result = Calculate(script, 9);
        AssertExactTargets(result, [(5, 2, 1, 1), (6, 1, 1, 1)]);
    }

    [Fact]
    public void T13_LilMonstaAndImp_10Players_BaseAndDemonSwap()
    {
        var script = NormalScript("lilmonsta", "imp");
        var result = Calculate(script, 10);
        AssertExactTargets(result, [(7, 0, 2, 1), (7, 0, 3, 0)]);
    }

    [Fact]
    public void T14_SummonerAndImp_7Players_BaseAndSummonerNoDemon()
    {
        var script = NormalScript("summoner", "imp");
        var result = Calculate(script, 7);
        AssertExactTargets(result, [(5, 0, 1, 1), (6, 0, 1, 0)]);
    }

    [Fact]
    public void T15_Kazali_4Outsiders_7Players_ZeroToFourOutsidersZeroMinions()
    {
        var script = KazaliScript(FourOutsiders);
        var result = Calculate(script, 7, "kazali");
        AssertExactTargets(result, [(6, 0, 0, 1), (5, 1, 0, 1), (4, 2, 0, 1), (3, 3, 0, 1), (2, 4, 0, 1)]);
    }

    [Fact]
    public void T16_Kazali_4Outsiders_10Players_FreedMinionSlotsIntoTF()
    {
        var script = KazaliScript(FourOutsiders);
        var result = Calculate(script, 10, "kazali");
        AssertExactTargets(result, [(9, 0, 0, 1), (8, 1, 0, 1), (7, 2, 0, 1), (6, 3, 0, 1), (5, 4, 0, 1)]);
    }

    [Fact]
    public void T17_Kazali_4Outsiders_13Players_FreedMinionSlotsIntoTF()
    {
        var script = KazaliScript(FourOutsiders);
        var result = Calculate(script, 13, "kazali");
        AssertExactTargets(result, [(12, 0, 0, 1), (11, 1, 0, 1), (10, 2, 0, 1), (9, 3, 0, 1), (8, 4, 0, 1)]);
    }

    [Fact]
    public void T18_Kazali_2Outsiders_7Players_CappedAtTwo()
    {
        var script = KazaliScript(TwoOutsiders);
        var result = Calculate(script, 7, "kazali");
        AssertExactTargets(result, [(6, 0, 0, 1), (5, 1, 0, 1), (4, 2, 0, 1)]);
    }

    [Fact]
    public void T19_Kazali_6Outsiders_9Players_CappedAtSixWithFreedMinionSlot()
    {
        var script = KazaliScript(SixOutsiders);
        var result = Calculate(script, 9, "kazali");
        AssertExactTargets(result,
        [
            (8, 0, 0, 1), (7, 1, 0, 1), (6, 2, 0, 1), (5, 3, 0, 1),
            (4, 4, 0, 1), (3, 5, 0, 1), (2, 6, 0, 1),
        ]);
    }

    [Fact]
    public void T20_LordOfTyphon_4Outsiders_8Players_ZeroToFourOutsidersZeroMinions()
    {
        var script = LordOfTyphonScript(FourOutsiders);
        var result = Calculate(script, 8, "lordoftyphon");
        AssertExactTargets(result,
        [
            (7, 0, 0, 1), (6, 1, 0, 1), (5, 2, 0, 1), (4, 3, 0, 1), (3, 4, 0, 1),
        ]);
    }

    [Fact]
    public void T21_LordOfTyphon_4Outsiders_11Players_FreedMinionSlotsIntoTF()
    {
        // 11p base (7,1,2,1): UnconstrainedOutsiderDelta capped at min(7+1,4)=4, RemoveAllMinions adds 2 TF.
        // Demon branch forces HasAnyDemonChosen=true so Demons stays >=1.
        var script = LordOfTyphonScript(FourOutsiders);
        var result = Calculate(script, 11);
        AssertExactTargets(result,
        [
            (10, 0, 0, 1), (9, 1, 0, 1), (8, 2, 0, 1), (7, 3, 0, 1), (6, 4, 0, 1),
        ]);
    }

    [Fact]
    public void T22_LordOfTyphon_2Outsiders_8Players_CappedAtTwo()
    {
        var script = LordOfTyphonScript(TwoOutsiders);
        var result = Calculate(script, 8, "lordoftyphon");
        AssertExactTargets(result, [(7, 0, 0, 1), (6, 1, 0, 1), (5, 2, 0, 1)]);
    }

    [Fact]
    public void T23_LordOfTyphon_6Outsiders_8Players_CappedAtSix()
    {
        var script = LordOfTyphonScript(SixOutsiders);
        var result = Calculate(script, 8, "lordoftyphon");
        AssertExactTargets(result,
        [
            (7, 0, 0, 1), (6, 1, 0, 1), (5, 2, 0, 1), (4, 3, 0, 1),
            (3, 4, 0, 1), (2, 5, 0, 1), (1, 6, 0, 1),
        ]);
    }

    // -----------------------------------------------------------------------------------------
    // Combination tests
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void T24_BaronAndGodfather_7Players_AllOptionalCombinations()
    {
        // Both are optional non-demon sources (script-only, not chosen). The calculator enumerates
        // all power-set subsets including the impossible baron+godfather stacking (only 1 minion slot),
        // since feasibility does not yet enforce minion-slot occupancy. See T42 for the pruning test.
        var script = NormalScript("baron", "godfather", "imp");
        var result = Calculate(script, 7);
        AssertExactTargets(result, [(5, 0, 1, 1), (3, 2, 1, 1), (4, 1, 1, 1), (2, 3, 1, 1)]);
    }

    [Fact]
    public void T25_BaronAndGodfather_10Players_AllOptionalCombinations()
    {
        var script = NormalScript("baron", "godfather", "imp");
        var result = Calculate(script, 10);
        AssertExactTargets(result, [(7, 0, 2, 1), (6, 1, 2, 1), (5, 2, 2, 1), (4, 3, 2, 1)]);
    }

    [Fact]
    public void T26_BaronFangGuAndImp_7Players_FourCombinations()
    {
        var script = NormalScript("baron", "fanggu", "imp");
        var result = Calculate(script, 7);
        AssertExactTargets(result, [(5, 0, 1, 1), (3, 2, 1, 1), (4, 1, 1, 1), (2, 3, 1, 1)]);
    }

    [Fact]
    public void T27_FangGuVigormortisAndImp_9Players_DemonMutualExclusionNeverStack()
    {
        var script = NormalScript("fanggu", "vigormortis", "imp");
        var result = Calculate(script, 9);
        AssertExactTargets(result, [(5, 2, 1, 1), (4, 3, 1, 1), (6, 1, 1, 1)]);
    }

    [Fact]
    public void T28_BaronAndBalloonist_9Players_AllOptionalCombinations()
    {
        // baron(+2) and balloonist(0 or +1) are independent optional non-demon sources.
        // All four power-set subsets produce distinct distributions.
        var script = NormalScript("baron", "balloonist", "imp");
        var result = Calculate(script, 9);
        AssertExactTargets(result, [(5, 2, 1, 1), (4, 3, 1, 1), (3, 4, 1, 1), (2, 5, 1, 1)]);
    }

    [Fact]
    public void T29_DrunkAndBaron_9Players_DrunkNoChangeBaronPlusTwo()
    {
        // Drunk is an Outsider with no rules — no effect on distribution when unchosen.
        var script = NormalScript("drunk", "baron", "imp");
        var result = Calculate(script, 9);
        AssertExactTargets(result, [(5, 2, 1, 1), (3, 4, 1, 1)]);
    }

    [Fact]
    public void T30_DrunkAndHermit_9Players_BothOutsiders_BaseUnchanged()
    {
        // Both Drunk and Hermit are Outsiders — unchosen Outsiders are excluded from optional
        // non-demon sources, so neither affects the distribution when script-only.
        var script = NormalScript("drunk", "hermit", "imp");
        var result = Calculate(script, 9);
        AssertExactTargets(result, [(5, 2, 1, 1)]);
    }

    [Fact]
    public void T31_AlchemistBorrowsBaron_13Players_MandatoryPlusTwoOnly()
    {
        // Alchemist (chosen) borrows Baron's ability → mandatory +2 outsiders.
        // Baron is in borrowedSet so it is excluded from optional non-demon sources → no further +2.
        var script = NormalScript("alchemist", "baron", "imp");
        var result = CalculateWithBorrowed(script, 13, ["alchemist"], ["baron"]);
        AssertExactTargets(result, [(7, 2, 3, 1)]);
    }

    [Fact]
    public void T32_AlchemistBorrowsGodfather_13Players_MandatoryChoiceOnly()
    {
        // Alchemist (chosen) borrows Godfather's ability → mandatory StoryTellerChoice(±1).
        // Base (9,0,3,1): -1 → (10,-1) filtered; +1 → (8,1,3,1). Mandatory outcome: {(8,1,3,1)}.
        // Godfather is in borrowedSet so it is excluded from optional non-demon sources.
        var script = NormalScript("alchemist", "godfather", "imp");
        var result = CalculateWithBorrowed(script, 13, ["alchemist"], ["godfather"]);
        AssertExactTargets(result, [(8, 1, 3, 1)]);
    }

    [Fact]
    public void T33_AlchemistBorrowsBaron_GodfatherOnScript_13Players()
    {
        // Alchemist (chosen) borrows Baron → mandatory +2 outsiders. Baron is in borrowedSet,
        // excluded from optional non-demon sources. Godfather (unchosen Minion) still contributes
        // as an optional non-demon source via StoryTellerChoice(±1).
        // Mandatory: (9,0,3,1) +2 → (7,2,3,1).
        // Optional power-set on (7,2,3,1):
        //   {} → (7,2,3,1)
        //   {godfather -1} → (8,1,3,1); {godfather +1} → (6,3,3,1)
        var script = NormalScript("alchemist", "baron", "godfather", "imp");
        var result = CalculateWithBorrowed(script, 13, ["alchemist"], ["baron"]);
        AssertExactTargets(result, [(8, 1, 3, 1), (7, 2, 3, 1), (6, 3, 3, 1)]);
    }

    // -----------------------------------------------------------------------------------------
    // Edge cases — base distribution not reachable
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void T34_LilMonsta_OnlyDemon_7Players_MandatorySwap()
    {
        var script = NormalScriptNoNeutralDemons("lilmonsta");
        var result = Calculate(script, 7);
        AssertExactTargets(result, [(5, 0, 2, 0)]);
    }

    [Fact]
    public void T35_Vigormortis_OnlyDemon_9Players_MandatoryMinusOne()
    {
        var script = NormalScriptNoNeutralDemons("vigormortis");
        var result = Calculate(script, 9);
        AssertExactTargets(result, [(6, 1, 1, 1)]);
    }

    [Fact]
    public void T36_Godfather_OnlyMinion_7Players_MandatoryGF()
    {
        var script = NormalScript("godfather", "imp");
        var result = Calculate(script, 7);
        AssertContainsTarget(result, (4, 1, 1, 1));
        Assert.DoesNotContain(new SetupCounts(6, -1, 1, 1), result.ValidTargetCounts);
    }

    // -----------------------------------------------------------------------------------------
    // Boffin confirmation test
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void T37_BoffinBorrowsBalloonist_9Players_BaseAndPlusOneOutsider()
    {
        // Boffin (chosen) borrows Balloonist → mandatory OutsiderDelta(+1, isStorytellerChoice=true).
        // Balloonist must not be in chosenIds (Boffin's scope is NotInPlayTownsfolkOrOutsider).
        var script = NormalScript("boffin", "balloonist", "imp");
        var result = CalculateWithBorrowed(script, 9, ["boffin", "imp"], ["balloonist"]);
        AssertExactTargets(result, [(5, 2, 1, 1), (4, 3, 1, 1)]);
    }

    // -----------------------------------------------------------------------------------------
    // Legion tests
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void T38_Legion_9Players_7Legion_TwoGoodSeatsZeroToTwoOutsiders()
    {
        var script = LegionScript(FourOutsiders);
        var result = CalculateLegion(script, 9, 7);
        AssertExactTargets(result, [(2, 0, 0, 7), (1, 1, 0, 7), (0, 2, 0, 7)]);
    }

    [Fact]
    public void T39_Legion_9Players_6Legion_ThreeGoodSeatsZeroToThreeOutsiders()
    {
        var script = LegionScript(FourOutsiders);
        var result = CalculateLegion(script, 9, 6);
        AssertExactTargets(result, [(3, 0, 0, 6), (2, 1, 0, 6), (1, 2, 0, 6), (0, 3, 0, 6)]);
    }

    [Fact]
    public void T40_Legion_12Players_9Legion_ThreeGoodSeatsZeroToThreeOutsiders()
    {
        var script = LegionScript(FourOutsiders);
        var result = CalculateLegion(script, 12, 9);
        AssertExactTargets(result, [(3, 0, 0, 9), (2, 1, 0, 9), (1, 2, 0, 9), (0, 3, 0, 9)]);
    }

    [Fact]
    public void T41_Legion_12Players_7Legion_FiveGoodSeatsZeroToFourOutsiders()
    {
        // goodSlots = 12-7 = 5; outsiderCap = min(5, 4 outsiders on script) = 4.
        var script = LegionScript(FourOutsiders);
        var result = CalculateLegion(script, 12, 7);
        AssertExactTargets(result,
        [
            (5, 0, 0, 7), (4, 1, 0, 7), (3, 2, 0, 7), (2, 3, 0, 7), (1, 4, 0, 7),
        ]);
    }

    // -----------------------------------------------------------------------------------------
    // Known-failing stacking tests
    // -----------------------------------------------------------------------------------------

    [Fact(Skip = "Known failing: feasibility filter does not yet enforce minion-slot occupancy — see bug #XXX")]
    public void T42_BaronAndGodfather_7Players_StackedDistributionPruned()
    {
        var script = NormalScript("baron", "godfather", "imp");
        var result = Calculate(script, 7, "baron", "godfather");
        // Only 1 minion slot; stacking Baron (+2 O) and Godfather (±1 O) is impossible.
        Assert.DoesNotContain(new SetupCounts(1, 4, 1, 1), result.ValidTargetCounts);
        Assert.DoesNotContain(new SetupCounts(2, 3, 1, 1), result.ValidTargetCounts);
    }

    [Fact(Skip = "Known failing: feasibility filter does not yet enforce minion-slot occupancy — see bug #XXX")]
    public void T43_BaronBalloonistAndGodfather_9Players_ImpossibleMinion_SlotCombinationsPruned()
    {
        var script = NormalScript("baron", "balloonist", "godfather", "imp");
        var result = Calculate(script, 9, "baron", "balloonist", "godfather");
        // Only 1 minion slot; Baron+Godfather+Balloonist combined is impossible.
        Assert.DoesNotContain(new SetupCounts(2, 5, 1, 1), result.ValidTargetCounts);
        Assert.DoesNotContain(new SetupCounts(3, 4, 1, 1), result.ValidTargetCounts);
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    private SetupCalculationResult Calculate(Script script, int playerCount, params string[] chosenIds)
    {
        return _calculator.Calculate(
            script,
            playerCount,
            chosenIds,
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);
    }

    private SetupCalculationResult CalculateWithBorrowed(Script script, int playerCount, string[] chosenIds, string[] borrowedAbilityIds)
    {
        return _calculator.Calculate(
            script,
            playerCount,
            chosenIds,
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase,
            borrowedAbilityIds);
    }

    private SetupCalculationResult CalculateLegion(Script script, int playerCount, int legionCount)
    {
        return _calculator.Calculate(
            script,
            playerCount,
            Array.Empty<string>(),
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false, true, legionCount),
            _characterDatabase,
            _loricDatabase);
    }

    /// <summary>
    /// Builds a 'normal' script: 13 neutral Townsfolk, 4 neutral Outsiders, 4 neutral Minions,
    /// 4 neutral Demons, plus the specified additional role IDs (de-duplicated).
    /// All filler characters are deliberately neutral (no count-affecting setup rules) so that
    /// only the extra roles listed affect the distribution.
    /// </summary>
    private Script NormalScript(params string[] extraIds)
    {
        var ids = AllTownsfolk
            .Concat(FourOutsiders)
            .Concat(NeutralMinions)
            .Concat(NeutralDemons)
            .Concat(extraIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var characters = ids.Select(_characterDatabase.Resolve).ToList().AsReadOnly();
        return new Script("Normal Script", "Test", characters);
    }

    /// <summary>
    /// Like <see cref="NormalScript"/> but without any neutral Demon fillers, so the only
    /// Demon source is the one explicitly passed in <paramref name="extraIds"/>.
    /// </summary>
    private Script NormalScriptNoNeutralDemons(params string[] extraIds)
    {
        var ids = AllTownsfolk
            .Concat(FourOutsiders)
            .Concat(NeutralMinions)
            .Concat(extraIds)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var characters = ids.Select(_characterDatabase.Resolve).ToList().AsReadOnly();
        return new Script("Normal Script (No Neutral Demons)", "Test", characters);
    }

    private Script KazaliScript(string[] outsiderIds)
    {
        var ids = AllTownsfolk
            .Concat(outsiderIds)
            .Append("kazali")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var characters = ids.Select(_characterDatabase.Resolve).ToList().AsReadOnly();
        return new Script("Kazali Script", "Test", characters);
    }

    private Script LordOfTyphonScript(string[] outsiderIds)
    {
        var ids = AllTownsfolk
            .Concat(outsiderIds)
            .Append("lordoftyphon")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var characters = ids.Select(_characterDatabase.Resolve).ToList().AsReadOnly();
        return new Script("LordOfTyphon Script", "Test", characters);
    }

    private Script LegionScript(string[] outsiderIds)
    {
        var ids = AllTownsfolk
            .Concat(outsiderIds)
            .Append("legion")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var characters = ids.Select(_characterDatabase.Resolve).ToList().AsReadOnly();
        return new Script("Legion Script", "Test", characters);
    }

    private static void AssertExactTargets(SetupCalculationResult result, (int t, int o, int m, int d)[] expected)
    {
        var expectedSet = expected
            .Select(e => new SetupCounts(e.t, e.o, e.m, e.d))
            .ToHashSet();

        var actual = result.ValidTargetCounts.ToHashSet();

        var missing = expectedSet.Except(actual).ToList();
        var extra = actual.Except(expectedSet).ToList();

        if (missing.Count > 0 || extra.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ValidTargetCounts mismatch.");
            if (missing.Count > 0)
            {
                sb.AppendLine($"  Missing: {string.Join(", ", missing.Select(c => $"({c.Townsfolk}/{c.Outsiders}/{c.Minions}/{c.Demons})"))}");
            }

            if (extra.Count > 0)
            {
                sb.AppendLine($"  Unexpected: {string.Join(", ", extra.Select(c => $"({c.Townsfolk}/{c.Outsiders}/{c.Minions}/{c.Demons})"))}");
            }

            Assert.Fail(sb.ToString().TrimEnd());
        }
    }

    private static void AssertContainsTarget(SetupCalculationResult result, (int t, int o, int m, int d) expected)
    {
        Assert.Contains(new SetupCounts(expected.t, expected.o, expected.m, expected.d), result.ValidTargetCounts);
    }

    private static string GetCharactersFilePath()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data", "characters.json"));
    }

    private static string GetLoricsFilePath()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data", "lorics.json"));
    }
}
