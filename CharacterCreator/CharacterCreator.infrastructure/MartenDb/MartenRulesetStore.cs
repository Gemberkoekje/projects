using CharacterCreator.application.Ports;
using CharacterCreator.core.Rulesets;
using JasperFx;
using Marten;

namespace CharacterCreator.infrastructure.MartenDb;

/// <summary>Published rulesets as Marten documents, one per ruleset, never changed after it is added.</summary>
/// <remarks>A session per call, over the singleton document store.</remarks>
/// <param name="store">The Marten document store.</param>
public sealed class MartenRulesetStore(IDocumentStore store) : IRulesetStore
{
    private readonly IDocumentStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async Task<Result<PublishedRuleset>> CurrentAsync(CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        var current = await session.Query<PublishedRuleset>()
            .OrderByDescending(ruleset => ruleset.PublishedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return current is null
            ? Result.WithMessages<PublishedRuleset>(ValidationMessage.Error("No ruleset has been published yet."))
            : Result.For(current);
    }

    /// <inheritdoc />
    public async Task<Result<PublishedRuleset>> GetAsync(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return Result.WithMessages<PublishedRuleset>(ValidationMessage.Error("A ruleset id is needed."));
        }

        await using var session = store.QuerySession();
        var ruleset = await session.LoadAsync<PublishedRuleset>(id, cancellationToken);
        return ruleset is null
            ? Result.WithMessages<PublishedRuleset>(ValidationMessage.Error($"There is no ruleset '{id}'."))
            : Result.For(ruleset);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RulesetSummary>> ListAsync(int max, CancellationToken cancellationToken)
    {
        await using var session = store.QuerySession();
        var summaries = await session.Query<PublishedRuleset>()
            .OrderByDescending(ruleset => ruleset.PublishedAt)
            .Take(max)
            .Select(ruleset => new RulesetSummary
            {
                Id = ruleset.Id,
                PublishedAt = ruleset.PublishedAt,
                PublishedBy = ruleset.PublishedBy,
                Source = ruleset.Source,
                Provisional = ruleset.Provisional,
            })
            .ToListAsync(cancellationToken);
        return [.. summaries];
    }

    /// <inheritdoc />
    public Task<Result> AddAsync(PublishedRuleset ruleset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        return InsertAsync(ruleset, cancellationToken);
    }

    private async Task<Result> InsertAsync(PublishedRuleset ruleset, CancellationToken cancellationToken)
    {
        await using var session = store.LightweightSession();
        session.Insert(ruleset);
        try
        {
            await session.SaveChangesAsync(cancellationToken);
            return Result.OK;
        }
        catch (DocumentAlreadyExistsException)
        {
            return Result.WithMessages(ValidationMessage.Error($"A ruleset '{ruleset.Id}' already exists; a published ruleset is never replaced."));
        }
    }
}
