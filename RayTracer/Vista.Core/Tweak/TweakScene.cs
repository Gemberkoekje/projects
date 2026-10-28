using Vista.Astronomy;
using Vista.Cli;
using Vista.Recipes;
using Vista.Scene;
using Vista.Sliders;

namespace Vista.Tweak;

/// <summary>What the window shows of a preview (vista-plan §10 M1.16): the beauty, the denoised beauty or one debug image.</summary>
public enum TweakView
{
    /// <summary>Not set: the beauty.</summary>
    None,

    /// <summary>The beauty, as the PNG.</summary>
    Beauty,

    /// <summary>The beauty denoised by Intel Open Image Denoise on the GPU (<c>--denoise --denoise-device cuda</c>).</summary>
    Denoised,

    /// <summary>The first hit's albedo (<c>--debug albedo</c>).</summary>
    Albedo,

    /// <summary>The first hit's normal.</summary>
    Normal,

    /// <summary>The first hit's distance.</summary>
    Depth,

    /// <summary>The ground's materials by rule.</summary>
    Material,

    /// <summary>The materials' data tiers.</summary>
    Tier,

    /// <summary>Each pixel's variance.</summary>
    Variance,

    /// <summary>The auto camera's detail budget over the beauty.</summary>
    Detail,
}

/// <summary>How the window's preview renders (vista-plan §7.4): the final's settings at fewer pixels, 16:9.</summary>
public sealed record TweakPreview
{
    /// <summary>The preview widths the window offers; the first is the CLI's default.</summary>
    public static IReadOnlyList<int> Widths { get; } = [PreviewCommandLine.DefaultWidth, 480, 640, 960];

    /// <summary>
    /// The window's preview width: twice the CLI's, since the window shows it two or three times larger and a 320 px preview read as a
    /// smear there; at 64 spp it still traces in about a second.
    /// </summary>
    public const int DefaultWidth = 640;

    /// <summary>The samples a pixel the window offers for the preview and the final.</summary>
    public static IReadOnlyList<int> Samples { get; } = [16, 64, 256, 1024];

    /// <summary>The preview's width, px; its height is the width at 16:9, as the CLI's.</summary>
    public int Width { get; init; } = DefaultWidth;

    /// <summary>Samples per pixel: the final's quality, so the preview shows its noise as well as its light.</summary>
    public int SamplesPerPixel { get; init; } = 64;

    /// <summary>What the window shows of it.</summary>
    public TweakView View { get; init; } = TweakView.Beauty;
}

/// <summary>How the window's final renders (vista-plan §10 M1.16): 4K by default, with its EXR, PNG and sidecar.</summary>
public sealed record TweakFinal
{
    /// <summary>The final's width, px (M1's single target is 3840×2160); its height is the width at 16:9.</summary>
    public int Width { get; init; } = 3840;

    /// <summary>Samples per pixel.</summary>
    public int SamplesPerPixel { get; init; } = 64;

    /// <summary>Also write the beauty denoised (on the GPU), beside the raw one.</summary>
    public bool Denoise { get; init; }

    /// <summary>The estimate's noise target in steps of the 8-bit PNG (<c>--noise</c>); null for none.</summary>
    public double? NoiseTarget { get; init; }
}

/// <summary>
/// What the window edits (vista-plan §3.1, §10 M1.16): the recipe's set pieces, the seed, the camera, the slider pins, and how the
/// preview and the final render. The recipe, the camera and the pins are the files the CLI reads (<see cref="TweakFiles"/>); the seed
/// and the sizes go in an options file a watch reads. Each control makes a new <see cref="TweakScene"/>.
///
/// <para>Changing the set pieces or the seed starts a new scene (§3.4): the world is another one, so the camera is chosen again (the auto
/// camera for the mountain set pieces, else the default camera) and nothing is promised to stay where it was. Everything else keeps the
/// world. The recipe holds no slider pins (a recipe from before the slider file gives its own to <see cref="Pins"/> when loaded), so
/// the files the window saves are a recipe, a camera and a slider file the CLI takes together.</para>
/// </summary>
public sealed record TweakScene
{
    /// <summary>The recipe: its set pieces and name; never its own pins.</summary>
    public required Recipe Recipe { get; init; }

    /// <summary>The world's seed.</summary>
    public required ulong Seed { get; init; }

    /// <summary>The camera; null for a new scene, until its first run chooses one.</summary>
    public CameraSpec? Camera { get; init; }

    /// <summary>The slider pins: every slider the user moved, and its value.</summary>
    public SliderPins Pins { get; init; } = SliderPins.None;

    /// <summary>How the preview renders.</summary>
    public TweakPreview Preview { get; init; } = new();

    /// <summary>How the final renders.</summary>
    public TweakFinal Final { get; init; } = new();

    /// <summary>True for a new scene: no camera yet, so the next run chooses one.</summary>
    public bool IsNew => Camera is null;

    /// <summary>
    /// A scene from what the files hold: a recipe that still pins sliders itself gives them to the pins (with a slider file as well, the
    /// pins would have two homes, which the CLI refuses too).
    /// </summary>
    public static TweakScene From(Recipe recipe, ulong seed, CameraSpec? camera = null, SliderPins? pins = null)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (recipe.HasPins && pins is not null)
            throw new InvalidDataException("The recipe pins sliders itself (its time, season, place or altitude) and a slider file pins them too: keep one.");
        return new TweakScene
        {
            Recipe = WithoutPins(recipe),
            Seed = seed,
            Camera = camera,
            Pins = pins ?? SliderPins.FromRecipe(recipe),
        };
    }

    /// <summary><paramref name="recipe"/> with no slider pins of its own: its set pieces and name.</summary>
    public static Recipe WithoutPins(Recipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return recipe with
        {
            Schema = Recipe.CurrentSchema,
            Time = null,
            Season = null,
            Location = Place.None,
            Latitude = null,
            Date = null,
            SolarTime = null,
            SunElevation = null,
            Altitude = null,
        };
    }

    /// <summary>This scene with <paramref name="piece"/> in the recipe (a conflicting piece swapped out): a new scene, unless it was there.</summary>
    public TweakScene With(SetPiece piece) =>
        Recipe.SetPieces.Contains(piece) ? this : this with { Recipe = Recipe.With(piece), Camera = null };

    /// <summary>This scene without <paramref name="piece"/>: a new scene, unless it was not there.</summary>
    public TweakScene Without(SetPiece piece) =>
        Recipe.SetPieces.Contains(piece) ? this with { Recipe = Recipe.Without(piece), Camera = null } : this;

    /// <summary>This scene on the world of <paramref name="seed"/>: a new scene, unless it is the same seed.</summary>
    public TweakScene WithSeed(ulong seed) => seed == Seed ? this : this with { Seed = seed, Camera = null };

    /// <summary>A seed for "new seed": a different one, drawn from <paramref name="random"/>, small enough to type.</summary>
    public ulong NextSeed(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        ulong seed;
        do
            seed = (ulong)random.NextInt64(1, 1_000_000);
        while (seed == Seed);
        return seed;
    }

    /// <summary>This scene with <paramref name="slider"/> reset: auto again (vista-plan §3.4).</summary>
    public TweakScene Reset(Slider slider) => this with { Pins = Pins.Reset(slider) };

    /// <summary>The preview's height: its width at 16:9, rounded as the CLI rounds it.</summary>
    public static int HeightOf(int width) => (int)Math.Round(width * 9.0 / 16.0, MidpointRounding.AwayFromZero);
}
