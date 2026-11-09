namespace DatumPrikker.test.TestHelpers;

/// <summary>Polls in a dictionary, found by share token like the Marten store.</summary>
internal sealed class InMemoryPollStore : IPollStore
{
    private readonly Dictionary<Guid, Poll> polls = [];

    public IReadOnlyCollection<Poll> All => polls.Values;

    public Task<Result<Poll>> LoadAsync(string shareToken, CancellationToken cancellationToken = default)
    {
        var poll = polls.Values.FirstOrDefault(x => x.ShareToken == shareToken);
        return Task.FromResult(poll is null
            ? Result.WithMessages<Poll>(ValidationMessage.Error(IPollStore.NotFound, nameof(shareToken)))
            : Result.For(poll));
    }

    public Task<bool> IsShareTokenTakenAsync(string shareToken, CancellationToken cancellationToken = default)
        => Task.FromResult(polls.Values.Any(x => x.ShareToken == shareToken));

    public Task<IReadOnlyList<Poll>> ListOwnedByAsync(string accountId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Poll>>([.. polls.Values.Where(x => x.OwnerAccountId == accountId)]);

    public Task<IReadOnlyList<Poll>> LoadManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Poll>>([.. polls.Values.Where(x => ids.Contains(x.Id))]);

    public Task<IReadOnlyList<Poll>> ListAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Poll>>([.. polls.Values]);

    public Task<Result> SaveAsync(Poll poll, CancellationToken cancellationToken = default)
    {
        polls[poll.Id] = poll;
        return Task.FromResult(Result.OK);
    }
}

/// <summary>Answers in a dictionary, keyed by id like the Marten store.</summary>
internal sealed class InMemoryAnswerStore : IAnswerStore
{
    private readonly Dictionary<string, Answer> answers = [];

    public IReadOnlyCollection<Answer> All => answers.Values;

    public Task<Result<Answer>> LoadAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(answers.TryGetValue(id, out var answer)
            ? Result.For(answer)
            : Result.WithMessages<Answer>(ValidationMessage.Error("Er is nog geen antwoord.", nameof(id))));

    public Task<IReadOnlyList<Answer>> ListForPollAsync(Guid pollId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Answer>>([.. answers.Values.Where(x => x.PollId == pollId)]);

    public Task<IReadOnlyList<Answer>> ListByEmailAsync(string email, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Answer>>([.. answers.Values.Where(x => x.Email == email)]);

    public Task<IReadOnlyDictionary<Guid, int>> CountForPollsAsync(IReadOnlyCollection<Guid> pollIds, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, int>>(answers.Values
            .Where(x => pollIds.Contains(x.PollId))
            .GroupBy(x => x.PollId)
            .ToDictionary(group => group.Key, group => group.Count()));

    public Task<Result> SaveAsync(Answer answer, CancellationToken cancellationToken = default)
    {
        answers[answer.Id] = answer;
        return Task.FromResult(Result.OK);
    }
}
