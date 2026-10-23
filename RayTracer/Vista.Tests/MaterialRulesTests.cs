using System.Runtime.CompilerServices;
using Vista.Weather;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The ground's materials by rule (vista-plan §10 M1.12): where rock, scree, soil and grass lie, and how wet the ground is, from the
/// slope, the altitude, the aspect, the erosion's maps and the bed's hardness; the noise that breaks their boundaries up; the grass
/// line from the climate; and the constants the shader reads.
/// </summary>
[TestClass]
public sealed class MaterialRulesTests
{
    /// <summary>M1.12's rules: no forest (<see cref="ForestTests"/> has the forest's).</summary>
    private static readonly MaterialConstants K = MaterialRules.Default.Constants(2900f, 1f, 1234u, 2300f, false);

    /// <summary>A place on bare, dry ground of middling rock below the grass line, with no breakup.</summary>
    private static MaterialInputs Place(float slope = 10f, float loose = 0f, float hardness = 0.5f, float altitude = 2000f, float poleward = 0f,
        float area = 1000f, float water = 0f, float b1 = 0f, float b2 = 0f) =>
        new(slope, poleward, altitude, loose, MathF.Log10(area), water, hardness, b1, b2, 0f);

    private static MaterialWeights Weigh(MaterialInputs p) => MaterialRules.Evaluate(p, K);

    [TestMethod]
    public void SteepFaces_AreBareRock_GentleGroundIsNot()
    {
        Assert.IsTrue(Weigh(Place(slope: 55f)).Rock > 0.99f, "a 55° face");
        Assert.IsTrue(Weigh(Place(slope: 10f)).Rock < 0.01f, "a 10° slope");
        Assert.IsTrue(Weigh(Place(slope: 55f, loose: 3f)).Rock < 0.5f, "thick loose material on a steep slope is partly scree");
    }

    [TestMethod]
    public void HardBeds_StandBare_WhereSoftOnesWeather()
    {
        // At 36°, hard rock is a face and soft rock is ground; through thin cover on a moderate slope hard rock crops out.
        Assert.IsTrue(Weigh(Place(slope: 36f, hardness: 1f)).Rock > 0.9f);
        Assert.IsTrue(Weigh(Place(slope: 36f, hardness: 0f)).Rock < 0.1f);
        Assert.IsTrue(Weigh(Place(slope: 32f, hardness: 1f)).Rock > 0.7f, "an outcrop");
        Assert.IsTrue(Weigh(Place(slope: 32f, hardness: 1f, loose: 2f)).Rock < 0.15f, "mostly buried under scree");
    }

    [TestMethod]
    public void Deposition_BelowAFace_IsScree()
    {
        MaterialWeights talus = Weigh(Place(slope: 32f, loose: 4f));
        Assert.IsTrue(talus.Scree > 0.95f, $"loose material at the talus angle: {talus}");
        MaterialWeights floor = Weigh(Place(slope: 8f, loose: 20f));
        Assert.IsTrue(floor.Scree < 0.01f && floor.Grass > 0.9f, $"the same material on a valley floor is meadow: {floor}");
        MaterialWeights bed = Weigh(Place(slope: 3f, loose: 5f, area: 5e6f));
        Assert.IsTrue(bed.Scree > 0.95f && bed.Wet > 0.95f, $"in a stream's channel it is wet gravel: {bed}");
    }

    [TestMethod]
    public void Grass_GrowsBelowItsLine_LowerOnPolewardFaces()
    {
        Assert.IsTrue(Weigh(Place(altitude: 2500f)).Grass > 0.99f);
        MaterialWeights high = Weigh(Place(altitude: 3300f));
        Assert.IsTrue(high.Grass < 0.01f && high.Soil > 0.99f && high.Stony > 0.99f, $"above the line, stony soil: {high}");
        // 30° faces at the line: the equator-facing one keeps more grass than the poleward one.
        float south = Weigh(Place(slope: 30f, altitude: 2900f, poleward: -0.5f)).Grass;
        float north = Weigh(Place(slope: 30f, altitude: 2900f, poleward: 0.5f)).Grass;
        Assert.IsTrue(south > 0.9f && north < 0.1f, $"south {south}, north {north}");
        Assert.IsTrue(Weigh(Place(slope: 50f, hardness: 0f)).Grass < 0.01f, "too steep for grass");
    }

    [TestMethod]
    public void Meadows_OpenOnBareSoil_WhereTheFineNoiseIsLow()
    {
        Assert.IsTrue(Weigh(Place(b2: -0.9f)).Soil > 0.99f);
        Assert.IsTrue(Weigh(Place(b2: 0f)).Grass > 0.99f);
    }

    [TestMethod]
    public void Water_StandingDeep_IsWet_SteepFacesAreNot()
    {
        Assert.IsTrue(Weigh(Place(water: 2f)).Wet > 0.99f);
        Assert.IsTrue(Weigh(Place(water: 0.1f)).Wet < 0.01f);
        Assert.IsTrue(Weigh(Place(slope: 60f, area: 5e6f)).Wet < 0.01f, "a stream over a face is a waterfall, not wet ground");
    }

    [TestMethod]
    public void Weights_SumToOne_AndStayInRange()
    {
        var random = new Random(3);
        for (int i = 0; i < 20_000; i++)
        {
            var p = new MaterialInputs((float)(random.NextDouble() * 90.0), (float)(random.NextDouble() * 2.0 - 1.0), (float)(random.NextDouble() * 5000.0),
                (float)(random.NextDouble() * 50.0), (float)(1.0 + random.NextDouble() * 8.0), (float)(random.NextDouble() * 3.0), (float)random.NextDouble(),
                (float)(random.NextDouble() * 2.0 - 1.0), (float)(random.NextDouble() * 2.0 - 1.0), (float)(random.NextDouble() * 2.0 - 1.0));
            MaterialWeights w = Weigh(p);
            foreach (float v in new[] { w.Rock, w.Scree, w.Soil, w.Grass, w.Wet, w.Stony })
                Assert.IsTrue(v is >= -1e-6f and <= 1f + 1e-6f, $"{w} at {p}");
            Assert.AreEqual(1f, w.Rock + w.Scree + w.Soil + w.Grass, 1e-5f, $"{w} at {p}");
            Assert.AreEqual(w.Soil + w.Grass, w.Smooth, 1e-6f);
            Assert.AreEqual(0f, w.Forest, "no forest without it");
        }
    }

    [TestMethod]
    public void SmoothStep_IsHlsls_AndFallsWhenItsEdgesDo()
    {
        Assert.AreEqual(0f, MaterialRules.SmoothStep(1f, 2f, 0.5f));
        Assert.AreEqual(0.5f, MaterialRules.SmoothStep(1f, 2f, 1.5f));
        Assert.AreEqual(1f, MaterialRules.SmoothStep(1f, 2f, 3f));
        Assert.AreEqual(1f, MaterialRules.SmoothStep(-0.35f, -0.75f, -0.9f), "a falling edge");
        Assert.AreEqual(0f, MaterialRules.SmoothStep(-0.35f, -0.75f, 0f));
    }

    [TestMethod]
    public void Rules_Validate()
    {
        Assert.AreEqual(0, MaterialRules.Default.Validate().Count);
        Assert.AreNotEqual(0, (MaterialRules.Default with { CoverFromMetres = 2f }).Validate().Count, "cover must rise");
        Assert.AreNotEqual(0, (MaterialRules.Default with { WetDarkening = 1f }).Validate().Count);
        Assert.AreNotEqual(0, (MaterialRules.Default with { BumpOctaves = 9 }).Validate().Count);
        Assert.AreNotEqual(0, (MaterialRules.Default with { BareFrom = -0.9f }).Validate().Count, "the bare soil's edge falls");
    }

    [TestMethod]
    public void Constants_AreWholeRows_ThatFitTheShadersBuffer()
    {
        int size = Unsafe.SizeOf<MaterialConstants>();
        Assert.AreEqual(0, size % 16);
        Assert.AreEqual(336, size, "twenty-one rows since M1.12b's forest; the shader's buffer holds 512 bytes");
        Assert.AreEqual(MathF.Log10(MaterialRules.Default.ChannelAreaFromSquareMetres), K.ChannelLogFrom);
        Assert.AreEqual(2900f, K.GrassLine);
        Assert.AreEqual(1234u, K.NoiseSeed);
        Assert.AreEqual(MaterialRules.Default.LaminaePerMetre * RockHardness.TableSpacingMetres, K.LaminaePerEntry);
        Assert.AreEqual(2300f, K.TreeLine);
        Assert.AreEqual(0u, K.Forest);
        Assert.AreEqual(1u, MaterialRules.Default.Constants(2900f, 1f, 1234u, 2300f, true).Forest);
        Assert.AreEqual(MathF.Log10(MaterialRules.Default.MoistAreaToSquareMetres), K.MoistLogTo);
        Assert.AreEqual(MaterialRules.Default.CanopyAlbedoScale, K.CanopyIso);
    }

    [TestMethod]
    public void TheCamerasPlace_SplitsIntoWholeCellsAndTheRest()
    {
        MaterialConstants k = K.ForCamera(-1234.567, 9876.54321, 0.001);
        double cell = MaterialRules.Default.BumpFinestMetres; // the float the shader multiplies by
        Assert.AreEqual(-12346, k.CamCellX);
        Assert.AreEqual(98765, k.CamCellZ);
        Assert.AreEqual(-1234.567, k.CamCellX * cell + k.CamFracX, 1e-5, "the parts add up to the place");
        Assert.AreEqual(9876.54321, k.CamCellZ * cell + k.CamFracZ, 1e-5);
        Assert.IsTrue(k.CamFracX is >= 0f and < 0.1001f && k.CamFracZ is >= 0f and < 0.1001f);
        Assert.AreEqual(0.001f, k.PixelAngle);
    }

    [TestMethod]
    public void Noise_IsAPureFunction_WithinItsRange_AndZeroOnTheLattice()
    {
        Assert.AreEqual(MaterialNoise.Sample(7u, 12.3f, -45.6f), MaterialNoise.Sample(7u, 12.3f, -45.6f));
        Assert.AreNotEqual(MaterialNoise.Sample(7u, 12.3f, -45.6f), MaterialNoise.Sample(8u, 12.3f, -45.6f));
        Assert.AreEqual(0f, MaterialNoise.Sample(7u, 3f, -4f));
        var random = new Random(5);
        float worst = 0f;
        for (int i = 0; i < 50_000; i++)
            worst = MathF.Max(worst, MathF.Abs(MaterialNoise.Sample((uint)random.Next(), (float)(random.NextDouble() * 2000.0 - 1000.0), (float)(random.NextDouble() * 2000.0 - 1000.0))));
        Assert.IsTrue(worst is > 0.5f and <= 1f, $"{worst}");
        // The integrator's PcgHash, pinned (the shader's PcgHash must give the same).
        Assert.AreEqual(129708002u, MaterialNoise.Hash(0u));
        Assert.AreEqual(4099845390u, MaterialNoise.Hash(12345u));
        (float b1, float b2) = MaterialNoise.Breakup(9u, 100f, 200f, 60f, 11f);
        Assert.AreNotEqual(b1, b2, "the two breakups have their own streams");
        Assert.AreEqual(MaterialNoise.SeedOf(42), MaterialNoise.SeedOf(42));
        Assert.AreNotEqual(MaterialNoise.SeedOf(42), MaterialNoise.SeedOf(43));
    }

    [TestMethod]
    public void GrassLine_FollowsTheWarmestMonth()
    {
        double zermatt = GrassLine.AltitudeMetres(ClimateTable.For(Astronomy.Place.Zermatt));
        Assert.AreEqual(2892.0, zermatt, 1.0, "Zermatt: July's 14.15 °C at 1638 m, 6 °C 1254 m higher");
        double lofoten = GrassLine.AltitudeMetres(ClimateTable.For(Astronomy.Place.Lofoten));
        Assert.IsTrue(lofoten < zermatt - 1000.0, $"a cool maritime summer keeps it lower: {lofoten:0} m");
        double torres = GrassLine.AltitudeMetres(ClimateTable.For(Astronomy.Place.TorresDelPaine));
        Assert.IsTrue(torres is > 0.0 and < 1500.0, $"the southern summer is the warmest month there: {torres:0} m");
        Assert.AreEqual(1, Scene.GroundCover.For(ClimateTable.For(Astronomy.Place.Zermatt), 46.0).Poleward);
        Assert.AreEqual(-1, Scene.GroundCover.For(ClimateTable.For(Astronomy.Place.Fiordland), -44.7).Poleward);
    }
}
