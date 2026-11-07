using CharacterCreator.core.Rulesets;

namespace CharacterCreator.application.Ports;

/// <summary>Where published rulesets are kept (DESIGN.md §4.2). A ruleset is stored once and never changed.</summary>
public interface IRulesetStore
{
    /// <summary>The newest published ruleset: what anonymous visitors get.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The ruleset, or a message when none has been published yet.</returns>
    Task<Result<PublishedRuleset>> CurrentAsync(CancellationToken cancellationToken);

    /// <summary>A published ruleset by id, for a plan made on an older one.</summary>
    /// <param name="id">The ruleset's id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The ruleset, or a message when there is no such ruleset.</returns>
    Task<Result<PublishedRuleset>> GetAsync(string id, CancellationToken cancellationToken);

    /// <summary>The newest published rulesets, newest first.</summary>
    /// <param name="max">How many.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Their summaries.</returns>
    Task<IReadOnlyList<RulesetSummary>> ListAsync(int max, CancellationToken cancellationToken);

    /// <summary>Stores a newly published ruleset.</summary>
    /// <param name="ruleset">The ruleset.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>OK, or a message when a ruleset with that id already exists.</returns>
    Task<Result> AddAsync(PublishedRuleset ruleset, CancellationToken cancellationToken);
}
