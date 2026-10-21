using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vista.Scene;
using Vista.Sliders;

namespace Vista.Recipes;

/// <summary>
/// The one JSON dialect for every Vista file — recipes, slider files, cameras and sidecars (vista-plan §7.4): camelCase
/// names, enums as camelCase strings, indented output, and <b>unknown properties rejected</b> so a typo in a
/// hand- or LLM-edited file fails loudly instead of being silently ignored.
/// </summary>
public static class VistaJson
{
    /// <summary>Serializer options shared by every Vista file.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
            NewLine = "\n",
            // Files on disk, never embedded in HTML: keep "+" and non-ASCII readable instead of \u-escaped.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    /// <summary>Serializes <paramref name="value"/> in the Vista dialect, ending with a newline.</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options) + "\n";

    /// <summary>Deserializes <paramref name="json"/>; throws <see cref="JsonException"/> on malformed input or a null document.</summary>
    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException($"Expected a {typeof(T).Name}, got null.");

    /// <summary>Parses and validates a recipe; throws <see cref="InvalidDataException"/> listing every problem.</summary>
    public static Recipe ParseRecipe(string json)
    {
        Recipe recipe = Deserialize<Recipe>(json);
        IReadOnlyList<string> errors = recipe.Validate();
        if (errors.Count > 0)
            throw new InvalidDataException("Invalid recipe: " + string.Join(" ", errors));
        return recipe;
    }

    /// <summary>Parses and validates a slider file; throws <see cref="InvalidDataException"/> listing every problem.</summary>
    public static SliderPins ParseSliders(string json)
    {
        SliderPins pins = Deserialize<SliderPins>(json);
        IReadOnlyList<string> errors = pins.Validate();
        if (errors.Count > 0)
            throw new InvalidDataException("Invalid slider file: " + string.Join(" ", errors));
        return pins;
    }

    /// <summary>Parses and validates a camera; throws <see cref="InvalidDataException"/> listing every problem.</summary>
    public static CameraSpec ParseCamera(string json)
    {
        CameraSpec camera = Deserialize<CameraSpec>(json);
        IReadOnlyList<string> errors = camera.Validate();
        if (errors.Count > 0)
            throw new InvalidDataException("Invalid camera: " + string.Join(" ", errors));
        return camera;
    }
}
