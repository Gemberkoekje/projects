using System.Text.Json;
using System.Text.Json.Serialization;

namespace CharacterCreator.core.Skills;

/// <summary>Writes a <see cref="SkillKey"/> as its plain text, so content, rulesets and plans read naturally as JSON.</summary>
/// <remarks>Stateless and thread-safe.</remarks>
public sealed class SkillKeyJsonConverter : JsonConverter<SkillKey>
{
    /// <inheritdoc />
    public override SkillKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        return string.IsNullOrWhiteSpace(text) ? SkillKey.Empty : new SkillKey(text);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, SkillKey value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.ToString());
    }

    /// <inheritdoc />
    public override SkillKey ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => Read(ref reader, typeToConvert, options);

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, SkillKey value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WritePropertyName(value.ToString());
    }
}
