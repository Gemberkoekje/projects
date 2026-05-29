using TheCuratool.Domain;

namespace TheCuratool.Application;

/// <summary>
/// The outcome of parsing a Blood on the Clocktower script JSON string.
/// Contains the parsed <see cref="Script"/>, informational diagnostics in <see cref="Info"/>,
/// non-fatal <see cref="Warnings"/>, and fatal <see cref="Errors"/>.
/// </summary>
/// <param name="Script">The parsed script. May be empty when parsing fails entirely.</param>
/// <param name="Info">Informational diagnostics that do not indicate issues (for example, setup UI guidance).</param>
/// <param name="Warnings">Non-fatal advisory messages (e.g. missing Minions or Outsiders).</param>
/// <param name="Errors">Fatal errors that prevented a usable script from being produced.</param>
public sealed record ScriptParseResult(
    Script Script,
    IReadOnlyList<string> Info,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors)
{
    /// <summary>Returns <see langword="true"/> when no errors were encountered during parsing.</summary>
    public bool IsSuccess => Errors.Count == 0;
}
