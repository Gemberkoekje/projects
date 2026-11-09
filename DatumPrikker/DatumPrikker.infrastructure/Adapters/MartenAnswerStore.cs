using Marten;

namespace DatumPrikker.infrastructure.Adapters;

/// <summary>Keeps <see cref="Answer"/> documents in Marten; the id (poll + email) makes a second answer replace the first.</summary>
public sealed class MartenAnswerStore(IDocumentStore store) : IAnswerStore
{
    private readonly IDocumentStore store = store ?? throw new ArgumentNullException(nameof(store));

    /// <inheritdoc />
    public async Task<Result<Answer>> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        var answer = await session.LoadAsync<Answer>(id, cancellationToken);
        return answer is null
            ? Result.WithMessages<Answer>(ValidationMessage.Error("Er is nog geen antwoord.", nameof(id)))
            : answer;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Answer>> ListForPollAsync(Guid pollId, CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        return await session.Query<Answer>().Where(x => x.PollId == pollId).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Answer>> ListByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        using var session = store.QuerySession();
        return await session.Query<Answer>().Where(x => x.Email == email).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, int>> CountForPollsAsync(IReadOnlyCollection<Guid> pollIds, CancellationToken cancellationToken = default)
    {
        if (pollIds is not { Count: > 0 })
        {
            return new Dictionary<Guid, int>();
        }

        // Only the poll ids come back; a family app has tens of answers per poll, not thousands.
        var ids = pollIds.ToArray();
        using var session = store.QuerySession();
        var answered = await session.Query<Answer>().Where(x => ids.Contains(x.PollId)).Select(x => x.PollId).ToListAsync(cancellationToken);
        return answered.GroupBy(id => id).ToDictionary(group => group.Key, group => group.Count());
    }

    /// <inheritdoc />
    public Task<Result> SaveAsync(Answer answer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(answer);

        return StoreAsync(answer, cancellationToken);
    }

    private async Task<Result> StoreAsync(Answer answer, CancellationToken cancellationToken)
    {
        using var session = store.LightweightSession();
        session.Store(answer);
        await session.SaveChangesAsync(cancellationToken);
        return Result.OK;
    }
}
