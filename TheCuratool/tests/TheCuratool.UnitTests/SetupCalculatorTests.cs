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
        Assert.Contains(new SetupCounts(3, 2, 1, 1), result.ValidTargetCounts);
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
        var script = CreateScript("fang_gu");

        var result = _calculator.Calculate(
            script,
            7,
            new[] { "fang_gu" },
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
        var script = CreateScript("fang_gu");

        var result = _calculator.Calculate(
            script,
            7,
            new[] { "fang_gu" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>
            {
                ["fang_gu"] = new HiddenFlags(false, true),
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
    public void Calculate_LordOfTyphon_UsesDeferredOutsiderAndExtraMinionRule()
    {
        var script = CreateScript("lord_of_typhon");

        var result = _calculator.Calculate(
            script,
            7,
            new[] { "lord_of_typhon" },
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false),
            _characterDatabase,
            _loricDatabase);

        Assert.Contains(new SetupCounts(4, 0, 2, 1), result.ValidTargetCounts);
    }

    [Fact]
    public void Marionette_IsNeverPartOfSetupCalculationInput()
    {
        var marionette = _characterDatabase.Resolve("marionette");

        Assert.True(marionette.IsDraftExcluded);
    }

    [Fact]
    public void Calculate_Drunk_AppliesReplaceTownsfolkRule()
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

        Assert.Contains(new SetupCounts(4, 1, 1, 1), result.ValidTargetCounts);
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

        Assert.Contains(new SetupCounts(4, 2, 1, 1), result.ValidTargetCounts);
        Assert.Contains(new SetupCounts(6, 0, 1, 1), result.ValidTargetCounts);
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
    public void AtheistConstraint_RequiresFirstPickUnlessDrunkFlagApplied()
    {
        var blockedContext = new AvailabilityContext(new[] { "poisoner" }, true, false, 1, false, false);
        var drunkOverrideContext = new AvailabilityContext(new[] { "poisoner" }, true, false, 1, true, false);
        var constraint = new AtheistFirstPickConstraint();

        Assert.False(constraint.IsAvailable(blockedContext));
        Assert.True(constraint.IsAvailable(drunkOverrideContext));
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
    [InlineData(9, 7)]
    [InlineData(10, 7)]
    [InlineData(11, 8)]
    [InlineData(12, 9)]
    [InlineData(15, 11)]
    public void Calculate_LegionGame_UsesDerivedDefaultLegionCount(int playerCount, int expectedLegionCount)
    {
        var script = CreateScript("legion", "chef", "washerwoman", "poisoner", "imp");

        var result = _calculator.Calculate(
            script,
            playerCount,
            Array.Empty<string>(),
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false, true, 0),
            _characterDatabase,
            _loricDatabase);

        var counts = Assert.Single(result.ValidTargetCounts);
        Assert.Equal(expectedLegionCount, counts.Demons);
        Assert.Equal(0, counts.Minions);
        Assert.Equal(playerCount - expectedLegionCount, counts.Townsfolk + counts.Outsiders);
    }

    [Fact]
    public void Calculate_LegionGame_UsesStorytellerOverrideLegionCount()
    {
        var script = CreateScript("legion", "chef", "washerwoman", "poisoner", "imp");

        var result = _calculator.Calculate(
            script,
            10,
            Array.Empty<string>(),
            Array.Empty<string>(),
            new Dictionary<string, HiddenFlags>(),
            new SessionSetupOptions(false, true, 5),
            _characterDatabase,
            _loricDatabase);

        var counts = Assert.Single(result.ValidTargetCounts);
        Assert.Equal(5, counts.Demons);
        Assert.Equal(5, counts.Townsfolk + counts.Outsiders);
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
