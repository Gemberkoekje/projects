using CharacterCreator.core.Content;

namespace CharacterCreator.application.Ports;

/// <summary>Where a ruleset's content comes from (DESIGN.md §4.1): the files in the repository until v4.</summary>
public interface IContentSource
{
    /// <summary>The rulesets there is content for, such as <c>2026-v07</c>.</summary>
    IReadOnlyList<string> Rulesets { get; }

    /// <summary>Loads one ruleset's content.</summary>
    /// <param name="ruleset">The ruleset, such as <c>2026-v07</c>.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The content, or why it could not be loaded.</returns>
    Task<Result<ContentBook>> LoadAsync(string ruleset, CancellationToken cancellationToken);
}
