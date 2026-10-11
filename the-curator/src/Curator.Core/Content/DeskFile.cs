namespace Curator.Core.Content;

/// <summary>
/// game/content/desk.json: the in-world lines the desk shows that belong to no patron or book —
/// what Identify turned up, notes in the open book, and the gentle lines for refused commands (Q24).
/// </summary>
public sealed record DeskFile
{
    /// <summary>The <see cref="Refusals"/> key for reasons without a line of their own.</summary>
    public const string DefaultRefusal = "default";

    /// <summary>Shown when a book resists Identify outright.</summary>
    public required string IdentifyFailed { get; init; }

    /// <summary>Shown when Identify lands on a page already read.</summary>
    public required string PageRepeated { get; init; }

    /// <summary>The open book's note for a page that won't come into focus.</summary>
    public required string PageResisted { get; init; }

    /// <summary>The open book's note for a torn-out page.</summary>
    public required string PageRemoved { get; init; }

    /// <summary>Asked before closing while patrons are still to come.</summary>
    public required string CloseEarly { get; init; }

    /// <summary>
    /// Lines for refused commands, keyed by the camelCase name of the refusal reason
    /// (<c>patronAtCounter</c>), plus <c>default</c> for any reason without its own line.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Refusals { get; init; }

    public required bool Placeholder { get; init; }
}
