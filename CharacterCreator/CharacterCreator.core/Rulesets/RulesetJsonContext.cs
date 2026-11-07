using System.Text.Json;
using System.Text.Json.Serialization;
using CharacterCreator.core.Plans;

namespace CharacterCreator.core.Rulesets;

/// <summary>
/// Compile-time JSON metadata for what travels between the server and the build screen: the
/// published ruleset and plan files. The build screen is trimmed when published, and reflection-based
/// serialization may lose the members it needs; this context needs no reflection.
/// </summary>
/// <remarks>Thread-safe. Its settings match <see cref="RulesetJson"/>: camelCase, enums by name.</remarks>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(PublishedRuleset))]
[JsonSerializable(typeof(PlanFile))]
[JsonSerializable(typeof(List<PlanFile>))]
public sealed partial class RulesetJsonContext : JsonSerializerContext
{
}
