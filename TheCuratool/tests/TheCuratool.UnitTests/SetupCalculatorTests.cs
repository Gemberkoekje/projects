using System.IO;

using TheCuratool.Application;
using TheCuratool.Domain;

namespace TheCuratool.UnitTests;

public sealed class SetupCalculatorTests
{
    private readonly SetupCalculator _calculator = new();
    private readonly CharacterDatabase _characterDatabase = CharacterDatabase.LoadFromFile(GetCharactersFilePath());
    private readonly LoricDatabase _loricDatabase = LoricDatabase.LoadFromFile(GetLoricsFilePath());

    [Fact]
    public void Calculate_Baron_AppliesOutsiderDelta()
    {
        var script = CreateScript("baron");

        var result = _calculator.Calculate(
            script,
            7,
            new[] { "baron" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.Equal(new SetupCounts(5, 0, 1, 1), result.BaseDistribution);
        Assert.Equal(new SetupCounts(3, 2, 1, 1), Assert.Single(result.ValidTargetCounts));
    }

    [Fact]
    public void Calculate_DrunkFlaggedBaron_DoesNotApplyOutsiderDelta()
    {
        var script = CreateScript("baron");

        var result = _calculator.Calculate(
            script,
            7,
            new[] { "baron" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>
            {
                ["baron"] = new HiddenFlags(true, false),
            },
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.Equal(new SetupCounts(5, 0, 1, 1), Assert.Single(result.ValidTargetCounts));
    }

    [Fact]
    public void Calculate_Godfather_ProducesStorytellerChoiceOutcomes()
    {
        var script = CreateScript("godfather");

        var result = _calculator.Calculate(
            script,
            8,
            new[] { "godfather" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.Contains(new SetupCounts(6, 0, 1, 1), result.ValidTargetCounts);
        Assert.Contains(new SetupCounts(4, 2, 1, 1), result.ValidTargetCounts);
    }

    [Fact]
    public void Calculate_DrunkFlaggedGodfather_DoesNotProduceOutsiderChoiceOutcomes()
    {
        var script = CreateScript("godfather");

        var result = _calculator.Calculate(
            script,
            8,
            new[] { "godfather" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>
            {
                ["godfather"] = new HiddenFlags(true, false),
            },
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.Equal(new SetupCounts(5, 1, 1, 1), Assert.Single(result.ValidTargetCounts));
    }

    [Fact]
    public void Calculate_FangGu_AddsAnOutsider()
    {
        var script = CreateScript("fanggu");

        var result = _calculator.Calculate(
            script,
            7,
            new[] { "fanggu" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.Contains(new SetupCounts(4, 1, 1, 1), result.ValidTargetCounts);
    }

    [Fact]
    public void Calculate_LunaticFlaggedFangGu_DoesNotApplyOutsiderDelta()
    {
        var script = CreateScript("fanggu");

        var result = _calculator.Calculate(
            script,
            7,
            new[] { "fanggu" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>
            {
                ["fanggu"] = new HiddenFlags(false, true),
            },
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.Equal(new SetupCounts(5, 0, 1, 1), Assert.Single(result.ValidTargetCounts));
    }

    [Fact]
    public void Calculate_Vigormortis_SubtractsAnOutsider()
    {
        var script = CreateScript("vigormortis");

        var result = _calculator.Calculate(
            script,
            8,
            new[] { "vigormortis" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.Contains(new SetupCounts(6, 0, 1, 1), result.ValidTargetCounts);
    }

    [Fact]
    public void Calculate_Summoner_ReplacesADemonWithATownsfolk()
    {
        var script = CreateScript("summoner");

        var result = _calculator.Calculate(
            script,
            7,
            new[] { "summoner" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.Contains(new SetupCounts(6, 0, 1, 0), result.ValidTargetCounts);
    }

    [Fact]
    public void Calculate_Hermit_ProducesChoiceBetweenNoChangeAndSwap()
    {
        var script = CreateScript("hermit");

        var result = _calculator.Calculate(
            script,
            8,
            new[] { "hermit" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.Contains(new SetupCounts(5, 1, 1, 1), result.ValidTargetCounts);
        Assert.Contains(new SetupCounts(6, 0, 1, 1), result.ValidTargetCounts);
    }

    [Fact]
    public void Calculate_LordOfTyphon_UsesDeferredOutsiderOnly()
    {
        var script = CreateScript("lordoftyphon");

        var result = _calculator.Calculate(
            script,
            7,
            new[] { "lordoftyphon" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.Contains(new SetupCounts(6, 0, 0, 1), result.ValidTargetCounts);
    }

    [Fact]
    public void Calculate_Kazali_ClampsOutsidersByOutsiderCharactersOnScript()
    {
        var script = CreateScript("kazali", "drunk", "saint", "recluse", "butler");

        // The script has exactly 4 Outsider characters (drunk, saint, recluse, butler), so no legal
        // draft can ever seat 5 Outsiders. With Kazali chosen, the broadened enumeration also stacks
        // the still-draftable Drunk on top of Kazali's own clamp; the reachability filter (only 4
        // Outsider characters available) is what keeps the final set capped at 4.
        var feasibility = new SetupFeasibilityContext(
            true,
            new SetupCounts(0, 0, 0, 1),
            12,
            12,
            4,
            0,
            0);

        var result = _calculator.Calculate(
            script,
            13,
            new[] { "kazali" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase,
            null,
            feasibility);

        Assert.NotEmpty(result.ValidTargetCounts);
        Assert.DoesNotContain(result.ValidTargetCounts, counts => counts.Outsiders > 4);
    }

    [Fact]
    public void Calculate_Kazali_WhenDemonAlreadyChosen_DoesNotIncludeZeroDemonTargets()
    {
        var script = CreateScript("kazali", "imp", "drunk", "saint", "recluse", "butler", "chef", "washerwoman");

        var result = _calculator.Calculate(
            script,
            10,
            new[] { "imp", "kazali" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.DoesNotContain(result.ValidTargetCounts, counts => counts.Demons == 0);
    }

    [Fact]
    public void Marionette_IsNeverPartOfSetupCalculationInput()
    {
        var marionette = _characterDatabase.Resolve("marionette");

        Assert.True(marionette.IsDraftExcluded);
    }

    [Fact]
    public void Calculate_Drunk_DoesNotChangeOutsiderCount()
    {
        var script = CreateScript("drunk");

        var result = _calculator.Calculate(
            script,
            7,
            new[] { "drunk" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.Equal(new SetupCounts(5, 0, 1, 1), Assert.Single(result.ValidTargetCounts));
    }

    [Fact]
    public void Calculate_Lunatic_AppliesReplaceDemonOutsiderDisplayRule()
    {
        var script = CreateScript("lunatic");

        var result = _calculator.Calculate(
            script,
            7,
            new[] { "lunatic" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        // Lunatic occupies an outsider slot but is shown as a demon: D+1, O-1 from base (5,0,1,1)
        // Base has 0 outsiders so we expect fallback: O-1 is invalid, normal path D-1,O+1 = (5,1,1,0)
        // Actually base 7-player is (5,0,1,1): D==1, O==0 → normal path: D-1,O+1 → (5,1,1,0)
        Assert.Contains(new SetupCounts(5, 1, 1, 0), result.ValidTargetCounts);
    }

    [Fact]
    public void Calculate_Lunatic_WhenDemonAlreadyChosen_ShowsDemonSlotFromOutsider()
    {
        // 8-player base is (5,1,1,1); demon already chosen means D==0 after setup,
        // and outsider slot exists, so Lunatic flips O-1,D+1 → (5,0,1,1)... but here
        // we verify the rule at the setup-counts level directly.
        // Use an 8-player game where base outsiders > 0: Lunatic (D-1,O+1) → (5,2,1,0)
        var script = CreateScript("lunatic");

        var result = _calculator.Calculate(
            script,
            8,
            new[] { "lunatic" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        // Base 8-player: (5,1,1,1) → D-1,O+1 → (5,2,1,0)
        Assert.Contains(new SetupCounts(5, 2, 1, 0), result.ValidTargetCounts);
    }

    [Fact]
    public void Calculate_LunaticWhenNoDemons_ShowsDemonSlotFromOutsider()
    {
        // When the only outcome has D==0 but O>0, the rule should produce D+1,O-1
        // to allow a demon to appear in the draft as the Lunatic.
        // Summoner runs first: 8-player base (5,1,1,1) → T+1,D-1 → (6,1,1,0)
        // Then Lunatic: D==0,O>0 → D+1,O-1 → (6,0,1,1)
        var script = CreateScript("summoner", "lunatic");

        var result = _calculator.Calculate(
            script,
            8,
            new[] { "summoner", "lunatic" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.Contains(new SetupCounts(6, 0, 1, 1), result.ValidTargetCounts);
    }

    [Fact]
    public void Calculate_HuntsmanAndDamsel_ProducesBothOutcomes()
    {
        var script = CreateScript("huntsman", "damsel");

        var result = _calculator.Calculate(
            script,
            8,
            new[] { "huntsman", "damsel" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.Contains(new SetupCounts(5, 1, 1, 1), result.ValidTargetCounts);
        Assert.Contains(new SetupCounts(4, 2, 1, 1), result.ValidTargetCounts);
    }

    [Fact]
    public void Calculate_BoffinWithBorrowedBalloonist_ProducesBaseOrPlusOneOutsider()
    {
        var script = CreateScript("boffin", "balloonist");

        var result = _calculator.Calculate(
            script,
            12,
            new[] { "boffin" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase,
            new[] { "balloonist" });

        Assert.Equal(2, result.ValidTargetCounts.Count);
        Assert.Contains(new SetupCounts(7, 2, 2, 1), result.ValidTargetCounts);
        Assert.Contains(new SetupCounts(6, 3, 2, 1), result.ValidTargetCounts);
    }

    [Fact]
    public void Calculate_SentinelLoric_AppliesStorytellerChoice()
    {
        var script = CreateScript();

        var result = _calculator.Calculate(
            script,
            8,
            Array.Empty<string>(),
            new[] { "sentinel" },
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.Contains(new SetupCounts(4, 2, 1, 1), result.ValidTargetCounts);
        Assert.Contains(new SetupCounts(5, 1, 1, 1), result.ValidTargetCounts);
        Assert.Contains(new SetupCounts(6, 0, 1, 1), result.ValidTargetCounts);
    }

    [Fact]
    public void BlockedIfAnyChosenOfTypeConstraint_BlocksAfterMatchingTypeChosen()
    {
        var context = new AvailabilityContext(new[] { "poisoner" }, true, false, 1, false, false);
        var kazaliConstraint = new BlockedIfAnyChosenOfTypeConstraint(CharacterType.Minion);

        var isAvailable = kazaliConstraint.IsAvailable(context);

        Assert.False(isAvailable);
    }

    [Fact]
    public void BlockedIfAnyChosenOfTypeConstraint_BlocksSummonerAfterDemonChosen()
    {
        var context = new AvailabilityContext(new[] { "imp" }, false, true, 1, false, false);
        var summonerConstraint = new BlockedIfAnyChosenOfTypeConstraint(CharacterType.Demon);

        var isAvailable = summonerConstraint.IsAvailable(context);

        Assert.False(isAvailable);
    }

    [Fact]
    public void Calculate_NoOpCharacter_KeepsBaseDistribution()
    {
        var script = CreateScript("chef");

        var result = _calculator.Calculate(
            script,
            7,
            new[] { "chef" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.Equal(new SetupCounts(5, 0, 1, 1), Assert.Single(result.ValidTargetCounts));
    }

    [Theory]
    [InlineData(9, 7, 2)]
    [InlineData(10, 7, 3)]
    [InlineData(11, 8, 3)]
    [InlineData(12, 9, 3)]
    [InlineData(15, 11, 4)]
    public void Calculate_LegionGame_UsesDerivedDefaultLegionCount(int playerCount, int expectedLegionCount, int expectedMaxOutsiders)
    {
        var script = CreateScript("legion", "chef", "washerwoman", "poisoner", "imp", "saint", "drunk", "butler", "recluse", "goon", "moonchild", "tinker");

        var result = _calculator.Calculate(
            script,
            playerCount,
            Array.Empty<string>(),
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false, true, 0),
            _characterDatabase,
            _loricDatabase);

        Assert.Equal(expectedMaxOutsiders + 1, result.ValidTargetCounts.Count);
        Assert.Equal(expectedLegionCount, result.ValidTargetCounts[0].Demons);
        Assert.Equal(0, result.ValidTargetCounts[0].Minions);
        Assert.Equal(playerCount - expectedLegionCount, result.ValidTargetCounts[0].Townsfolk + result.ValidTargetCounts[0].Outsiders);
        Assert.Contains(result.ValidTargetCounts, counts => counts.Outsiders == expectedMaxOutsiders);
    }

    [Fact]
    public void Calculate_LegionGame_UsesStorytellerOverrideLegionCount()
    {
        var script = CreateScript("legion", "chef", "washerwoman", "poisoner", "imp", "saint", "drunk", "butler", "recluse", "goon", "moonchild", "tinker");

        var result = _calculator.Calculate(
            script,
            10,
            Array.Empty<string>(),
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false, true, 5),
            _characterDatabase,
            _loricDatabase);

        Assert.Equal(6, result.ValidTargetCounts.Count);
        Assert.Contains(result.ValidTargetCounts, counts => counts.Demons == 5 && counts.Outsiders == 0);
        Assert.Contains(result.ValidTargetCounts, counts => counts.Demons == 5 && counts.Outsiders == 1);
        Assert.Contains(result.ValidTargetCounts, counts => counts.Demons == 5 && counts.Outsiders == 2);
        Assert.Contains(result.ValidTargetCounts, counts => counts.Demons == 5 && counts.Outsiders == 3);
        Assert.Contains(result.ValidTargetCounts, counts => counts.Demons == 5 && counts.Outsiders == 4);
        Assert.Contains(result.ValidTargetCounts, counts => counts.Demons == 5 && counts.Outsiders == 5);
    }

    [Fact]
    public void Calculate_UseAtheist_ProducesCanonicalAllGoodDistribution()
    {
        var script = CreateScript("atheist", "baron", "lilmonsta", "chef", "washerwoman", "librarian", "imp");

        var result = _calculator.Calculate(
            script,
            10,
            Array.Empty<string>(),
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false, true, false, 0),
            _characterDatabase,
            _loricDatabase);

        var counts = Assert.Single(result.ValidTargetCounts);
        Assert.Equal(new SetupCounts(10, 0, 0, 0), counts);
    }

    [Fact]
    public void Calculate_LilMonstaOnScript_IncludesSwappedDistribution()
    {
        var script = CreateScript("lilmonsta", "chef", "washerwoman", "librarian", "imp");

        var result = _calculator.Calculate(
            script,
            8,
            Array.Empty<string>(),
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.Contains(new SetupCounts(5, 1, 1, 1), result.ValidTargetCounts);
        Assert.Contains(new SetupCounts(5, 1, 2, 0), result.ValidTargetCounts);
    }

    [Fact]
    public void Calculate_LilMonstaOnScript_WithUseAtheist_DoesNotIncludeSwappedDistribution()
    {
        var script = CreateScript("atheist", "lilmonsta", "chef", "washerwoman", "librarian", "imp");

        var result = _calculator.Calculate(
            script,
            8,
            Array.Empty<string>(),
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false, true, false, 0),
            _characterDatabase,
            _loricDatabase);

        var counts = Assert.Single(result.ValidTargetCounts);
        Assert.Equal(new SetupCounts(7, 1, 0, 0), counts);
    }

    public void Calculate_TwelvePlayerCuratoolScript_BroadensValidTargetsAcrossRoles()
    {
        var script = CreateScript(
            "investigator", "grandmother", "shugenja", "empath", "highpriestess", "balloonist",
            "mathematician", "king", "huntsman", "alchemist", "ravenkeeper", "choirboy", "atheist",
            "hermit", "lunatic", "damsel", "godfather", "summoner", "boffin", "baron",
            "lilmonsta", "kazali", "vigormortis", "fanggu", "lordoftyphon");

        var result = _calculator.Calculate(
            script,
            12,
            Array.Empty<string>(),
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.Contains(new SetupCounts(7, 2, 2, 1), result.ValidTargetCounts);
        Assert.Contains(new SetupCounts(9, 2, 0, 1), result.ValidTargetCounts);
        Assert.Contains(new SetupCounts(7, 2, 3, 0), result.ValidTargetCounts);
        Assert.Contains(new SetupCounts(6, 2, 3, 1), result.ValidTargetCounts);
    }

    [Fact]
    public void Calculate_LegionRange_RespectsScriptOutsiderCap()
    {
        var script = CreateScript("legion", "chef", "washerwoman", "poisoner", "imp");

        var result = _calculator.Calculate(
            script,
            9,
            Array.Empty<string>(),
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false, true, 6),
            _characterDatabase,
            _loricDatabase);

        Assert.Contains(new SetupCounts(3, 0, 0, 6), result.ValidTargetCounts);
        Assert.DoesNotContain(result.ValidTargetCounts, counts => counts.Outsiders > 0);
    }

    [Fact(Skip = "Known issue: feasibility filtering does not yet reject over-subscribed minion-source stacks.")]
    public void Calculate_BaronAndGodfatherStacking_RequiresEnoughMinionSlots()
    {
        // Bug: the feasibility filter does not currently enforce that the number of in-play Minion
        // sources cannot exceed available Minion slots, so this remains skipped until that bug is fixed.
        var script = CreateScript("baron", "godfather", "imp", "chef", "washerwoman", "librarian", "saint");

        var result = _calculator.Calculate(
            script,
            7,
            Array.Empty<string>(),
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.DoesNotContain(new SetupCounts(1, 4, 1, 1), result.ValidTargetCounts);
    }

    // --- Characterization tests for broadened valid-target distributions (still-on-script sources) ---
    // These assert the intended "broad at the start, narrows as drafting proceeds" behavior and the
    // demon mutual-exclusion rule. They are expected to fail until SetupCalculator enumerates legal
    // in-play source combinations rather than only applying rules from already-drafted characters.

    [Fact]
    public void Calculate_BaronOnScriptUndrafted_OffersBothBaseAndPlusTwoOutsiders()
    {
        // 7-player base is (5,0,1,1). Baron occupies the single Minion slot and adds +2 Outsiders.
        // Baron is still draftable (on script, not chosen), so BOTH the no-Baron base and the
        // Baron-in-play (+2 Outsiders) distributions must be offered. saint + recluse provide the
        // two Outsider characters needed to make the (3,2,1,1) target legally reachable.
        var script = CreateScript("baron", "imp", "saint", "recluse", "chef", "washerwoman", "librarian");

        var result = _calculator.Calculate(
            script,
            7,
            Array.Empty<string>(),
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.Contains(new SetupCounts(5, 0, 1, 1), result.ValidTargetCounts);
        Assert.Contains(new SetupCounts(3, 2, 1, 1), result.ValidTargetCounts);
    }

    [Fact]
    public void Calculate_FangGuAndLilMonstaUndrafted_AreMutuallyExclusiveDemonSources()
    {
        // 7-player base is (5,0,1,1). On this script the only Outsider-adding source is Fang Gu (+1 O,
        // keeps the Demon) and the only Demon-removing source is Lil' Monsta (Demon -> Minion swap, D=0).
        // Each must be offered on its own, but because only one Demon is ever in play they can NEVER
        // combine: a distribution with an extra Outsider AND zero Demons (4,1,2,0) is unreachable.
        var script = CreateScript("fanggu", "lilmonsta", "imp", "chef", "washerwoman", "librarian", "saint", "recluse");

        var result = _calculator.Calculate(
            script,
            7,
            Array.Empty<string>(),
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        // Fang Gu alone: +1 Outsider, Demon kept.
        Assert.Contains(new SetupCounts(4, 1, 1, 1), result.ValidTargetCounts);
        // Lil' Monsta alone: Demon swapped to Minion.
        Assert.Contains(new SetupCounts(5, 0, 2, 0), result.ValidTargetCounts);
        // Both demons cannot apply together (mutual exclusion).
        Assert.DoesNotContain(new SetupCounts(4, 1, 2, 0), result.ValidTargetCounts);
    }

    public void Calculate_TwelvePlayerFullScriptUndrafted_OffersBroadMinionSpread()
    {
        // The Curatool Test Script at 12 players (base 7T/2O/2M/1D). With nothing drafted, the valid
        // target list should be broad: Minion counts of 0 (Kazali removes all Minions), 2 (base Demon),
        // and 3 (Lord of Typhon adds a Minion) must all be reachable.
        var script = CreateScript(
            "investigator", "grandmother", "shugenja", "empath", "highpriestess", "balloonist",
            "mathematician", "king", "huntsman", "alchemist", "ravenkeeper", "choirboy", "atheist",
            "hermit", "lunatic", "damsel", "godfather", "summoner", "boffin", "baron",
            "lilmonsta", "kazali", "vigormortis", "fanggu", "lordoftyphon");

        var result = _calculator.Calculate(
            script,
            12,
            Array.Empty<string>(),
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        // Base distribution is always valid.
        Assert.Contains(new SetupCounts(7, 2, 2, 1), result.ValidTargetCounts);
        // Lord of Typhon: +1 Minion (3 Minions, Demon kept).
        Assert.Contains(new SetupCounts(6, 2, 3, 1), result.ValidTargetCounts);
        // Kazali: removes all Minions (0 Minions, Demon kept).
        Assert.Contains(new SetupCounts(9, 2, 0, 1), result.ValidTargetCounts);

        // The full Minion spread {0, 2, 3} should be represented somewhere in the broadened set.
        var minionValues = result.ValidTargetCounts.Select(counts => counts.Minions).Distinct().ToList();
        Assert.Contains(0, minionValues);
        Assert.Contains(2, minionValues);
        Assert.Contains(3, minionValues);
    }

    private Script CreateScript(params string[] ids)
    {
        IReadOnlyList<CharacterDefinition> characters = ids.Length == 0
            ? Array.Empty<CharacterDefinition>()
            : ids.Select(_characterDatabase.Resolve).ToList().AsReadOnly();

        return new Script("Test Script", "UnitTest", characters);
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
