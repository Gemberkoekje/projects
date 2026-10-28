using System.Globalization;
using Vista.Recipes;

namespace Vista.Cli;

/// <summary>
/// The one axis a contact sheet varies along (vista-plan §7.4), parsed from <c>--vary key=v1,v2,…</c>:
/// <list type="bullet">
///   <item><c>seed=1,2,3,4</c> — the world seed;</item>
///   <item><c>recipe=a.json,b.json</c> / <c>camera=a.json,b.json</c> — whole files (<c>camera=default</c> is
///   the default camera);</item>
///   <item><c>spp=16,64,256</c> / <c>bounces=1,3,6</c> / <c>atmosphere=luts,reference</c> / <c>lightSampling=mis,sunOnly</c> —
///   render settings; <c>erosion=off,on</c> — the world un-eroded and eroded (M1.7); <c>mesh=uniform,view</c>, <c>meshError=0.5,1,2</c>,
///   <c>detail=off,on</c>, <c>farField=off,on</c> — how the terrain is meshed (M1.9); <c>meter=average,incident</c>,
///   <c>display=aces,agx</c> — how the PNG is exposed and display-encoded (M1.10a), <c>look=base,punchy</c> and
///   <c>compensation=0,0.5,1</c> — AgX's look and the meter's own stops (M1.11b; one render serves every cell), <c>dither=off,on</c> —
///   the 8-bit PNG's dither (M1.11c; one render too); <c>materials=off,on</c>, <c>diffuse=lambert,orenNayar</c> — the ground's
///   materials and how they scatter (M1.12); <c>quadrature=engine,bins</c> — the hero wavelengths and their weights (M1.12a);
///   <c>forest=off,on</c>, <c>canopy=rough,kernels</c> — the forest and how its canopy scatters (M1.12b); <c>snow=off,on</c> — the
///   season's snow (M1.13);</item>
///   <item><c>camera.&lt;field&gt;=…</c> — one camera field, e.g. <c>camera.yawDegrees=0,90,180,270</c>;</item>
///   <item><c>sliders=a.json,b.json</c> — whole slider files; a slider-file field — one slider, pinned per cell (M1.11), e.g.
///   <c>time=dawn,day,goldenHour,dusk</c>, <c>sunElevation=2,4,6</c>, <c>date=2026-01-15,2026-07-15</c> or <c>visibility=20,80</c>
///   (<see cref="Sliders.SliderPins.Fields"/>);</item>
///   <item>anything else — one recipe field by its dotted path, e.g.
///   <c>setPieces=[snowCappedMountains],[snowCappedMountains,lake]</c>.</item>
/// </list>
/// Values are split at top-level commas and written loosely (<see cref="JsonOverrides.LooseValue"/>); field
/// overrides still pass the strict parser, so a misspelt field fails before anything renders.
/// </summary>
public sealed class SheetAxis
{
    /// <summary>Most values one axis takes (one sheet's worth of tiles).</summary>
    public const int MaxValues = Output.ContactSheet.MaxTiles;

    private readonly Func<PreviewRequest, string, PreviewRequest> _apply;

    private SheetAxis(string key, IReadOnlyList<string> values, Func<PreviewRequest, string, PreviewRequest> apply)
    {
        Key = key;
        Values = values;
        _apply = apply;
    }

    /// <summary>What varies: <c>seed</c>, <c>recipe</c>, <c>sliders</c>, <c>camera</c>, <c>spp</c>, <c>bounces</c>, <c>atmosphere</c>, <c>lightSampling</c>, <c>erosion</c>, <c>mesh</c>, <c>meshError</c>, <c>detail</c>, <c>farField</c>, <c>meter</c>, <c>display</c>, <c>look</c>, <c>compensation</c>, <c>dither</c>, <c>camera.&lt;field&gt;</c>, a slider-file field or a recipe field path.</summary>
    public string Key { get; }

    /// <summary>The values as written, one per cell.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>True when the axis chooses the recipe file, so <c>--recipe</c> is not needed.</summary>
    public bool SuppliesRecipe => Key == "recipe";

    /// <summary>True when the axis chooses the seed, so <c>--seed</c> is not needed.</summary>
    public bool SuppliesSeed => Key == "seed";

    /// <summary>True when the axis chooses the camera file, so <c>--camera</c> must not be given.</summary>
    public bool SuppliesCamera => Key == "camera";

    /// <summary>True when the axis chooses the slider file, so <c>--sliders</c> must not be given.</summary>
    public bool SuppliesSliders => Key == "sliders";

    /// <summary>The label of cell <paramref name="index"/>: <c>key=value</c>.</summary>
    public string Label(int index) => $"{Key}={Values[index]}";

    /// <summary>The request for cell <paramref name="index"/>: <paramref name="request"/> with this axis set to that value.</summary>
    public PreviewRequest Apply(PreviewRequest request, int index)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _apply(request, Values[index]);
    }

    /// <summary>Parses <c>key=v1,v2,…</c>; returns false with the problem in <paramref name="error"/>.</summary>
    public static bool TryParse(string text, out SheetAxis? axis, out string? error)
    {
        ArgumentNullException.ThrowIfNull(text);
        axis = null;
        error = null;
        int eq = text.IndexOf('=', StringComparison.Ordinal);
        if (eq <= 0 || eq == text.Length - 1)
        {
            error = $"--vary must look like key=value1,value2,…, got '{text}'.";
            return false;
        }
        string key = text[..eq].Trim();
        IReadOnlyList<string> values;
        try
        {
            values = JsonOverrides.SplitList(text[(eq + 1)..]);
        }
        catch (FormatException e)
        {
            error = $"--vary {key}: {e.Message}";
            return false;
        }
        if (values.Count > MaxValues)
        {
            error = $"--vary {key} has {values.Count} values; a sheet takes at most {MaxValues}.";
            return false;
        }

        if (key.Split('.').Any(segment => segment.Length == 0))
        {
            error = $"--vary: malformed field path '{key}'.";
            return false;
        }

        Func<PreviewRequest, string, PreviewRequest> apply;
        try
        {
            apply = key switch
            {
                "seed" => Checked(values, v => ulong.Parse(v, NumberStyles.None, CultureInfo.InvariantCulture),
                    (r, v) => r with { Seed = ulong.Parse(v, NumberStyles.None, CultureInfo.InvariantCulture) }),
                "spp" => Checked(values, v => Int(v, 1, 1 << 20), (r, v) => r with { SamplesPerPixel = Int(v, 1, 1 << 20) }),
                "bounces" => Checked(values, v => Int(v, 0, 64), (r, v) => r with { MaxBounces = Int(v, 0, 64) }),
                "atmosphere" => Checked(values, Atmosphere, (r, v) => r with { Atmosphere = Atmosphere(v) }),
                "lightSampling" => Checked(values, Lights, (r, v) => r with { LightSampling = Lights(v) }),
                "erosion" => Checked(values, OnOff, (r, v) => r with { Erosion = OnOff(v) }),
                "mesh" => Checked(values, MeshKindOf, (r, v) => r with { Mesh = r.Mesh with { Kind = MeshKindOf(v) } }),
                "meshError" => Checked(values, MeshError, (r, v) => r with { Mesh = r.Mesh with { ErrorPixels = MeshError(v) } }),
                "detail" => Checked(values, OnOff, (r, v) => r with { Mesh = r.Mesh with { Detail = OnOff(v) } }),
                "farField" => Checked(values, OnOff, (r, v) => r with { Mesh = r.Mesh with { FarField = OnOff(v) } }),
                "materials" => Checked(values, OnOff, (r, v) => r with { Mesh = r.Mesh with { Materials = OnOff(v) } }),
                "diffuse" => Checked(values, DiffuseOf, (r, v) => r with { Diffuse = DiffuseOf(v) }),
                "quadrature" => Checked(values, QuadratureOf, (r, v) => r with { Quadrature = QuadratureOf(v) }),
                "forest" => Checked(values, OnOff, (r, v) => r with { Forest = OnOff(v) }),
                "canopy" => Checked(values, CanopyOf, (r, v) => r with { Canopy = CanopyOf(v) }),
                "snow" => Checked(values, OnOff, (r, v) => r with { Snow = OnOff(v) }),
                "meter" => Checked(values, Meter, (r, v) => r with { Meter = Meter(v) }),
                "display" => Checked(values, Display, (r, v) => r with { Display = Display(v) }),
                "look" => Checked(values, Look, (r, v) => r with { Look = Look(v) }),
                "compensation" => Checked(values, Compensation, (r, v) => r with { MeterCompensationEv = Compensation(v) }),
                "dither" => Checked(values, Dither, (r, v) => r with { Dither = Dither(v) }),
                "whiteBalance" => Checked(values, WhiteBalance, (r, v) => r with { Grade = r.Grade with { WhiteBalanceKelvin = WhiteBalance(v) } }),
                "skyMargin" => Checked(values, SkyMargin, (r, v) => r with { Grade = r.Grade with { SkyMargin = SkyMargin(v) } }),
                "adaptation" => Checked(values, Adaptation, (r, v) => r with { Adaptation = Adaptation(v) }),
                "recipe" => (r, v) => r with { RecipePath = v },
                "camera" => (r, v) => r with { CameraPath = v == "default" ? null : v },
                "sliders" => (r, v) => r with { SlidersPath = v },
                _ when key.StartsWith("camera.", StringComparison.Ordinal) =>
                    FieldOverride(values, key["camera.".Length..], Target.Camera),
                _ when Sliders.SliderPins.Fields.ContainsKey(key) => FieldOverride(values, key, Target.Sliders),
                _ => FieldOverride(values, key, Target.Recipe),
            };
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
        {
            error = $"--vary {key}: {e.Message}";
            return false;
        }
        axis = new SheetAxis(key, values, apply);
        return true;
    }

    private static Func<PreviewRequest, string, PreviewRequest> Checked<T>(
        IReadOnlyList<string> values, Func<string, T> parse, Func<PreviewRequest, string, PreviewRequest> apply)
    {
        foreach (string v in values)
            _ = parse(v);
        return apply;
    }

    private static Rendering.AtmosphereMode Atmosphere(string text) =>
        PreviewCommandLine.TryParseAtmosphere(text, out Rendering.AtmosphereMode mode) ? mode : throw new ArgumentException($"'{text}' is not luts or reference.");

    private static Rendering.LightSampling Lights(string text) =>
        PreviewCommandLine.TryParseLightSampling(text, out Rendering.LightSampling sampling) ? sampling : throw new ArgumentException($"'{text}' is not mis or sunOnly.");

    private static Output.Metering Meter(string text) =>
        PreviewCommandLine.TryParseMeter(text, out Output.Metering meter) ? meter : throw new ArgumentException($"'{text}' is not incident or average.");

    private static Output.DisplayCurve Display(string text) =>
        PreviewCommandLine.TryParseDisplay(text, out Output.DisplayCurve curve) ? curve : throw new ArgumentException($"'{text}' is not agx or aces.");

    private static double WhiteBalance(string text) =>
        PreviewCommandLine.TryParseWhiteBalance(text, out double kelvin) ? kelvin : throw new ArgumentException($"'{text}' is not none or a Kelvin value.");

    private static double SkyMargin(string text) =>
        PreviewCommandLine.TryParseSkyMargin(text, out double margin) ? margin : throw new ArgumentException($"'{text}' is not a sky margin.");

    private static double Adaptation(string text) =>
        PreviewCommandLine.TryParseAdaptation(text, out double adaptation) ? adaptation : throw new ArgumentException($"'{text}' is not an adaptation from 0 to 1.");

    private static Output.DisplayLook Look(string text) =>
        PreviewCommandLine.TryParseLook(text, out Output.DisplayLook look) ? look : throw new ArgumentException($"'{text}' is not base or punchy.");

    private static double Compensation(string text) =>
        PreviewCommandLine.TryParseCompensation(text, out double ev) ? ev
            : throw new ArgumentException($"'{text}' is not a number of stops within ±{PreviewCommandLine.MaxCompensationEv}.");

    private static Output.PngDither Dither(string text) =>
        PreviewCommandLine.TryParseDither(text, out Output.PngDither dither) ? dither : throw new ArgumentException($"'{text}' is not on or off.");

    private static bool OnOff(string text) => text switch
    {
        "on" => true,
        "off" => false,
        _ => throw new ArgumentException($"'{text}' is not on or off."),
    };

    private static Rendering.DiffuseModel DiffuseOf(string text) =>
        PreviewCommandLine.TryParseDiffuse(text, out Rendering.DiffuseModel diffuse) ? diffuse : throw new ArgumentException($"'{text}' is not orenNayar or lambert.");

    private static Rendering.CanopyModel CanopyOf(string text) =>
        PreviewCommandLine.TryParseCanopy(text, out Rendering.CanopyModel canopy) ? canopy : throw new ArgumentException($"'{text}' is not kernels or rough.");

    private static Rendering.Quadrature QuadratureOf(string text) =>
        PreviewCommandLine.TryParseQuadrature(text, out Rendering.Quadrature quadrature) ? quadrature : throw new ArgumentException($"'{text}' is not blend, bins, redTail or engine.");

    private static World.MeshKind MeshKindOf(string text) =>
        PreviewCommandLine.TryParseMeshKind(text, out World.MeshKind kind) ? kind : throw new ArgumentException($"'{text}' is not view or uniform.");

    private static double MeshError(string text) =>
        PreviewCommandLine.TryParseMeshError(text, out double pixels) ? pixels : throw new ArgumentException($"'{text}' is not an error between 0.01 and 1000 pixels.");

    private static int Int(string text, int min, int max)
    {
        int value = int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        return value >= min && value <= max ? value : throw new ArgumentException($"'{text}' is outside [{min}, {max}].");
    }

    /// <summary>Which file a field override goes to.</summary>
    private enum Target
    {
        None,
        Recipe,
        Sliders,
        Camera,
    }

    private static Func<PreviewRequest, string, PreviewRequest> FieldOverride(IReadOnlyList<string> values, string path, Target target)
    {
        var json = values.Distinct().ToDictionary(v => v, JsonOverrides.LooseValue, StringComparer.Ordinal);
        return (r, v) =>
        {
            IReadOnlyDictionary<string, string> current = target switch
            {
                Target.Camera => r.CameraOverrides,
                Target.Sliders => r.SliderOverrides,
                _ => r.RecipeOverrides,
            };
            var overrides = new Dictionary<string, string>(current, StringComparer.Ordinal) { [path] = json[v] };
            return target switch
            {
                Target.Camera => r with { CameraOverrides = overrides },
                Target.Sliders => r with { SliderOverrides = overrides },
                _ => r with { RecipeOverrides = overrides },
            };
        };
    }
}
