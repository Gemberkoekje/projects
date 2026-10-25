using System.Text.Json.Nodes;
using Vista.Cli;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// How a request asks for the ground's materials (vista-plan §10 M1.12) — the CLI options, the sheet axes, the sheet's labels and
/// the sidecar — and the built-in spectra they shade with.
/// </summary>
[TestClass]
public sealed class MaterialOptionsTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vista-materials-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "r.json"), VistaJson.Serialize(new Recipe { SetPieces = [SetPiece.SnowCappedMountains] }));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static PreviewRequest Parse(params string[] extra)
    {
        string[] args = ["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", .. extra];
        Assert.IsTrue(PreviewCommandLine.TryParse(args, out PreviewRequest? request, out IReadOnlyList<string> errors), string.Join(" | ", errors));
        return request!;
    }

    private static string Error(params string[] extra)
    {
        string[] args = ["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", .. extra];
        Assert.IsFalse(PreviewCommandLine.TryParse(args, out _, out IReadOnlyList<string> errors));
        return string.Join(" | ", errors);
    }

    [TestMethod]
    public void TheCommandLine_SetsTheMaterialsAndTheDiffuseModel()
    {
        PreviewRequest defaults = Parse();
        Assert.IsTrue(defaults.Mesh.Materials, "materials by rule by default");
        Assert.AreEqual(DiffuseModel.OrenNayar, defaults.Diffuse);
        Assert.IsFalse(Parse("--materials", "off").Mesh.Materials);
        Assert.AreEqual(DiffuseModel.Lambert, Parse("--diffuse", "lambert").Diffuse);
        Assert.AreEqual(DiffuseModel.OrenNayar, Parse("--diffuse", "orenNayar").Diffuse);
        StringAssert.Contains(Error("--materials", "some"), "--materials must be on or off");
        StringAssert.Contains(Error("--diffuse", "phong"), "--diffuse must be orenNayar or lambert");
        StringAssert.Contains(Error("--diffuse", "1"), "--diffuse must be");
    }

    [TestMethod]
    public void ASheet_VariesTheMaterialsAndTheDiffuseModel()
    {
        var request = new PreviewRequest { RecipePath = "r.json", Seed = 1, OutPath = "p.png" };
        Assert.IsTrue(SheetAxis.TryParse("materials=off,on", out SheetAxis? materials, out string? problem), problem);
        Assert.IsFalse(materials!.Apply(request, 0).Mesh.Materials);
        Assert.IsTrue(SheetAxis.TryParse("diffuse=lambert,orenNayar", out SheetAxis? diffuse, out problem), problem);
        Assert.AreEqual(DiffuseModel.Lambert, diffuse!.Apply(request, 0).Diffuse);
        Assert.IsFalse(SheetAxis.TryParse("diffuse=lambert,glossy", out _, out problem));
        StringAssert.Contains(problem, "not orenNayar or lambert");
    }

    [TestMethod]
    public void ASheetFromSidecars_LabelsTheMaterialsAndTheDiffuseModel()
    {
        JsonNode Sidecar(string extra) => JsonNode.Parse($$"""{ "schema": 17, "seed": 42, "defaultCamera": true, "image": { "width": 320, "height": 180 {{extra}} } }""")!;
        Assert.AreEqual("lambert", SheetLabels.Identity(Sidecar(""))["image.diffuse"], "before schema 17 every surface was Lambert's");
        Assert.AreEqual("orenNayar", SheetLabels.Identity(Sidecar(""", "diffuse": "orenNayar" """))["image.diffuse"]);
        Assert.AreEqual("M0", SheetLabels.Identity(Sidecar(""))["materials"]);
        JsonNode byRule = JsonNode.Parse("""{ "schema": 17, "seed": 42, "materials": { "byRule": true } }""")!;
        Assert.AreEqual("by rule", SheetLabels.Identity(byRule)["materials"]);
    }

    [TestMethod]
    public void TheSidecar_RecordsTheMaterials()
    {
        var request = new PreviewRequest
        {
            RecipePath = Path.Combine(_dir, "r.json"),
            Seed = 3,
            OutPath = Path.Combine(_dir, "p.png"),
            Width = 32,
            Height = 18,
            SamplesPerPixel = 2,
            Grid = TestWorlds.Grid,
            Erosion = false,
        };
        Sidecar rule = PreviewPipeline.Run(request, new FakeRenderer());
        Assert.AreEqual(21, rule.Schema);
        MaterialsInfo materials = rule.Materials!;
        Assert.IsTrue(materials.ByRule);
        CollectionAssert.AreEqual(new[] { "rock.hard", "rock.soft", "scree", "soil", "grass", "forest", "snow", "ice", "firn", "snow.wet" },
            materials.Table.Select(m => m.Key).ToArray());
        Assert.AreEqual(2892.0, materials.GrassLineMetres!.Value, 1.0, "Zermatt's, the default place");
        StringAssert.Contains(materials.GrassLineFrom, "Zermatt");
        Assert.AreEqual(1, materials.Poleward);
        Assert.AreEqual(MaterialRules.Default, materials.Rules);
        Assert.IsNotNull(materials.Detail);
        Assert.AreEqual(DiffuseModel.OrenNayar, rule.Image.Diffuse);
        Assert.IsTrue(rule.World.Mesh!.Materials);
        Assert.IsNull(materials.Shares, "only a dry run measures the shares");
        // The forest (M1.12b): below Zermatt's tree line, its canopy by the kernels, its crowns in the mesh.
        Assert.IsTrue(materials.Forest);
        Assert.AreEqual(2276.0, materials.TreeLineMetres!.Value, 1.0, "July's 14.15 °C at 1638 m, 10 °C 638 m higher");
        StringAssert.Contains(materials.TreeLineFrom, "Zermatt");
        Assert.AreEqual(CanopyModel.Kernels, rule.Image.Canopy);
        Assert.AreEqual(new CanopyDetail(0).MaxAmplitude, materials.Detail.CanopyAmplitudeMetres);

        Sidecar off = PreviewPipeline.Run(request with { Mesh = new MeshSettings { Materials = false }, Diffuse = DiffuseModel.Lambert }, new FakeRenderer());
        Assert.IsFalse(off.Materials!.ByRule);
        CollectionAssert.AreEqual(new[] { "terrain" }, off.DataTiers.Keys.ToArray());
        Assert.IsFalse(off.World.Mesh!.Materials);
        Assert.AreEqual(DiffuseModel.Lambert, off.Image.Diffuse);

        Sidecar bare = PreviewPipeline.Run(request with { Forest = false, Canopy = CanopyModel.Rough }, new FakeRenderer());
        Assert.IsFalse(bare.Materials!.Forest);
        Assert.AreEqual(0.0, bare.Materials.Detail!.CanopyAmplitudeMetres, "no crowns without forest");
        Assert.AreEqual(CanopyModel.Rough, bare.Image.Canopy);

        Sidecar dry = PreviewPipeline.Run(request with { DryRun = true }, renderer: null);
        MaterialShares shares = dry.Materials!.Shares!;
        Assert.AreEqual(1.0, shares.RockHard + shares.RockSoft + shares.Scree + shares.Soil + shares.Grass + shares.Forest + shares.Snow + shares.Firn + shares.Ice, 1e-6);
        MaterialShares noForest = PreviewPipeline.Run(request with { DryRun = true, Forest = false }, renderer: null).Materials!.Shares!;
        Assert.AreEqual(0.0, noForest.Forest);
        Assert.AreEqual(shares.RockHard + shares.RockSoft + shares.Scree, noForest.RockHard + noForest.RockSoft + noForest.Scree, 1e-9,
            "the forest takes only the open ground");
    }

    [TestMethod]
    public void TheGroundsMaterials_AreBuiltInSpectra_ThatTheTablesUse()
    {
        IReadOnlyList<VistaMaterial> ground = MaterialCatalog.Ground;
        for (int m = 0; m < ground.Count; m++)
        {
            VistaMaterial material = ground[m];
            Assert.AreEqual(m, material.Id);
            Assert.AreEqual(DataTier.BuiltIn, material.Tier);
            Assert.IsNotNull(material.Spectrum);
            // Snow is Lambertian (M1.13); every other ground is rough.
            Assert.IsTrue(material.Roughness is > 0f and <= 1f || material.Key.StartsWith("snow", StringComparison.Ordinal) && material.Roughness == 0f, material.Key);
            for (int k = 0; k < material.Spectrum.Count; k += 2)
            {
                Assert.IsTrue(material.Spectrum[k + 1] is > 0.0 and < 1.0, material.Key);
                Assert.IsTrue(k == 0 || material.Spectrum[k] > material.Spectrum[k - 2], $"{material.Key}: wavelengths rise");
            }
            Assert.AreEqual(WavelengthTable.For(WavelengthTable.DefaultQuadrature).LinearRgbOf(material.Spectrum), material.LinearRgbAlbedo,
                $"{material.Key}: its albedo is its spectrum's colour");
        }
        Assert.AreEqual(MaterialCatalog.RockHardId, ground.Single(m => m.Key == "rock.hard").Id);
        Assert.AreEqual(MaterialCatalog.GrassId, ground.Single(m => m.Key == "grass").Id);

        IntegratorTables tables = IntegratorTables.Build(ground, WavelengthTable.Bins);
        for (int i = 0; i < tables.Count; i++)
        {
            double nm = tables.WavelengthNm(i);
            Assert.AreEqual(IntegratorTables.ReflectanceAt(ground[MaterialCatalog.SoilId].Spectrum!, nm), tables.Reflectance[MaterialCatalog.SoilId * tables.Count + i], 1e-6);
        }
        Assert.AreEqual(ground[MaterialCatalog.ScreeId].Roughness, tables.Albedo[MaterialCatalog.ScreeId * 4 + 3], "the roughness rides in the albedo's fourth float");
        Assert.AreEqual(0f, IntegratorTables.Build(MaterialCatalog.M0, WavelengthTable.Bins).Albedo[3], "M0's terrain is Lambert's");
        IntegratorTables engine = IntegratorTables.Build(ground, WavelengthTable.Engine);
        (float er, float eg, float eb) = WavelengthTable.Engine.LinearRgbOf(ground[MaterialCatalog.GrassId].Spectrum!);
        CollectionAssert.AreEqual(new[] { er, eg, eb }, engine.Albedo[(MaterialCatalog.GrassId * 4)..(MaterialCatalog.GrassId * 4 + 3)],
            "a spectrum's albedo is its colour through the render's own quadrature");

        // Green in the green, more than in the red: chlorophyll's absorption.
        (float r, float g, float b) = ground[MaterialCatalog.GrassId].LinearRgbAlbedo;
        Assert.IsTrue(g > 1.3f * r && g > 2f * b, $"grass {r} {g} {b}");
        (r, _, b) = ground[MaterialCatalog.SoilId].LinearRgbAlbedo;
        Assert.IsTrue(r > 2f * b, "soil is brown");
        Assert.IsTrue(ground[MaterialCatalog.RockSoftId].LinearRgbAlbedo.G < 0.5f * ground[MaterialCatalog.RockHardId].LinearRgbAlbedo.G, "soft rock is the darker");
    }

    [TestMethod]
    public void ASpectrum_IsLinearBetweenItsPoints_AndFlatPastItsEnds()
    {
        double[] spectrum = [400, 0.1, 500, 0.3];
        Assert.AreEqual(0.1, IntegratorTables.ReflectanceAt(spectrum, 380));
        Assert.AreEqual(0.2, IntegratorTables.ReflectanceAt(spectrum, 450), 1e-12);
        Assert.AreEqual(0.3, IntegratorTables.ReflectanceAt(spectrum, 700));
        Assert.ThrowsExactly<ArgumentException>(() => IntegratorTables.ReflectanceAt([400, 0.1, 500], 450));
    }
}
