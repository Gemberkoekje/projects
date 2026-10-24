using System.Numerics;
using System.Text.Json.Nodes;
using RayTracer;
using Vista.Cli;
using Vista.Output;
using Vista.Recipes;
using Vista.Rendering;
using Vista.Scene;

namespace Vista.Tests;

/// <summary>
/// The colour quadrature (vista-plan §10 M1.12a): Vista's own wavelength table of 50 equal-mass bins against dense 1 nm integration
/// of the CIE 1931 functions, the engine's table kept to the bit, the basis that upsamples a colour on either, and how a request
/// chooses one (the CLI, the sheet axis, the sheet's labels, the sidecar).
/// </summary>
[TestClass]
public sealed class QuadratureTests
{
    [TestMethod]
    public void TheBins_Ascend_EachInsideItsBin_From413To661()
    {
        WavelengthTable bins = WavelengthTable.Bins;
        Assert.AreEqual(Quadrature.Bins, bins.Quadrature);
        Assert.AreEqual(50, bins.Nanometres.Count);
        Assert.AreEqual(413, bins.FirstNm);
        Assert.AreEqual(661, bins.LastNm);
        Assert.AreEqual(51, bins.EdgesNm.Count);
        Assert.AreEqual(359.5, bins.EdgesNm[0], 1e-9, "the first bin starts where the table does");
        Assert.AreEqual(830.5, bins.EdgesNm[^1], 1e-9, "the last ends where it ends");
        for (int i = 0; i < 50; i++)
        {
            int nm = bins.Nanometres[i];
            Assert.IsTrue(i == 0 || nm > bins.Nanometres[i - 1], $"entry {i} ascends");
            Assert.IsTrue(nm >= bins.EdgesNm[i] && nm <= bins.EdgesNm[i + 1], $"entry {i}, {nm} nm, lies in its bin [{bins.EdgesNm[i]:0.0}, {bins.EdgesNm[i + 1]:0.0}]");
            Assert.AreEqual(nm, bins.Cie[i * 4 + 3], "the wavelength rides the fourth float");
        }
    }

    [TestMethod]
    public void TheBins_HoldEqualMass_AndTheirWeightsSumToTheWholeFunctions()
    {
        WavelengthTable bins = WavelengthTable.Bins;
        double sx = CieObserver.X.Sum(), sy = CieObserver.Y.Sum(), sz = CieObserver.Z.Sum();
        double mass = (sx + sy + sz) / 50.0;
        double wx = 0.0, wy = 0.0, wz = 0.0;
        for (int i = 0; i < 50; i++)
        {
            Assert.AreEqual(mass, bins.Cie[i * 4] + bins.Cie[i * 4 + 1] + bins.Cie[i * 4 + 2], mass * 1e-6, $"bin {i} holds a fiftieth of the mass");
            wx += bins.Cie[i * 4];
            wy += bins.Cie[i * 4 + 1];
            wz += bins.Cie[i * 4 + 2];
        }
        Assert.AreEqual(sx, wx, sx * 1e-6);
        Assert.AreEqual(sy, wy, sy * 1e-6);
        Assert.AreEqual(sz, wz, sz * 1e-6);
        Assert.AreEqual(106.857, sy, 1e-3, "∫ȳ, as LuminancePerY has it");
    }

    [TestMethod]
    public void TheEnginesTable_IsTheEnginesToTheBit()
    {
        var lookup = new WavelengthLookup();
        SpectralResources engine = SpectralResourceBaker.Bake(lookup, []);
        WavelengthTable table = WavelengthTable.Engine;
        Assert.AreEqual(Quadrature.Engine, table.Quadrature);
        CollectionAssert.AreEqual(engine.DeterXYZ, table.Cie);
        CollectionAssert.AreEqual(engine.DeterWavelengths, table.Nanometres.ToArray());
        Assert.AreEqual(engine.DeterministicCorrection, table.DeterministicCorrection);
        Assert.AreEqual(419, table.FirstNm);
        Assert.AreEqual(651, table.LastNm);
        Assert.AreEqual(0, table.EdgesNm.Count, "the engine's wavelengths have no bins");
        Assert.AreSame(WavelengthTable.Engine, WavelengthTable.For(Quadrature.Engine));
        Assert.AreSame(WavelengthTable.Bins, WavelengthTable.For(Quadrature.Bins));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => WavelengthTable.For(Quadrature.None));
    }

    [TestMethod]
    public void ACycleOf50Samples_VisitsEveryEntryFourTimes()
    {
        IntegratorTables tables = IntegratorTables.Build(MaterialCatalog.M0, WavelengthTable.Bins);
        foreach (uint pixelHash in new uint[] { 0, 17, 49, 123_456_789 })
        {
            var visits = new int[tables.Count];
            for (int sample = 0; sample < tables.Count; sample++)
            {
                int hero = (int)((pixelHash + (uint)sample) % (uint)tables.Count);
                for (int k = 0; k < IntegratorTables.CompanionCount; k++)
                    visits[(hero + k * tables.CompanionStride) % tables.Count]++;
            }
            Assert.IsTrue(visits.All(v => v == 4), $"pixel hash {pixelHash}: {string.Join(",", visits)}");
        }
    }

    [TestMethod]
    public void AFlatSpectrum_IsExactlyEqualEnergyWhite()
    {
        QuadratureError flat = QuadratureCheck.Measure(WavelengthTable.Bins, _ => 1.0);
        Assert.AreEqual(0.0, flat.DeltaE76, 1e-3, "neutral to the float weights' rounding");
        Assert.AreEqual(0.0, flat.LuminanceError, 1e-6);
        QuadratureError engine = QuadratureCheck.Measure(WavelengthTable.Engine, _ => 1.0);
        Assert.IsTrue(engine.DeltaE76 > 3.0 && engine.DeltaA < -3.0, $"the engine's flat grey is green: {engine}");
    }

    [TestMethod]
    public void EverySpectrum_IntegratesWithin1DeltaE_And1Percent_WhereTheEnginesMissesBy2OrMore()
    {
        var report = new List<string>();
        foreach (QuadratureCase spectrum in QuadratureCheck.Cases)
        {
            QuadratureError bins = QuadratureCheck.Measure(WavelengthTable.Bins, spectrum.Spectrum);
            QuadratureError engine = QuadratureCheck.Measure(WavelengthTable.Engine, spectrum.Spectrum);
            report.Add($"{spectrum.Name}: bins {bins.DeltaE76:0.00} ({bins.LuminanceError:P2}), engine {engine.DeltaE76:0.00} ({engine.LuminanceError:P2})");
            Assert.IsTrue(engine.DeltaE76 > 2.0, $"{spectrum.Name}: the engine's table misses by {engine.DeltaE76:0.00}");
            Assert.IsTrue(bins.DeltaE76 < engine.DeltaE76 / 4.0, $"{spectrum.Name}: the bins' {bins.DeltaE76:0.00} against the engine's {engine.DeltaE76:0.00}");
            if (!QuadratureCheck.IsHeldToTheLimit(spectrum))
                continue;
            Assert.IsTrue(bins.DeltaE76 < 1.0, $"{spectrum.Name}: {bins.DeltaE76:0.00} ΔE76");
            Assert.IsTrue(Math.Abs(bins.LuminanceError) < 0.01, $"{spectrum.Name}: luminance {bins.LuminanceError:P2}");
        }
        Assert.AreEqual(16, QuadratureCheck.Cases.Count, string.Join(Environment.NewLine, report));
    }

    [TestMethod]
    public void TheTwilightSun_IsDeepRed_PastAirMass20()
    {
        (Func<double, double> sun, double airMass) = QuadratureCheck.TwilightSun();
        Assert.IsTrue(airMass > 20.0, $"air mass {airMass:0.0}");
        Assert.IsTrue(sun(650.0) > 5.0 * sun(550.0), "mostly deep red");
        Assert.IsTrue(sun(450.0) < 0.03 * sun(650.0), "the blue a thirtieth of the red or less");
        // The last wavelength stands for everything above it: the twilight case's error, recorded (vista-log/M1.12a.md).
        QuadratureCase twilight = QuadratureCheck.Cases.Single(c => !QuadratureCheck.IsHeldToTheLimit(c));
        QuadratureError bins = QuadratureCheck.Measure(WavelengthTable.Bins, twilight.Spectrum);
        Assert.IsTrue(bins.DeltaE76 is > 0.9 and < 1.3, $"{bins.DeltaE76:0.00} ΔE76: recorded in the log; a change moves it");
        Assert.ThrowsExactly<ArgumentException>(() => QuadratureCheck.SlantColumns(Atmosphere.AtmosphereModel.Default, 4000.0, -5.0), "that ray meets the ground");
    }

    [TestMethod]
    public void TheBasis_IsTheEnginesOnItsTable_AndRoundTripsOnTheBins()
    {
        var engine = RgbToReflectanceBasis.Build(SpectralResourceBaker.Bake(new WavelengthLookup(), []));
        ReflectanceBasis ours = ReflectanceBasis.Build(WavelengthTable.Engine);
        CollectionAssert.AreEqual(engine.Data, ours.Data.ToArray(), "the engine's arithmetic, step for step");

        ReflectanceBasis bins = ReflectanceBasis.Build(WavelengthTable.Bins);
        foreach (Vector3 colour in new[] { new Vector3(0.18f), new Vector3(0.3f, 0.2f, 0.1f), new Vector3(0.05f, 0.12f, 0.04f) })
        {
            Vector3 back = bins.ForwardLinearRgb(bins.Reflectance(colour));
            Assert.AreEqual(colour.X, back.X, 1e-5f);
            Assert.AreEqual(colour.Y, back.Y, 1e-5f);
            Assert.AreEqual(colour.Z, back.Z, 1e-5f);
        }
        Assert.IsTrue(bins.Reflectance(new Vector3(0.18f)).All(r => r is > 0f and < 1f), "a grey upsamples to a physical reflectance");
    }

    [TestMethod]
    public void TheCommandLine_AndASheet_ChooseTheQuadrature()
    {
        PreviewRequest Parse(params string[] extra)
        {
            string[] args = ["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", .. extra];
            Assert.IsTrue(PreviewCommandLine.TryParse(args, out PreviewRequest? request, out IReadOnlyList<string> errors), string.Join(" | ", errors));
            return request!;
        }
        Assert.AreEqual(Quadrature.Bins, Parse().Quadrature, "the bins by default");
        Assert.AreEqual(Quadrature.Engine, Parse("--quadrature", "engine").Quadrature);
        Assert.AreEqual(Quadrature.Bins, Parse("--quadrature", "bins").Quadrature);
        Assert.IsFalse(PreviewCommandLine.TryParse(["--preview", "--recipe", "r.json", "--seed", "1", "--out", "p.png", "--quadrature", "2"], out _,
            out IReadOnlyList<string> errors));
        StringAssert.Contains(string.Join(" | ", errors), "--quadrature must be bins or engine");

        var request = new PreviewRequest { RecipePath = "r.json", Seed = 1, OutPath = "p.png" };
        Assert.IsTrue(SheetAxis.TryParse("quadrature=engine,bins", out SheetAxis? axis, out string? problem), problem);
        Assert.AreEqual(Quadrature.Engine, axis!.Apply(request, 0).Quadrature);
        Assert.AreEqual(Quadrature.Bins, axis.Apply(request, 1).Quadrature);
        Assert.IsFalse(SheetAxis.TryParse("quadrature=engine,gauss", out _, out problem));
        StringAssert.Contains(problem, "not bins or engine");
    }

    [TestMethod]
    public void ASheetFromSidecars_LabelsTheQuadrature()
    {
        JsonNode Sidecar(string extra) => JsonNode.Parse($$"""{ "schema": 18, "seed": 42, "defaultCamera": true, "image": { "width": 320, "height": 180 {{extra}} } }""")!;
        Assert.AreEqual("engine", SheetLabels.Identity(Sidecar(""))["image.quadrature"], "before schema 18 every render used the engine's");
        Assert.AreEqual("bins", SheetLabels.Identity(Sidecar(""", "quadrature": "bins" """))["image.quadrature"]);
    }

    [TestMethod]
    public void TheSidecar_RecordsTheQuadrature_AndTheAlbedoThroughIt()
    {
        string dir = Path.Combine(Path.GetTempPath(), "vista-quadrature-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "r.json"), VistaJson.Serialize(new Recipe { SetPieces = [SetPiece.SnowCappedMountains] }));
            var request = new PreviewRequest
            {
                RecipePath = Path.Combine(dir, "r.json"),
                Seed = 3,
                OutPath = Path.Combine(dir, "p.png"),
                Width = 32,
                Height = 18,
                SamplesPerPixel = 2,
                Grid = TestWorlds.Grid,
                Erosion = false,
            };
            Sidecar bins = PreviewPipeline.Run(request, new FakeRenderer());
            Assert.AreEqual(20, bins.Schema);
            Assert.AreEqual(Quadrature.Bins, bins.Image.Quadrature);
            Assert.AreEqual(new WavelengthsInfo(50, 413, 661), bins.Image.Wavelengths);
            Sidecar engine = PreviewPipeline.Run(request with { Quadrature = Quadrature.Engine }, new FakeRenderer());
            Assert.AreEqual(Quadrature.Engine, engine.Image.Quadrature);
            Assert.AreEqual(new WavelengthsInfo(50, 419, 651), engine.Image.Wavelengths);

            (float r, float g, float b) = WavelengthTable.Engine.LinearRgbOf(MaterialCatalog.Ground[MaterialCatalog.GrassId].Spectrum!);
            CollectionAssert.AreEqual(new[] { r, g, b }, engine.Materials!.Table.Single(m => m.Key == "grass").Albedo, "the albedo the render used");
            Assert.AreNotEqual(bins.Materials!.Table.Single(m => m.Key == "grass").Albedo[1], engine.Materials.Table.Single(m => m.Key == "grass").Albedo[1]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
