using System.Text.Json;
using System.Text.Json.Serialization;

namespace CharacterCreator.core.Rulesets;

/// <summary>
/// How rulesets, content and plans are written as JSON between the server and the build screen:
/// camelCase names, enums by name. One place, so both sides read what the other writes.
/// </summary>
/// <remarks>Thread-safe: the options are frozen on first use by the serializer.</remarks>
public static class RulesetJson
{
    /// <summary>The options.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    /// <summary>Applies the same settings to options someone else owns, such as ASP.NET Core's.</summary>
    /// <param name="options">The options to set up.</param>
    public static void Apply(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.PropertyNameCaseInsensitive = true;
        options.Converters.Add(new JsonStringEnumConverter());
    }

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Apply(options);
        return options;
    }
}
