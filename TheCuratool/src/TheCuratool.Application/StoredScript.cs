using TheCuratool.Domain;

namespace TheCuratool.Application;

/// <summary>
/// A persisted script record combining the parsed <see cref="Script"/> domain object
/// with its storage identifier and original raw JSON.
/// </summary>
/// <param name="Id">The unique identifier assigned on first persistence.</param>
/// <param name="Script">The parsed domain model of the script.</param>
/// <param name="RawJson">The original JSON string as uploaded.</param>
public sealed record StoredScript(
    Guid Id,
    Script Script,
    string RawJson);
