using System.Globalization;
using Vista.Astronomy;
using Vista.Rendering;
using Vista.Scene;
using Vista.Weather;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The snow of M1.13 (vista-plan §4.5, §10): where the season's snow lies by the material rules (slope, aspect, hollows, wind,
/// altitude) and how wet it is, the glaciers' firn and ice, the forest's dusting, the maps that carry the hollows and the ice, the
/// spectra from ice's absorption, and the snowline from each place's own precipitation.
/// </summary>
[TestClass]
public sealed class SnowTests
{
    /// <summary>Snow from 2500 m with the freezing level at 3000 m, the glaciers' line at 3100 m; the pole toward +z; no forest.</summary>
    private static readonly MaterialConstants K = MaterialRules.Default.Constants(2900f, 1f, 1234u, 2300f, false) with { Glaciers = 1u };

    private static MaterialConstants Snowy => K.WithSnow(new SnowPlacement(true, 2500f, 3000f, 1f, 0f, 3100f));

    /// <summary>A place on bare, dry ground of middling rock, with no breakup.</summary>
    private static MaterialInputs At(float slope = 10f, float altitude = 2800f, float poleward = 0f, float hollow = 0f, float ice = 0f,
        float windward = 0f, float b1 = 0f, float b2 = 0f) =>
        new(slope, poleward, altitude, 0f, 3f, 0f, 0.5f, b1, b2, 0f) { Hollow = hollow, IceMetres = ice, Windward = windward };

    private static MaterialWeights Weigh(MaterialInputs p, MaterialConstants? k = null) => MaterialRules.Evaluate(p, k ?? Snowy);

    private static float Sum(MaterialWeights w) => w.Rock + w.Scree + w.Soil + w.Grass + w.Forest + w.Snow + w.Firn + w.Ice;

    // ── Where the season's snow lies ──────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Snow_LiesAboveTheSnowline_NotBelowIt()
    {
        Assert.IsTrue(Weigh(At(altitude: 2800f)).Snow > 0.99f, "300 m above the line");
        Assert.IsTrue(Weigh(At(altitude: 2200f)).Snow < 0.01f, "300 m below it");
        float atLine = Weigh(At(altitude: 2500f)).Snow;
        Assert.IsTrue(atLine is > 0.3f and < 0.7f, $"half at the line: {atLine}");
        Assert.AreEqual(0f, Weigh(At(altitude: 2800f), K).Snow, "--snow off: none");
    }

    [TestMethod]
    public void SteepFaces_ShedTheSnow_GulliesHoldItSteeper()
    {
        Assert.IsTrue(Weigh(At(slope: 50f, altitude: 3500f)).Snow < 0.01f, "a 50° face stays rock far above the line");
        Assert.IsTrue(Weigh(At(slope: 25f, altitude: 3500f)).Snow > 0.99f, "a 25° slope is white");
        float face = Weigh(At(slope: 42f, altitude: 3500f)).Snow, gully = Weigh(At(slope: 42f, altitude: 3500f, hollow: 6f)).Snow;
        Assert.IsTrue(gully > face + 0.3f, $"a 42° gully holds its snow ({gully}), a 42° face less ({face})");
    }

    [TestMethod]
    public void PolewardFaces_Hollows_AndLeeSides_HoldSnowLower()
    {
        float flat = Weigh(At(slope: 20f, altitude: 2450f)).Snow;
        Assert.IsTrue(Weigh(At(slope: 20f, altitude: 2450f, poleward: 0.34f)).Snow > flat, "a poleward face");
        Assert.IsTrue(Weigh(At(slope: 20f, altitude: 2450f, poleward: -0.34f)).Snow < flat, "an equator-facing one");
        Assert.IsTrue(Weigh(At(slope: 20f, altitude: 2450f, hollow: 3f)).Snow > flat, "a hollow");
        Assert.IsTrue(Weigh(At(slope: 20f, altitude: 2450f, windward: -0.34f)).Snow > flat, "the wind's lee");
        Assert.IsTrue(Weigh(At(slope: 20f, altitude: 2450f, windward: 0.34f)).Snow < flat, "into the wind");
        float ridge = Weigh(At(slope: 10f, altitude: 3500f, hollow: -6f)).Snow;
        Assert.IsTrue(ridge < 0.5f, $"the wind scours a full ridge: {ridge}");
    }

    [TestMethod]
    public void Snow_IsWetBelowTheFreezingLevel_DryAboveIt()
    {
        Assert.IsTrue(Weigh(At(altitude: 2600f)).SnowWet > 0.99f, "400 m below the freezing level");
        Assert.IsTrue(Weigh(At(altitude: 3400f)).SnowWet < 0.01f, "400 m above it");
    }

    [TestMethod]
    public void Forest_StaysDark_WithADusting()
    {
        MaterialConstants k = MaterialRules.Default.Constants(2900f, 1f, 1234u, 3500f, true).WithSnow(new SnowPlacement(true, 1500f, 3000f, 1f, 0f, 3100f));
        MaterialWeights w = MaterialRules.Evaluate(At(slope: 20f, altitude: 2000f) with { Breakup3 = 1f }, k);
        Assert.IsTrue(w.Forest > 0.99f, $"the forest keeps its ground under snow cover: {w}");
        Assert.AreEqual(0f, w.Snow);
        Assert.AreEqual(MaterialRules.Default.ForestDusting, w.Dusting, 1e-6f, "a dusting in its texture");
    }

    // ── The glaciers ───────────────────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Glaciers_AreFirnAboveTheirLine_IceBelow_UnderTheSeasonsSnow()
    {
        MaterialConstants noSnow = K.WithSnow(new SnowPlacement(false, 0f, 0f, 1f, 0f, 3100f));
        MaterialWeights firn = Weigh(At(altitude: 3300f, ice: 50f), noSnow), ice = Weigh(At(altitude: 2900f, ice: 50f), noSnow);
        Assert.IsTrue(firn.Firn > 0.99f, $"above the glaciers' line: {firn}");
        Assert.IsTrue(ice.Ice > 0.99f, $"below it: {ice}");
        Assert.IsTrue(Weigh(At(altitude: 2900f, ice: 0.2f), noSnow).Ice < 0.01f, "a glacier's thin edge is the ground");
        MaterialWeights covered = Weigh(At(altitude: 2900f, ice: 50f));
        Assert.IsTrue(covered.Snow > 0.99f, $"the season's snow covers the glacier above the snowline: {covered}");
        // Without ice, the glaciers' rules change nothing, to the bit.
        MaterialWeights bare = Weigh(At(slope: 33f, altitude: 2700f), noSnow), off = Weigh(At(slope: 33f, altitude: 2700f), noSnow with { Glaciers = 0u });
        Assert.AreEqual(off, bare);
    }

    [TestMethod]
    public void Weights_SumToOne_WithSnowAndGlaciers()
    {
        var random = new Random(13);
        MaterialConstants k = MaterialRules.Default.Constants(2900f, 1f, 1234u, 2600f, true).WithSnow(new SnowPlacement(true, 2400f, 2900f, 1f, 0f, 2800f))
            with { Glaciers = 1u };
        for (int i = 0; i < 20000; i++)
        {
            var p = new MaterialInputs((float)(random.NextDouble() * 70.0), (float)(random.NextDouble() * 2.0 - 1.0), 1500f + (float)random.NextDouble() * 2500f,
                (float)random.NextDouble() * 3f, 1f + (float)random.NextDouble() * 7f, (float)random.NextDouble(), (float)random.NextDouble(),
                (float)(random.NextDouble() * 2.0 - 1.0), (float)(random.NextDouble() * 2.0 - 1.0), (float)(random.NextDouble() * 2.0 - 1.0))
            {
                Hollow = (float)(random.NextDouble() * 30.0 - 15.0),
                IceMetres = random.NextDouble() < 0.3 ? (float)(random.NextDouble() * 100.0) : 0f,
                Windward = (float)(random.NextDouble() * 2.0 - 1.0),
            };
            MaterialWeights w = MaterialRules.Evaluate(p, k);
            Assert.AreEqual(1f, Sum(w), 1e-5f, w.ToString());
            foreach (float v in new[] { w.Rock, w.Scree, w.Soil, w.Grass, w.Forest, w.Snow, w.Firn, w.Ice, w.SnowWet, w.Dusting })
                Assert.IsTrue(v is >= 0f and <= 1f + 1e-6f, w.ToString());
        }
    }

    [TestMethod]
    public void Constants_CarryTheScenesSnow()
    {
        MaterialConstants k = Snowy;
        Assert.AreEqual(1u, k.Snow);
        Assert.AreEqual(2500f, k.SnowLine);
        Assert.AreEqual(3000f, k.FreezingLevel);
        Assert.AreEqual(3100f, k.GlacierLine);
        Assert.AreEqual(MaterialRules.Default.SnowSlopeFromDegrees, k.SnowSlopeFrom);
        Assert.AreEqual(0u, MaterialRules.Default.Constants(2900f, 1f, 1u, 2300f, false).Snow, "no snow until a scene lays it");
        Assert.IsFalse(GroundCover.Default.Snow, "a ground cover lays no snow unless asked");
        GroundCover cover = GroundCover.Default with { Snow = true, SnowlineMetres = 2400.0, FreezingLevelMetres = 3100.0, GlacierLineMetres = 2900.0 };
        Assert.AreEqual(new SnowPlacement(true, 2400f, 3100f, 1f, 0f, 2900f), cover.SnowPlacement, "the westerlies blow toward the east");
        CollectionAssert.AreEqual(Array.Empty<string>(), MaterialRules.Default.Validate().ToArray());
        Assert.AreEqual(1, (MaterialRules.Default with { SnowSlopeFromDegrees = 50f }).Validate().Count);
    }

    // ── The maps ───────────────────────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Codes_KeepTheHollowsAndTheIce()
    {
        Assert.AreEqual(128u, MaterialField.HollowCode(0f));
        Assert.AreEqual(0f, MaterialField.HollowOf(128f));
        foreach (float h in new[] { -20f, -6f, -0.5f, 0.5f, 6f, 20f })
            Assert.AreEqual(h, MaterialField.HollowOf(MaterialField.HollowCode(h)), MaterialField.HollowRange / 254f, h.ToString(CultureInfo.InvariantCulture));
        Assert.AreEqual(255u, MaterialField.HollowCode(100f), "clamped");
        Assert.AreEqual(0u, MaterialField.IceCode(0f));
        Assert.AreEqual(0f, MaterialField.IceOf(0f), 1e-6f);
        foreach (float ice in new[] { 1f, 4f, 30f, 300f })
            Assert.AreEqual(ice, MaterialField.IceOf(MaterialField.IceCode(ice)), ice * 0.03f + 0.02f, ice.ToString(CultureInfo.InvariantCulture));
    }

    // ── The spectra ────────────────────────────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void IceAbsorption_FollowsItsTables()
    {
        Assert.AreEqual(6.896e-10, SnowSpectrum.IceKappa(450.0), 1e-15, "Picard et al. 2016");
        Assert.AreEqual(1.430e-8, SnowSpectrum.IceKappa(650.0), 1e-13, "Warren and Brandt 2008");
        double between = SnowSpectrum.IceKappa(655.0);
        Assert.IsTrue(between > 1.430e-8 && between < 1.660e-8);
        // Absorption about 2 m⁻¹ in the near infrared's edge, about 0.02 m⁻¹ in the blue.
        Assert.AreEqual(0.28, SnowSpectrum.IceAbsorption(650.0), 0.01);
        Assert.AreEqual(0.019, SnowSpectrum.IceAbsorption(450.0), 0.001);
    }

    [TestMethod]
    public void Snow_IsWhite_CoarseWetSnowAndIceLess_MostOfAllInTheRed()
    {
        double fresh450 = SnowSpectrum.Albedo(450.0, 0.2e-3, 0.0, 3.0), fresh650 = SnowSpectrum.Albedo(650.0, 0.2e-3, 0.0, 3.0);
        Assert.IsTrue(fresh450 > 0.98 && fresh650 > 0.95 && fresh650 < fresh450, $"clean fine snow: {fresh450:0.000} blue, {fresh650:0.000} red");
        Assert.IsTrue(SnowSpectrum.Albedo(650.0, 2e-3, 0.0, 3.0) < fresh650, "coarser grains reflect less");
        double dirty450 = SnowSpectrum.Albedo(450.0, 0.2e-3, 1.0, 3.0), dirty650 = SnowSpectrum.Albedo(650.0, 0.2e-3, 1.0, 3.0);
        Assert.IsTrue(fresh450 - dirty450 > fresh650 - dirty650, "dust greys the blue most");
        double[] pairs = SnowSpectrum.Spectrum(0.2e-3, 0.02, 3.0);
        Assert.AreEqual(162, pairs.Length);
        Assert.AreEqual(380.0, pairs[0]);
        Assert.AreEqual(780.0, pairs[^2]);
        // The catalogue's: dry snow nearly white, wet snow and firn greyer, the glaciers' ice the darkest.
        float Y(int id) => MaterialCatalog.Ground[id].LinearRgbAlbedo.G;
        Assert.IsTrue(Y(MaterialCatalog.SnowId) > 0.9f && Y(MaterialCatalog.SnowId) > Y(MaterialCatalog.WetSnowId) && Y(MaterialCatalog.WetSnowId) > Y(MaterialCatalog.FirnId)
            && Y(MaterialCatalog.FirnId) > Y(MaterialCatalog.IceId) && Y(MaterialCatalog.IceId) > 0.4f,
            string.Join(", ", MaterialCatalog.Ground.Skip(6).Select(m => $"{m.Key} {m.LinearRgbAlbedo}")));
        for (int id = 0; id < MaterialCatalog.Ground.Count; id++)
            Assert.AreEqual(id, MaterialCatalog.Ground[id].Id);
        Assert.AreEqual(DataTier.BuiltIn, MaterialCatalog.Ground[MaterialCatalog.SnowId].Tier);
    }

    // ── The snowline from each place's precipitation ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void EveryPlace_HasTwelveMonthsOfPrecipitation_WithASource()
    {
        foreach ((Place place, ClimateNormals normals) in ClimateTable.All)
        {
            Assert.AreEqual(12, normals.Precipitation?.Count, place.ToString());
            CollectionAssert.AreEqual(Array.Empty<string>(), normals.Validate().ToArray(), place.ToString());
            // A year's days sum close to the normals' year.
            double year = Enumerable.Range(0, 365).Sum(d => normals.PrecipitationOn(new DateOnly(2026, 1, 1).AddDays(d))!.Value);
            Assert.AreEqual(normals.Precipitation!.Sum(), year, 0.04 * year, place.ToString());
        }
        Assert.IsNull(LatitudeClimate.For(46.0).Precipitation, "a custom latitude has none");
        Assert.AreEqual(13.0, ClimateTable.For(Place.Lofoten).PrecipitationAltitudeMetres, "Leknes, not Skrova");
    }

    [TestMethod]
    public void Snowline_FollowsEachPlacesPrecipitation()
    {
        double Line(Place place, int month, bool own)
        {
            ClimateNormals normals = ClimateTable.For(place);
            return SnowCover.Snowline(own ? normals : normals with { Precipitation = null }, new DateOnly(2026, month, 15), 12_000.0);
        }
        // Maritime places snow lower than one rate for all would put them, dry ones higher.
        Assert.IsTrue(Line(Place.Fiordland, 1, true) < Line(Place.Fiordland, 1, false) - 300.0, "Fiordland's summer");
        Assert.IsTrue(Line(Place.Lofoten, 7, true) < Line(Place.Lofoten, 7, false) - 200.0, "Lofoten's summer");
        Assert.IsTrue(Line(Place.ScottishHighlands, 4, true) < Line(Place.ScottishHighlands, 4, false), "the Highlands' spring");
        Assert.IsTrue(Line(Place.Banff, 4, true) > Line(Place.Banff, 4, false) + 200.0, "Banff's spring");
        Assert.IsTrue(Line(Place.Yellowstone, 4, true) > Line(Place.Yellowstone, 4, false), "Yellowstone's spring");
        // A custom latitude keeps M1.11's one rate.
        ClimateNormals custom = LatitudeClimate.For(46.0);
        Assert.AreEqual(1.0, SnowCover.PrecipitationFactor(custom, 3000.0));
        Assert.AreEqual(1.0 + SnowCover.PrecipitationGradientPerMetre * 1000.0, SnowCover.PrecipitationFactor(ClimateTable.For(Place.Zermatt), 2638.0), 1e-12);
        Assert.AreEqual(SnowCover.MaxPrecipitationFactor, SnowCover.PrecipitationFactor(ClimateTable.For(Place.Fiordland), 8000.0));
    }

    [TestMethod]
    public void SnowBySlope_CountsWhiteGroundByItsSlope()
    {
        // Six pixels: sky, flat snow, flat grass, a 35° firn ledge, a 45° rock face and a 60° snow-plastered face.
        static (float X, float Y, float Z) Tilted(double degrees) => ((float)Math.Sin(degrees * Math.PI / 180.0), (float)Math.Cos(degrees * Math.PI / 180.0), 0f);
        float[] ids = [-1f, MaterialCatalog.SnowId, MaterialCatalog.GrassId, MaterialCatalog.FirnId, MaterialCatalog.RockHardId, MaterialCatalog.SnowId];
        (float X, float Y, float Z)[] n = [(0f, 0f, 0f), Tilted(0), Tilted(5), Tilted(35), Tilted(45), Tilted(60)];
        float[] normals = [.. n.SelectMany(v => new[] { v.X, v.Y, v.Z })];
        IReadOnlyList<Output.SlopeBand> bands = Output.SnowBySlope.Measure(ids, normals)!;
        Assert.AreEqual(4, bands.Count);
        Assert.AreEqual(2, bands[0].Pixels, "0–30°: the sky is not ground");
        Assert.AreEqual(0.5, bands[0].WhiteShare);
        Assert.AreEqual(1.0, bands[1].WhiteShare, "30–40°: firn counts as white");
        Assert.AreEqual(0.0, bands[2].WhiteShare, "40–50°: bare rock");
        Assert.AreEqual(1.0, bands[3].WhiteShare);
        Assert.IsNull(Output.SnowBySlope.Measure([-1f], [0f, 0f, 0f]), "no ground, no bands");
    }
}
