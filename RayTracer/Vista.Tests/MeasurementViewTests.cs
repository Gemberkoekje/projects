using Vista.Astronomy;
using Vista.Recipes;
using Vista.Scene;
using Vista.World;

namespace Vista.Tests;

/// <summary>
/// The committed measurement views (<c>vista-views/</c>, M1.1): the M1 cost ledger renders them after every
/// slice, so they must stay valid files and keep their cameras above the seed-42 terrain.
/// </summary>
[TestClass]
public sealed class MeasurementViewTests
{
    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "vista-views");

    private static string Read(string file) => File.ReadAllText(Path.Combine(Directory, file));

    [TestMethod]
    public void EveryViewFile_ParsesInTheStrictDialect()
    {
        string[] cameras = System.IO.Directory.GetFiles(Directory, "*.camera.json");
        string[] recipes = System.IO.Directory.GetFiles(Directory, "*.recipe.json");
        CollectionAssert.AreEquivalent(new[] { "aerial", "default", "down-slope", "sky" },
            cameras.Select(f => Path.GetFileName(f).Replace(".camera.json", "", StringComparison.Ordinal)).ToArray());
        Assert.AreEqual(2, recipes.Length);
        foreach (string file in cameras)
            VistaJson.ParseCamera(File.ReadAllText(file));
        foreach (string file in recipes)
            VistaJson.ParseRecipe(File.ReadAllText(file));
    }

    [TestMethod]
    public void TheViews_AreWhatM0Measured()
    {
        Recipe day = VistaJson.ParseRecipe(Read("mountains-day.recipe.json"));
        Recipe golden = VistaJson.ParseRecipe(Read("mountains-golden.recipe.json"));
        Assert.AreEqual((TimeOfDay.Day, TimeOfDay.GoldenHour), (day.Time, golden.Time));
        CollectionAssert.AreEqual(new[] { SetPiece.SnowCappedMountains }, day.SetPieces.ToArray());
        foreach (Recipe recipe in new[] { day, golden })
        {
            Assert.AreEqual((Place.Zermatt, new DateOnly(2026, 7, 15)), (recipe.Location, recipe.Date), "place and date are pinned (M1.2)");
            Assert.IsNull(recipe.SolarTime);
        }
        Assert.AreEqual(45.0, VistaJson.ParseCamera(Read("sky.camera.json")).PitchDegrees);
        Assert.AreEqual(-30.0, VistaJson.ParseCamera(Read("down-slope.camera.json")).PitchDegrees);
        Assert.AreEqual(4200.0, VistaJson.ParseCamera(Read("aerial.camera.json")).Position.Y);
    }

    [TestMethod]
    public void EveryCamera_StandsAboveTheSeed42Terrain()
    {
        HeightField terrain = TerrainGenerator.Generate(VistaJson.ParseRecipe(Read("mountains-day.recipe.json")), 42);
        foreach (string file in new[] { "sky", "aerial", "default", "down-slope" })
        {
            CameraSpec camera = VistaJson.ParseCamera(Read(file + ".camera.json"));
            Assert.IsNull(DefaultCamera.GroundWarning(camera, terrain), file);
        }
    }

    [TestMethod]
    public void EveryCamera_StandsAboveTheIncisedSeed42Terrain()
    {
        // M1.11a's incision moves the ground by hundreds of metres, valleys and ridges both, before the GPU erosion runs (whose
        // own changes the self-test checks on the eroded world). The incision runs on the CPU, so CI can check the views over it.
        Recipe recipe = VistaJson.ParseRecipe(Read("mountains-day.recipe.json"));
        HeightField ground = TerrainGenerator.Generate(recipe, 42);
        HeightField incised = StreamPower.Prepare(ground, recipe, 42, ErosionSettings.For(TerrainProfile.For(recipe.SetPieces)));
        string[] cameras = [.. System.IO.Directory.GetFiles(Directory, "*.camera.json", SearchOption.AllDirectories)];
        Assert.AreEqual(8, cameras.Length, "the measurement, atmosphere and aerial views");
        foreach (string file in cameras)
        {
            CameraSpec camera = VistaJson.ParseCamera(File.ReadAllText(file));
            double clearance = camera.Position.Y - incised.HeightAt(camera.Position.X, camera.Position.Z);
            Assert.IsTrue(clearance > ClearanceMetres, $"{Path.GetFileName(file)} stands {clearance:0.0} m above the incised ground");
        }
    }

    /// <summary>
    /// How far above the incised ground a view must stand, so the GPU erosion's cut and fill (tens of metres on most of the ground;
    /// its loose material piles higher only on valley floors) does not bury it.
    /// </summary>
    private const double ClearanceMetres = 20.0;

    [TestMethod]
    public void TheAtmosphereCheckViews_AreValidAndAboveTheTerrain()
    {
        // vista-views/atmosphere (M1.3): the views render.ps1 renders the atmosphere checks from.
        string folder = Path.Combine(Directory, "atmosphere");
        string[] recipes = System.IO.Directory.GetFiles(folder, "*.recipe.json");
        Assert.AreEqual(2, recipes.Length);
        foreach (string file in recipes)
        {
            Recipe recipe = VistaJson.ParseRecipe(File.ReadAllText(file));
            Assert.AreEqual(Place.Zermatt, recipe.Location, file);
            Assert.IsNotNull(recipe.Date, "the date is pinned, so the sun stays put");
        }
        HeightField terrain = TerrainGenerator.Generate(VistaJson.ParseRecipe(File.ReadAllText(recipes[0])), 42);
        string[] cameras = System.IO.Directory.GetFiles(folder, "*.camera.json");
        CollectionAssert.AreEquivalent(new[] { "away-from-sun", "range", "toward-sun" },
            cameras.Select(f => Path.GetFileName(f).Replace(".camera.json", "", StringComparison.Ordinal)).ToArray());
        foreach (string file in cameras)
            Assert.IsNull(DefaultCamera.GroundWarning(VistaJson.ParseCamera(File.ReadAllText(file)), terrain), file);
    }

    [TestMethod]
    public void TheAerialPerspectiveView_LooksAlongTheRangeAboveTheTerrain()
    {
        // vista-views/aerial (M1.4): the layered ridges, rendered with the measurement views' recipes.
        string[] cameras = System.IO.Directory.GetFiles(Path.Combine(Directory, "aerial"), "*.camera.json");
        CollectionAssert.AreEquivalent(new[] { "layered-ridges" },
            cameras.Select(f => Path.GetFileName(f).Replace(".camera.json", "", StringComparison.Ordinal)).ToArray());
        CameraSpec camera = VistaJson.ParseCamera(File.ReadAllText(cameras[0]));
        Assert.AreEqual(270.0, camera.YawDegrees, "west, along the range and toward the evening sun");
        HeightField terrain = TerrainGenerator.Generate(VistaJson.ParseRecipe(Read("mountains-day.recipe.json")), 42);
        Assert.IsNull(DefaultCamera.GroundWarning(camera, terrain));
    }
}
