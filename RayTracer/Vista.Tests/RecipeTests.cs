using System.Text.Json;
using Vista.Astronomy;
using Vista.Recipes;

namespace Vista.Tests;

/// <summary>
/// The recipe file (vista-plan §3.2 / §7.4): a set of set pieces plus time and season. It round-trips, and
/// the strict JSON dialect rejects what a hand- or LLM-edited file gets wrong instead of silently ignoring it.
/// </summary>
[TestClass]
public sealed class RecipeTests
{
    private const string AutumnLakeJson = """
        {
          "schema": 1,
          "name": "autumn lake, dawn",
          "setPieces": ["snowCappedMountains", "forest", "lake"],
          "time": "dawn",
          "season": "autumn"
        }
        """;

    [TestMethod]
    public void Parse_ReadsEveryField()
    {
        Recipe recipe = VistaJson.ParseRecipe(AutumnLakeJson);
        Assert.AreEqual("autumn lake, dawn", recipe.Name);
        CollectionAssert.AreEqual(new[] { SetPiece.SnowCappedMountains, SetPiece.Forest, SetPiece.Lake }, recipe.SetPieces.ToArray());
        Assert.AreEqual(TimeOfDay.Dawn, recipe.Time);
        Assert.AreEqual(Season.Autumn, recipe.Season);
    }

    [TestMethod]
    public void Serialize_RoundTripsByContent()
    {
        Recipe recipe = VistaJson.ParseRecipe(AutumnLakeJson);
        string json = VistaJson.Serialize(recipe);
        Assert.IsTrue(recipe.ContentEquals(VistaJson.ParseRecipe(json)));
        StringAssert.Contains(json, "\"setPieces\"");
        StringAssert.Contains(json, "\"snowCappedMountains\"");
    }

    [TestMethod]
    public void Defaults_FillOptionalFields()
    {
        Recipe recipe = VistaJson.ParseRecipe("{}");
        Assert.AreEqual(Recipe.CurrentSchema, recipe.Schema);
        Assert.AreEqual(0, recipe.SetPieces.Count, "no pieces = plain base ground");
        Assert.AreEqual(TimeOfDay.Day, recipe.Time);
        Assert.AreEqual(Season.Summer, recipe.Season);
        Assert.IsNull(recipe.Name);
        Assert.AreEqual(Place.None, recipe.Location, "no place named: the default place");
        Assert.IsNull(recipe.Latitude);
        Assert.IsNull(recipe.Date);
        Assert.IsNull(recipe.SolarTime);
    }

    [TestMethod]
    public void SchemaOne_StillReads()
    {
        Recipe recipe = VistaJson.ParseRecipe(AutumnLakeJson);
        Assert.AreEqual(1, recipe.Schema);
        Assert.AreEqual(Place.None, recipe.Location);
    }

    [TestMethod]
    public void PlaceFields_ParseAndRoundTrip()
    {
        Recipe recipe = VistaJson.ParseRecipe(
            "{ \"schema\": 2, \"location\": \"torresDelPaine\", \"date\": \"2026-04-15\", \"solarTime\": \"17:30\", \"time\": \"dusk\" }");
        Assert.AreEqual(Place.TorresDelPaine, recipe.Location);
        Assert.AreEqual(new DateOnly(2026, 4, 15), recipe.Date);
        Assert.AreEqual(new TimeOnly(17, 30), recipe.SolarTime);
        string json = VistaJson.Serialize(recipe);
        StringAssert.Contains(json, "\"location\": \"torresDelPaine\"");
        StringAssert.Contains(json, "\"date\": \"2026-04-15\"");
        Assert.IsTrue(recipe.ContentEquals(VistaJson.ParseRecipe(json)));

        Recipe custom = VistaJson.ParseRecipe("{ \"latitude\": -33.9, \"solarTime\": \"06:05:30\" }");
        Assert.AreEqual(-33.9, custom.Latitude);
        Assert.AreEqual(new TimeOnly(6, 5, 30), custom.SolarTime);
        Assert.IsTrue(custom.ContentEquals(VistaJson.ParseRecipe(VistaJson.Serialize(custom))));
    }

    [TestMethod]
    public void ContentEquals_SeesThePlaceFields()
    {
        var recipe = new Recipe();
        Assert.IsFalse(recipe.ContentEquals(recipe with { Location = Place.Banff }));
        Assert.IsFalse(recipe.ContentEquals(recipe with { Latitude = 10.0 }));
        Assert.IsFalse(recipe.ContentEquals(recipe with { Date = new DateOnly(2026, 1, 1) }));
        Assert.IsFalse(recipe.ContentEquals(recipe with { SolarTime = new TimeOnly(9, 0) }));
        Assert.IsTrue((recipe with { Latitude = 10.0 }).ContentEquals(recipe with { Latitude = 10.0 }));
    }

    [TestMethod]
    public void With_AddsOnce_Without_Removes()
    {
        Recipe mountains = new() { SetPieces = [SetPiece.SnowCappedMountains] };
        Recipe all = mountains.With(SetPiece.Lake).With(SetPiece.Forest).With(SetPiece.Lake);
        CollectionAssert.AreEquivalent(new[] { SetPiece.SnowCappedMountains, SetPiece.Forest, SetPiece.Lake }, all.SetPieces.ToArray());

        Recipe noForest = all.Without(SetPiece.Forest);
        CollectionAssert.AreEquivalent(new[] { SetPiece.SnowCappedMountains, SetPiece.Lake }, noForest.SetPieces.ToArray());
        Assert.AreSame(noForest, noForest.Without(SetPiece.Forest), "removing an absent piece changes nothing");
        Assert.AreEqual(0, noForest.Without(SetPiece.Lake).Without(SetPiece.SnowCappedMountains).Validate().Count, "an empty set is valid");
        CollectionAssert.AreEqual(new[] { SetPiece.SnowCappedMountains }, mountains.SetPieces.ToArray(), "recipes are immutable");
    }

    [TestMethod]
    public void AddingAConflictingPiece_SwapsItIn()
    {
        Recipe withLake = new Recipe { SetPieces = [SetPiece.SnowCappedMountains] }.With(SetPiece.Lake);
        Recipe frozen = withLake.With(SetPiece.FrozenLake);
        CollectionAssert.AreEquivalent(new[] { SetPiece.SnowCappedMountains, SetPiece.FrozenLake }, frozen.SetPieces.ToArray());
        CollectionAssert.AreEquivalent(new[] { SetPiece.SnowCappedMountains, SetPiece.Lake }, frozen.With(SetPiece.Lake).SetPieces.ToArray());
        Assert.AreEqual(0, frozen.Validate().Count);
    }

    [TestMethod]
    public void Conflicts_AreSymmetricAndNeverSelf()
    {
        foreach (SetPiece a in Enum.GetValues<SetPiece>())
        {
            Assert.IsFalse(SetPieceConflicts.Between(a, a), $"{a} conflicts with itself");
            foreach (SetPiece b in Enum.GetValues<SetPiece>())
                Assert.AreEqual(SetPieceConflicts.Between(a, b), SetPieceConflicts.Between(b, a), $"{a}/{b}");
            CollectionAssert.AreEquivalent(
                Enum.GetValues<SetPiece>().Where(b => SetPieceConflicts.Between(a, b)).ToArray(),
                SetPieceConflicts.Of(a).ToArray());
        }
        Assert.IsTrue(SetPieceConflicts.Between(SetPiece.FrozenLake, SetPiece.Lake));
    }

    [TestMethod]
    public void ContentEquals_IgnoresSetPieceOrder()
    {
        Recipe a = new() { SetPieces = [SetPiece.Lake, SetPiece.Forest] };
        Recipe b = new() { SetPieces = [SetPiece.Forest, SetPiece.Lake] };
        Assert.IsTrue(a.ContentEquals(b));
        Assert.IsFalse(a.ContentEquals(b.Without(SetPiece.Lake)));
    }

    [TestMethod]
    [DataRow("""{ "setPieces": ["forest"], "tme": "dawn" }""", DisplayName = "unknown property (typo)")]
    [DataRow("""{ "setPieces": ["forrest"] }""", DisplayName = "unknown set piece")]
    [DataRow("""{ "setPieces": ["forest"], "time": "midnight" }""", DisplayName = "unknown enum value")]
    [DataRow("""{ "setPieces": ["forest"], "time": 2 }""", DisplayName = "enum as integer")]
    [DataRow("""{ "setPieces": null }""", DisplayName = "null set pieces")]
    [DataRow("""{ "terrain": ["alpineRange"] }""", DisplayName = "the old terrain/water shape")]
    [DataRow("""{ "location": "atlantis" }""", DisplayName = "unknown location")]
    [DataRow("""{ "location": 7 }""", DisplayName = "location as integer")]
    [DataRow("""{ "date": "2026-10-5" }""", DisplayName = "date not yyyy-MM-dd")]
    [DataRow("""{ "solarTime": "25:00" }""", DisplayName = "solar time past midnight")]
    [DataRow("""{ "latitude": "north" }""", DisplayName = "latitude not a number")]
    public void Parse_RejectsMalformedJson(string json) =>
        Assert.ThrowsExactly<JsonException>(() => VistaJson.ParseRecipe(json));

    [TestMethod]
    [DataRow("""{ "schema": 5 }""", "schema", DisplayName = "future schema")]
    [DataRow("""{ "altitude": -10 }""", "altitude must be in [0, 8000]", DisplayName = "altitude below sea level")]
    [DataRow("""{ "altitude": 9000 }""", "altitude must be in [0, 8000]", DisplayName = "altitude above the highest valleys")]
    [DataRow("""{ "schema": 0 }""", "schema", DisplayName = "schema zero")]
    [DataRow("""{ "location": "zermatt", "latitude": 46 }""", "not both", DisplayName = "place and custom latitude")]
    [DataRow("""{ "latitude": 91 }""", "Latitude must be in [-90, 90]", DisplayName = "latitude past the pole")]
    [DataRow("""{ "setPieces": ["coast", "coast"] }""", "listed twice", DisplayName = "repeated set piece")]
    [DataRow("""{ "setPieces": ["frozenLake", "forest", "lake"] }""", "Lake and FrozenLake conflict", DisplayName = "conflicting set pieces")]
    public void Parse_RejectsInvalidRecipes(string json, string expected)
    {
        var e = Assert.ThrowsExactly<InvalidDataException>(() => VistaJson.ParseRecipe(json));
        StringAssert.Contains(e.Message, expected);
    }

    [TestMethod]
    public void Contradictions_AreAllowed()
    {
        // vista-plan §3.4: a frozen lake next to the sea in summer is implausible, not invalid.
        Recipe recipe = VistaJson.ParseRecipe("""{ "setPieces": ["sea", "frozenLake"], "season": "summer" }""");
        Assert.AreEqual(0, recipe.Validate().Count);
    }
}
