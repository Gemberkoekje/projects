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
///   render settings; <c>erosion=off,on</c> — the world un-eroded and eroded (M1.7);</item>
///   <item><c>camera.&lt;field&gt;=…</c> — one camera field, e.g. <c>camera.yawDegrees=0,90,180,270</c>;</item>
///   <item>anything else — one recipe field by its dotted path, e.g. <c>time=dawn,day,goldenHour,dusk</c> or
///   <c>setPieces=[snowCappedMountains],[snowCappedMountains,lake]</c>. The linked sliders join the recipe
///   later in M1, and with them this axis.</item>
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

    /// <summary>What varies: <c>seed</c>, <c>recipe</c>, <c>camera</c>, <c>spp</c>, <c>bounces</c>, <c>atmosphere</c>, <c>lightSampling</c>, <c>erosion</c>, <c>camera.&lt;field&gt;</c> or a recipe field path.</summary>
    public string Key { get; }

    /// <summary>The values as written, one per cell.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>True when the axis chooses the recipe file, so <c>--recipe</c> is not needed.</summary>
    public bool SuppliesRecipe => Key == "recipe";

    /// <summary>True when the axis chooses the seed, so <c>--seed</c> is not needed.</summary>
    public bool SuppliesSeed => Key == "seed";

    /// <summary>True when the axis chooses the camera file, so <c>--camera</c> must not be given.</summary>
    public bool SuppliesCamera => Key == "camera";

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
                "recipe" => (r, v) => r with { RecipePath = v },
                "camera" => (r, v) => r with { CameraPath = v == "default" ? null : v },
                _ when key.StartsWith("camera.", StringComparison.Ordinal) =>
                    FieldOverride(values, key["camera.".Length..], camera: true),
                _ => FieldOverride(values, key, camera: false),
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

    private static bool OnOff(string text) => text switch
    {
        "on" => true,
        "off" => false,
        _ => throw new ArgumentException($"'{text}' is not on or off."),
    };

    private static int Int(string text, int min, int max)
    {
        int value = int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        return value >= min && value <= max ? value : throw new ArgumentException($"'{text}' is outside [{min}, {max}].");
    }

    private static Func<PreviewRequest, string, PreviewRequest> FieldOverride(IReadOnlyList<string> values, string path, bool camera)
    {
        var json = values.Distinct().ToDictionary(v => v, JsonOverrides.LooseValue, StringComparer.Ordinal);
        return (r, v) =>
        {
            var overrides = new Dictionary<string, string>(camera ? r.CameraOverrides : r.RecipeOverrides, StringComparer.Ordinal)
            {
                [path] = json[v],
            };
            return camera ? r with { CameraOverrides = overrides } : r with { RecipeOverrides = overrides };
        };
    }
}
