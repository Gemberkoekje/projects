using System.IO;
using System.Text;

using TheCuratool.Application;
using TheCuratool.Domain;

namespace TheCuratool.UnitTests;

public sealed class ScriptParserTests
{
    private readonly CharacterDatabase _characterDatabase = CharacterDatabase.LoadFromFile(GetCharactersFilePath());
    private readonly ScriptParser _parser = new();

    [Fact]
    public void Parse_NoVortoxJson_ParsesExpectedMetadataAndCharacters()
    {
        var json = File.ReadAllText(GetNoVortoxFilePath());

        var result = _parser.Parse(json, _characterDatabase);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Info);
        Assert.Empty(result.Errors);
        Assert.Equal("No Vortox", result.Script.Name);
        Assert.Equal("Gemberkoekje", result.Script.Author);
        Assert.Equal(27, result.Script.Characters.Count);

        var godfather = Assert.Single(result.Script.Characters.Where(c => c.Id == "godfather"));
        Assert.Equal(CharacterType.Minion, godfather.Type);
        var storytellerChoice = Assert.IsType<StoryTellerChoiceSetupRule>(Assert.Single(godfather.SetupRules));
        Assert.Equal(2, storytellerChoice.Options.Count);

        var drunk = Assert.Single(result.Script.Characters.Where(c => c.Id == "drunk"));
        Assert.Equal(CharacterType.Outsider, drunk.Type);

        var nodashii = Assert.Single(result.Script.Characters.Where(c => c.Id == "nodashii"));
        Assert.Equal(CharacterType.Demon, nodashii.Type);

        var vortox = Assert.Single(result.Script.Characters.Where(c => c.Id == "vortox"));
        Assert.Equal(CharacterType.Demon, vortox.Type);
    }

    [Fact]
    public void Parse_ScriptWithoutDemons_AddsWarningOnly()
    {
        const string json = "[\"chef\",\"drunk\",\"baron\"]";

        var result = _parser.Parse(json, _characterDatabase);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Info);
        Assert.Empty(result.Errors);
        Assert.Contains("Script has no Demon characters.", result.Warnings);
    }

    [Fact]
    public void Parse_ScriptWithoutTownsfolk_ReturnsError()
    {
        const string json = "[\"drunk\",\"baron\",\"imp\"]";

        var result = _parser.Parse(json, _characterDatabase);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Info);
        Assert.Contains("Script must include at least one Townsfolk character.", result.Errors);
    }

    [Fact]
    public void Parse_MixedStringAndObjectEntries_MapsIdsAndSupportsUnknowns()
    {
        const string json = "[{\"id\":\"_meta\",\"name\":\"Mixed\",\"author\":\"Tester\"},\"chef\",{\"id\":\"baron\",\"name\":\"Baron\",\"team\":\"minion\"},{\"id\":\"homebrewrole\",\"name\":\"Homebrew\"},\"imp\"]";

        var result = _parser.Parse(json, _characterDatabase);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Info);
        Assert.Equal("Mixed", result.Script.Name);
        Assert.Equal("Tester", result.Script.Author);
        Assert.Equal(4, result.Script.Characters.Count);

        var homebrew = Assert.Single(result.Script.Characters.Where(c => c.Id == "homebrewrole"));
        Assert.True(homebrew.IsUnknown);
        Assert.Equal("Homebrewrole", homebrew.DisplayName);
    }

    [Fact]
    public void Parse_StreamOverload_ReadsAndParsesJson()
    {
        const string json = "[{\"id\":\"_meta\",\"name\":\"Streamed\",\"author\":\"Tester\"},\"chef\",\"imp\"]";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var result = _parser.Parse(stream, _characterDatabase);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Info);
        Assert.Equal("Streamed", result.Script.Name);
        Assert.Equal("Tester", result.Script.Author);
        Assert.Equal(2, result.Script.Characters.Count);
    }

    [Fact]
    public void Parse_ScriptWithoutMinionsOrOutsiders_AddsWarnings()
    {
        const string json = "[\"chef\",\"imp\"]";

        var result = _parser.Parse(json, _characterDatabase);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Info);
        Assert.Contains("Script has no Outsider characters.", result.Warnings);
        Assert.Contains("Script has no Minion characters.", result.Warnings);
    }

    [Fact]
    public void Parse_ChoirboyWithoutKing_AddsInformationalDiagnostic()
    {
        const string json = "[\"choirboy\",\"chef\",\"baron\",\"imp\"]";

        var result = _parser.Parse(json, _characterDatabase);

        Assert.True(result.IsSuccess);
        Assert.Contains("Script includes Choirboy without King; King will be auto-added if Choirboy is drafted.", result.Info);
    }

    [Fact]
    public void Parse_ScriptWithLegion_AddsInformationalDiagnostic()
    {
        const string json = "[\"chef\",\"baron\",\"legion\"]";

        var result = _parser.Parse(json, _characterDatabase);

        Assert.True(result.IsSuccess);
        Assert.Contains("Script includes Legion; Setup will show a Legion game option.", result.Info);
    }

    private static string GetCharactersFilePath()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data", "characters.json"));
    }

    private static string GetNoVortoxFilePath()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "NoVortox.json"));
    }
}
