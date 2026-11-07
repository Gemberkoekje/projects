using CharacterCreator.application.Void;

namespace CharacterCreator.application.Ports;

/// <summary>
/// A VOID, called on a signed-in person's behalf (DESIGN.md §6): <c>void-test</c> for the demo, live
/// in production.
/// </summary>
public interface IVoidClient
{
    /// <summary>Whether a VOID address is configured (<c>Void:BaseUrl</c>); without one, nothing can be imported.</summary>
    bool IsConfigured { get; }

    /// <summary>The VOID address, for the import report and the published ruleset.</summary>
    string Source { get; }

    /// <summary>Reads every skill, with its prerequisite groups.</summary>
    /// <param name="accessToken">The signed-in person's ERU access token.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>VOID's skills, or why they could not be read (no address, refused, unreachable, an answer that did not parse).</returns>
    Task<Result<VoidSnapshot>> GetSkillsAsync(string accessToken, CancellationToken cancellationToken);
}
