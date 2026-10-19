using System.Text.Json.Nodes;
using Vista.Recipes;

namespace Vista.Tests;

/// <summary>Field overrides and loose command-line values, the machinery behind <c>--vary</c> (vista-plan §7.4).</summary>
[TestClass]
public sealed class JsonOverridesTests
{
    private static Dictionary<string, string> One(string path, string json) => new() { [path] = json };

    [TestMethod]
    public void Apply_SetsTopLevelAndNestedFields()
    {
        string json = """{ "time": "day", "position": { "x": 1, "y": 2 } }""";
        JsonNode result = JsonNode.Parse(JsonOverrides.Apply(json, new Dictionary<string, string>
        {
            ["time"] = "\"dawn\"",
            ["position.y"] = "5000",
            ["sliders.snow.amount"] = "0.5",
        }))!;
        Assert.AreEqual("dawn", (string?)result["time"]);
        Assert.AreEqual(1, (int?)result["position"]!["x"], "siblings are kept");
        Assert.AreEqual(5000, (int?)result["position"]!["y"]);
        Assert.AreEqual(0.5, (double?)result["sliders"]!["snow"]!["amount"], "missing parents are created");
    }

    [TestMethod]
    public void Apply_WithNoOverrides_ReturnsTheTextUnchanged()
    {
        const string json = "{ \"time\" :\"day\" }";
        Assert.AreSame(json, JsonOverrides.Apply(json, new Dictionary<string, string>()));
    }

    [TestMethod]
    public void Apply_ThroughAValue_FailsLoudly()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => JsonOverrides.Apply("""{ "time": "day" }""", One("time.hour", "6")));
        Assert.ThrowsExactly<InvalidDataException>(() => JsonOverrides.Apply("""{ "time": "day" }""", One("time..x", "6")));
        Assert.ThrowsExactly<InvalidDataException>(() => JsonOverrides.Apply("[1]", One("x", "6")));
    }

    [TestMethod]
    public void Apply_ThenTheStrictParser_CatchesMisspeltFields()
    {
        string recipe = VistaJson.Serialize(new Recipe { SetPieces = [SetPiece.AlpineRange] });
        Assert.AreEqual(TimeOfDay.Dusk, VistaJson.ParseRecipe(JsonOverrides.Apply(recipe, One("time", "\"dusk\""))).Time);
        Assert.ThrowsExactly<System.Text.Json.JsonException>(() => VistaJson.ParseRecipe(JsonOverrides.Apply(recipe, One("tme", "\"dusk\""))));
    }

    [TestMethod]
    [DataRow("goldenHour", "\"goldenHour\"")]
    [DataRow(" dawn ", "\"dawn\"")]
    [DataRow("42", "42")]
    [DataRow("-3.5", "-3.5")]
    [DataRow("1e3", "1e3")]
    [DataRow("true", "true")]
    [DataRow("null", "null")]
    [DataRow("\"a, b\"", "\"a, b\"")]
    [DataRow("042", "\"042\"")]
    [DataRow("[snowCappedMountains, lake]", "[\"snowCappedMountains\",\"lake\"]")]
    [DataRow("[]", "[]")]
    [DataRow("[[1, 2], x]", "[[1,2],\"x\"]")]
    [DataRow("{\"a\": 1}", "{\"a\":1}")]
    public void LooseValue_ReadsBareWordsAsStrings(string text, string json) =>
        Assert.AreEqual(json, JsonOverrides.LooseValue(text));

    [TestMethod]
    [DataRow("")]
    [DataRow("[a, b")]
    [DataRow("\"unclosed")]
    [DataRow("{bad}")]
    public void LooseValue_RejectsMalformedValues(string text) =>
        Assert.ThrowsExactly<FormatException>(() => JsonOverrides.LooseValue(text));

    [TestMethod]
    public void SplitList_SplitsOnlyTopLevelCommas()
    {
        CollectionAssert.AreEqual(new[] { "1", "2", "3" }, JsonOverrides.SplitList("1, 2,3").ToArray());
        CollectionAssert.AreEqual(new[] { "[a,b]", "[c]", "[]" }, JsonOverrides.SplitList("[a,b],[c],[]").ToArray());
        CollectionAssert.AreEqual(new[] { "\"x, y\"", "{\"a\": [1, 2]}" }, JsonOverrides.SplitList("\"x, y\", {\"a\": [1, 2]}").ToArray());
        CollectionAssert.AreEqual(new[] { "\"say \\\"hi, there\\\"\"", "b" }, JsonOverrides.SplitList("\"say \\\"hi, there\\\"\",b").ToArray());
    }

    [TestMethod]
    [DataRow("1,,2")]
    [DataRow("1,")]
    [DataRow("[1,2")]
    [DataRow("1]")]
    [DataRow("\"open")]
    public void SplitList_RejectsEmptyItemsAndUnbalancedBrackets(string text) =>
        Assert.ThrowsExactly<FormatException>(() => JsonOverrides.SplitList(text));
}
