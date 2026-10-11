using System.Text.Json;
using System.Text.Json.Serialization;

namespace Curator.Core.Content;

/// <summary>The strict JSON settings for content: camelCase; unknown or repeated properties, nulls and loose enum names are errors.</summary>
public static class ContentJson
{
    /// <summary>The shared options; never modify.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        AllowDuplicateProperties = false,
        Converters = { new StrictEnumConverterFactory() },
    };
}
