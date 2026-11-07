using CharacterCreator.application.Ports;
using CharacterCreator.core.Rulesets;
using Qowaiv.Validation.Abstractions;

namespace CharacterCreator.test.TestHelpers;

/// <summary>Published rulesets in memory, with Marten's rules: newest first, an id stored once.</summary>
/// <remarks>Thread-safe: the HTTP specs share one across requests.</remarks>
internal sealed class InMemoryRulesetStore : IRulesetStore
{
    private readonly List<PublishedRuleset> rulesets = [];
    private readonly Lock gate = new ();

    /// <summary>Everything stored, in the order it was added.</summary>
    public IReadOnlyList<PublishedRuleset> All
    {
        get
        {
            lock (gate)
            {
                return [.. rulesets];
            }
        }
    }

    /// <inheritdoc />
    public Task<Result<PublishedRuleset>> CurrentAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var current = rulesets.OrderByDescending(ruleset => ruleset.PublishedAt).FirstOrDefault();
            return Task.FromResult(current is null
                ? Result.WithMessages<PublishedRuleset>(ValidationMessage.Error("No ruleset has been published yet."))
                : Result.For(current));
        }
    }

    /// <inheritdoc />
    public Task<Result<PublishedRuleset>> GetAsync(string id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var ruleset = rulesets.FirstOrDefault(ruleset => ruleset.Id == id);
            return Task.FromResult(ruleset is null
                ? Result.WithMessages<PublishedRuleset>(ValidationMessage.Error($"There is no ruleset '{id}'."))
                : Result.For(ruleset));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RulesetSummary>> ListAsync(int max, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            IReadOnlyList<RulesetSummary> summaries = [.. rulesets.OrderByDescending(ruleset => ruleset.PublishedAt).Take(max)
                .Select(ruleset => new RulesetSummary
                {
                    Id = ruleset.Id,
                    PublishedAt = ruleset.PublishedAt,
                    PublishedBy = ruleset.PublishedBy,
                    Source = ruleset.Source,
                    Provisional = ruleset.Provisional,
                })];
            return Task.FromResult(summaries);
        }
    }

    /// <inheritdoc />
    public Task<Result> AddAsync(PublishedRuleset ruleset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        lock (gate)
        {
            if (rulesets.Exists(stored => stored.Id == ruleset.Id))
            {
                return Task.FromResult(Result.WithMessages(ValidationMessage.Error($"A ruleset '{ruleset.Id}' already exists.")));
            }

            rulesets.Add(ruleset);
            return Task.FromResult(Result.OK);
        }
    }
}
