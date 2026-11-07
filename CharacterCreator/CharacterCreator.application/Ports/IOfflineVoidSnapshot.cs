using CharacterCreator.application.Void;

namespace CharacterCreator.application.Ports;

/// <summary>
/// The offline snapshot of VOID's skills that ships with a ruleset's content: what the first start
/// publishes, so the build screen is never empty before anyone has imported from a VOID (DESIGN.md §4.2).
/// </summary>
public interface IOfflineVoidSnapshot
{
    /// <summary>Loads the offline snapshot for a content ruleset.</summary>
    /// <param name="ruleset">The content ruleset, such as <c>2026-v07</c>.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The snapshot, or why there is none.</returns>
    Task<Result<VoidSnapshot>> LoadAsync(string ruleset, CancellationToken cancellationToken);
}
