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
}
