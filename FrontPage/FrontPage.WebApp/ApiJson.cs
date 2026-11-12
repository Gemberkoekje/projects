using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrontPage.WebApp;

/// <summary>How <c>/api/cluster</c> writes JSON: camelCase, enums as camelCase strings.</summary>
internal static class ApiJson
{
    /// <summary>The serializer options, created once.</summary>
    public static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
