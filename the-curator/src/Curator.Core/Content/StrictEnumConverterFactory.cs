using System.Text.Json;
using System.Text.Json.Serialization;

namespace Curator.Core.Content;

/// <summary>
/// Reads enums only from their exact camelCase names — no other casing, no numbers — both as
/// values and as dictionary keys. Content is strict (BUILD_BRIEF §2).
/// </summary>
public sealed class StrictEnumConverterFactory : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return typeToConvert.IsEnum;
    }

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert))!;

    private sealed class Converter<T> : JsonConverter<T>
        where T : struct, Enum
    {
        private static readonly Dictionary<string, T> ByName = Enum.GetValues<T>().ToDictionary(Name, v => v, StringComparer.Ordinal);

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Expected one of: {string.Join(", ", ByName.Keys)}.");
            }

            return Parse(reader.GetString() ?? "");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStringValue(Name(value));
        }

        public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Parse(reader.GetString() ?? "");

        public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WritePropertyName(Name(value));
        }

        private static T Parse(string text) =>
            ByName.TryGetValue(text, out var value)
                ? value
                : throw new JsonException($"'{text}' is not one of: {string.Join(", ", ByName.Keys)}.");

        private static string Name(T value)
        {
            var name = value.ToString();
            return char.ToLowerInvariant(name[0]) + name[1..];
        }
    }
}
