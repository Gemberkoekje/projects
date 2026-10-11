using System.Text.Json;
using System.Text.Json.Serialization;

namespace Curator.Core.Content;

/// <summary>Reads a greeting written either as a plain string or as a list of variants.</summary>
public sealed class GreetingListConverter : JsonConverter<IReadOnlyList<GreetingVariant>>
{
    /// <inheritdoc />
    public override IReadOnlyList<GreetingVariant> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return [new GreetingVariant { Text = reader.GetString() ?? "" }];
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var variants = JsonSerializer.Deserialize<List<GreetingVariant>>(ref reader, options);
            return variants ?? throw new JsonException("A greeting list can't be null.");
        }

        throw new JsonException("A greeting is a string or a list of { when, text } variants.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, IReadOnlyList<GreetingVariant> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Count == 1 && value[0].When.IsAlways)
        {
            writer.WriteStringValue(value[0].Text);
            return;
        }

        JsonSerializer.Serialize(writer, value.ToList(), options);
    }
}
