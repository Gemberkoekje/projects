using System.Text.Json;
using Vista.Recipes;

namespace Vista.Tests;

/// <summary>
/// The recipe file (vista-plan §3.2 / §7.4): it round-trips, and the strict JSON dialect rejects what a
/// hand- or LLM-edited file gets wrong instead of silently ignoring it.
/// </summary>
[TestClass]
public sealed class RecipeTests
{
    private const string AlpineJson = """
        {
          "schema": 1,
          "name": "alpine range at golden hour",
          "terrain": ["alpineRange", "forest"],
          "water": ["lake"],
          "time": "goldenHour",
          "season": "autumn"
        }
        """;

    [TestMethod]
    public void Parse_ReadsEveryField()
    {
        Recipe recipe = VistaJson.ParseRecipe(AlpineJson);
        Assert.AreEqual("alpine range at golden hour", recipe.Name);
        CollectionAssert.AreEqual(new[] { TerrainBlock.AlpineRange, TerrainBlock.Forest }, recipe.Terrain.ToArray());
        CollectionAssert.AreEqual(new[] { WaterBlock.Lake }, recipe.Water.ToArray());
        Assert.AreEqual(TimeOfDay.GoldenHour, recipe.Time);
        Assert.AreEqual(Season.Autumn, recipe.Season);
    }

    [TestMethod]
    public void Serialize_RoundTripsByContent()
    {
        Recipe recipe = VistaJson.ParseRecipe(AlpineJson);
        string json = VistaJson.Serialize(recipe);
        Assert.IsTrue(recipe.ContentEquals(VistaJson.ParseRecipe(json)));
        StringAssert.Contains(json, "\"goldenHour\"");
        StringAssert.Contains(json, "\"alpineRange\"");
    }

    [TestMethod]
    public void Defaults_FillOptionalFields()
    {
        Recipe recipe = VistaJson.ParseRecipe("""{ "terrain": ["rollingHills"] }""");
        Assert.AreEqual(Recipe.CurrentSchema, recipe.Schema);
        Assert.AreEqual(0, recipe.Water.Count);
        Assert.AreEqual(TimeOfDay.Day, recipe.Time);
        Assert.AreEqual(Season.Summer, recipe.Season);
        Assert.IsNull(recipe.Name);
    }

    [TestMethod]
    [DataRow("""{ "terrain": ["rollingHills"], "tme": "dawn" }""", DisplayName = "unknown property (typo)")]
    [DataRow("""{ "terrain": ["rollingHills"], "time": "midnight" }""", DisplayName = "unknown enum value")]
    [DataRow("""{ "terrain": ["rollingHills"], "time": 2 }""", DisplayName = "enum as integer")]
    [DataRow("""{ "water": [] }""", DisplayName = "missing terrain")]
    [DataRow("""{ "terrain": null }""", DisplayName = "null terrain")]
    public void Parse_RejectsMalformedJson(string json) =>
        Assert.ThrowsExactly<JsonException>(() => VistaJson.ParseRecipe(json));

    [TestMethod]
    [DataRow("""{ "schema": 2, "terrain": ["rollingHills"] }""", "schema", DisplayName = "future schema")]
    [DataRow("""{ "terrain": [] }""", "at least one terrain", DisplayName = "empty terrain")]
    [DataRow("""{ "terrain": ["coast", "coast"] }""", "must not repeat", DisplayName = "repeated terrain")]
    [DataRow("""{ "terrain": ["coast"], "water": ["sea", "sea"] }""", "must not repeat", DisplayName = "repeated water")]
    public void Parse_RejectsInvalidRecipes(string json, string expected)
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() => VistaJson.ParseRecipe(json));
        StringAssert.Contains(e.Message, expected);
    }

    [TestMethod]
    public void Contradictions_AreAllowed()
    {
        // vista-plan §3.4: a frozen lake in summer at noon is implausible, not invalid.
        Recipe recipe = VistaJson.ParseRecipe("""{ "terrain": ["coast"], "water": ["frozenLake"], "season": "summer" }""");
        Assert.AreEqual(0, recipe.Validate().Count);
    }
}
