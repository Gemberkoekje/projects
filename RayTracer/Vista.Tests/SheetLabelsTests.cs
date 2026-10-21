using System.Globalization;
using System.Text.Json.Nodes;
using Vista.Output;

namespace Vista.Tests;

/// <summary>Sheet labels and titles read from sidecars (vista-plan §7.4).</summary>
[TestClass]
public sealed class SheetLabelsTests
{
    private static JsonNode Sidecar(string seed = "42", string time = "goldenHour", string version = "1.0.0+566b68d4abcdef",
        bool defaultCamera = true, string window = "", double cameraY = 1692.2126708984372) => JsonNode.Parse($$"""
        {
          "schema": 2,
          "kind": "preview",
          "createdUtc": "2026-10-19T12:00:00Z",
          "engine": { "vistaVersion": "{{version}}", "runtime": ".NET", "device": "GPU" },
          "recipe": { "schema": 1, "name": "alpine range", "setPieces": ["snowCappedMountains"], "time": "{{time}}", "season": "summer" },
          "seed": {{seed}},
          "camera": { "schema": 1, "position": { "x": 0, "y": {{cameraY.ToString("R", CultureInfo.InvariantCulture)}}, "z": -5734.4 }, "yawDegrees": 20, "pitchDegrees": -30,
                      "rollDegrees": 0, "focalLengthMm": 24, "sensorHeightMm": 24, "fStop": 0, "focusDistanceMetres": 1000, "exposureEv": 0 },
          "defaultCamera": {{(defaultCamera ? "true" : "false")}},
          "image": { "width": 320, "height": 180, "samplesPerPixel": 64, "maxBounces": 6, "displayTransform": "aces"{{window}} },
          "timingsSeconds": { "render": 0.5 },
          "render": { "raysPerSample": 2.3 }
        }
        """)!;

    private static IReadOnlyDictionary<string, string> Id(JsonNode node) => SheetLabels.Identity(node);

    [TestMethod]
    public void Identity_KeepsWhatTheImageIsAndDropsWhatItCost()
    {
        IReadOnlyDictionary<string, string> id = Id(Sidecar());
        CollectionAssert.AreEquivalent(
            new[] { "recipe.name", "recipe.setPieces", "sliders.time", "sliders.season", "seed", "camera", "image.size",
                    "image.samplesPerPixel", "image.maxBounces", "image.display", "build" },
            id.Keys.ToArray());
        Assert.AreEqual("aces", id["image.display"], "every sidecar before schema 11 is the ACES fit's");
        Assert.AreEqual("default", id["camera"], "the default camera's pose follows the terrain, so it is not compared");
        Assert.AreEqual("320×180", id["image.size"]);
        Assert.AreEqual("566b68d", id["build"]);
        Assert.AreEqual("snowCappedMountains", id["recipe.setPieces"]);
    }

    [TestMethod]
    public void Identity_TellsTheMetersAndTheCurvesApart()
    {
        JsonNode old = Sidecar();
        old["exposure"] = JsonNode.Parse("""{ "method": "auto", "appliedEv": 3 }""");
        JsonNode card = Sidecar();
        card["exposure"] = JsonNode.Parse("""{ "meter": "incident", "appliedEv": 2 }""");
        card["image"]!["display"] = "agx";
        Assert.AreEqual("average", Id(old)["exposure.meter"], "every sidecar before schema 11 is the average meter's");
        Assert.AreEqual(("incident", "agx"), (Id(card)["exposure.meter"], Id(card)["image.display"]));
        CollectionAssert.AreEqual(new[] { "display=aces look=- meter=average", "display=agx look=base meter=incident" },
            SheetLabels.Differences([Id(old), Id(card)]).ToArray());
        Assert.IsFalse(Id(Sidecar()).ContainsKey("exposure.meter"), "no exposure (a dry run), no meter");
    }

    [TestMethod]
    public void Identity_SpellsOutASuppliedCameraAndALoupe()
    {
        IReadOnlyDictionary<string, string> id = Id(Sidecar(defaultCamera: false,
            window: """, "window": { "x": 10, "y": 20, "width": 64, "height": 32 }"""));
        Assert.AreEqual("0,1692.21,-5734.4", id["camera.position"]);
        Assert.AreEqual("20", id["camera.yawDegrees"]);
        Assert.AreEqual("10,20,64,32", id["image.window"]);
        Assert.IsFalse(id.ContainsKey("camera"));

        IReadOnlyDictionary<string, string> full = Id(Sidecar(window: """, "window": { "x": 0, "y": 0, "width": 320, "height": 180 }"""));
        Assert.IsFalse(full.ContainsKey("image.window"), "a full-frame window is not a loupe");
    }

    [TestMethod]
    public void Identity_OfNothing_IsEmpty()
    {
        Assert.AreEqual(0, SheetLabels.Identity(null).Count);
        Assert.AreEqual(0, SheetLabels.Identity(JsonNode.Parse("[1, 2]")).Count);
        Assert.AreEqual("1.2.3", SheetLabels.Identity(JsonNode.Parse("""{ "engine": { "vistaVersion": "1.2.3" } }"""))["build"]);
    }

    [TestMethod]
    public void Differences_LabelOnlyWhatDiffers()
    {
        CollectionAssert.AreEqual(new[] { "seed=1", "seed=2" },
            SheetLabels.Differences([Id(Sidecar(seed: "1")), Id(Sidecar(seed: "2"))]).ToArray());
        CollectionAssert.AreEqual(new[] { "build=566b68d", "build=abcdef0" },
            SheetLabels.Differences([Id(Sidecar()), Id(Sidecar(version: "1.0.0+abcdef0123"))]).ToArray(),
            "the same view from two builds: a PR's before and after");
        CollectionAssert.AreEqual(new[] { "time=dawn seed=1", "time=dusk seed=2" },
            SheetLabels.Differences([Id(Sidecar(seed: "1", time: "dawn")), Id(Sidecar(seed: "2", time: "dusk"))]).ToArray(),
            "in reading order");
        CollectionAssert.AreEqual(new[] { "", "" }, SheetLabels.Differences([Id(Sidecar()), Id(Sidecar())]).ToArray());
        CollectionAssert.AreEqual(new[] { "seed=1 build=566b68d", "seed=1 build=abcdef0", "seed=2 build=566b68d", "seed=2 build=abcdef0" },
            SheetLabels.Differences([Id(Sidecar(seed: "1")), Id(Sidecar(seed: "1", version: "1.0.0+abcdef0")),
                Id(Sidecar(seed: "2")), Id(Sidecar(seed: "2", version: "1.0.0+abcdef0"))]).ToArray(),
            "with equally many values, reading order");
        CollectionAssert.AreEqual(new[] { "build=566b68d seed=1", "build=abcdef0 seed=1", "build=566b68d seed=2", "build=abcdef0 seed=3" },
            SheetLabels.Differences([Id(Sidecar(seed: "1")), Id(Sidecar(seed: "1", version: "1.0.0+abcdef0")),
                Id(Sidecar(seed: "2")), Id(Sidecar(seed: "3", version: "1.0.0+abcdef0"))]).ToArray(),
            "the field with the fewest values leads, so a before/after keeps its build in view");
        StringAssert.Contains(SheetLabels.Differences([Id(Sidecar()), new Dictionary<string, string>()])[1], "seed=-");
    }

    [TestMethod]
    public void Identity_LeavesOutRecipeFieldsThatAnOverrideReplaced()
    {
        JsonNode sidecar = Sidecar();
        JsonObject recipe = sidecar["recipe"]!.AsObject();
        recipe["location"] = "none";
        recipe["latitude"] = null;
        Assert.IsFalse(Id(sidecar).ContainsKey("sliders.location"), "no place named");
        Assert.IsFalse(Id(sidecar).ContainsKey("sliders.latitude"));

        recipe["location"] = "zermatt";
        recipe["date"] = "2026-10-15";
        recipe["solarTime"] = "17:30:00";
        IReadOnlyDictionary<string, string> id = Id(sidecar);
        Assert.IsFalse(id.ContainsKey("sliders.season"), "the date replaced the season");
        Assert.IsFalse(id.ContainsKey("sliders.time"), "the solar time replaced the time of day");
        StringAssert.Contains(SheetLabels.Title([id]), "alpine range · snowCappedMountains · zermatt · 2026-10-15 · 17:30:00 solar · seed 42");
    }

    [TestMethod]
    public void Title_ListsWhatEveryCellShares()
    {
        string title = SheetLabels.Title([Id(Sidecar(seed: "1")), Id(Sidecar(seed: "2"))]);
        Assert.AreEqual("alpine range · snowCappedMountains · goldenHour · summer · default camera · 320×180 · 64 spp · 6 bounces · aces curve · build 566b68d", title,
            "AgX and the incident meter, the defaults since M1.10a, go without saying; an older sidecar's ACES does not");

        string supplied = SheetLabels.Title([Id(Sidecar(defaultCamera: false))]);
        StringAssert.Contains(supplied, "seed 42 · camera at 0,1692.21,-5734.4 · yaw 20° · pitch -30° · 24 mm · 320×180");
        Assert.IsFalse(supplied.Contains("roll", StringComparison.Ordinal), "a zero roll is left out");
        Assert.AreEqual(string.Empty, SheetLabels.Title([]));
    }

    [TestMethod]
    public void SidecarPath_DropsDottedPartsUntilOneExists()
    {
        string dir = Path.Combine(Path.GetTempPath(), "vista-labels-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "view.json"), "{}");
            Assert.AreEqual(Path.Combine(dir, "view.json"), SheetLabels.SidecarPathFor(Path.Combine(dir, "view.png")));
            Assert.AreEqual(Path.Combine(dir, "view.json"), SheetLabels.SidecarPathFor(Path.Combine(dir, "view.normal.png")));
            Assert.IsNull(SheetLabels.SidecarPathFor(Path.Combine(dir, "other.png")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
