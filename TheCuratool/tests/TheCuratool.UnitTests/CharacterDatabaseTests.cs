using System.IO;

using TheCuratool.Domain;

namespace TheCuratool.UnitTests;

public sealed class CharacterDatabaseTests
{
    [Fact]
    public void LoadFromFile_ResolvesKnownCharacterWithExpectedRulesAndConstraints()
    {
        var filePath = GetCharactersFilePath();

        var database = CharacterDatabase.LoadFromFile(filePath);

        var godfather = database.Resolve("godfather");
        Assert.Equal("Godfather", godfather.DisplayName);
        Assert.Equal(CharacterType.Minion, godfather.Type);
        Assert.False(godfather.IsUnknown);

        var storytellerChoice = Assert.IsType<StoryTellerChoiceSetupRule>(Assert.Single(godfather.SetupRules));
        Assert.Equal(2, storytellerChoice.Options.Count);

        var drunk = database.Resolve("drunk");
        Assert.Equal(CharacterType.Outsider, drunk.Type);
        Assert.IsType<ReplaceTownsfolkSetupRule>(Assert.Single(drunk.SetupRules));

        var nodashii = database.Resolve("nodashii");
        Assert.Equal(CharacterType.Demon, nodashii.Type);

        var kazali = database.Resolve("kazali");
        var minionConstraint = Assert.IsType<BlockedIfAnyChosenOfTypeConstraint>(Assert.Single(kazali.AvailabilityConstraints));
        Assert.Equal(CharacterType.Minion, minionConstraint.Type);

        var summoner = database.Resolve("summoner");
        var demonConstraint = Assert.IsType<BlockedIfAnyChosenOfTypeConstraint>(Assert.Single(summoner.AvailabilityConstraints));
        Assert.Equal(CharacterType.Demon, demonConstraint.Type);

        var atheist = database.Resolve("atheist");
        Assert.IsType<AtheistFirstPickConstraint>(Assert.Single(atheist.AvailabilityConstraints));

        var huntsman = database.Resolve("huntsman");
        Assert.Contains(huntsman.SetupRules, r => r is RequiresCharacterSetupRule requires && requires.RequiredId == "damsel");

        var lilMonsta = database.Resolve("lil_monsta");
        Assert.IsType<MinionSwapSetupRule>(Assert.Single(lilMonsta.SetupRules));

        var alsaahir = database.Resolve("alsaahir");
        Assert.Empty(alsaahir.SetupRules);
    }

    [Fact]
    public void Resolve_UnknownCharacter_UsesTownsfolkAndSimpleCapitalizedDisplayName()
    {
        var filePath = GetCharactersFilePath();

        var database = CharacterDatabase.LoadFromFile(filePath);

        var unknown = database.Resolve("nodashiihomebrew");

        Assert.True(unknown.IsUnknown);
        Assert.Equal(CharacterType.Townsfolk, unknown.Type);
        Assert.Equal("Nodashiihomebrew", unknown.DisplayName);
        Assert.Empty(unknown.SetupRules);
        Assert.Empty(unknown.AvailabilityConstraints);
    }

    private static string GetCharactersFilePath()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data", "characters.json"));
    }
}
