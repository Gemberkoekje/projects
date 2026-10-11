using System.Text.Json;
using System.Text.Json.Serialization;

namespace Curator.Core.Game;

/// <summary>Reads and writes saved games as JSON.</summary>
public static class SaveSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Writes a save.</summary>
    /// <param name="save">The save.</param>
    /// <returns>The JSON.</returns>
    public static string Serialize(SaveData save) => JsonSerializer.Serialize(save, Options);

    /// <summary>Reads a save.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The save.</returns>
    /// <exception cref="JsonException">The JSON isn't a save.</exception>
    public static SaveData Deserialize(string json) =>
        JsonSerializer.Deserialize<SaveData>(json, Options) ?? throw new JsonException("The save is empty.");
}
