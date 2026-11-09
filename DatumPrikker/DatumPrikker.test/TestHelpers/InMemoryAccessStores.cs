namespace DatumPrikker.test.TestHelpers;

/// <summary>Access documents in a dictionary, keyed by account id like the Marten store.</summary>
internal sealed class InMemoryAccessStore : IAccessStore
{
    private readonly Dictionary<string, Access> accesses = [];

    public IReadOnlyCollection<Access> All => accesses.Values;

    public Task<Result<Access>> LoadAsync(string accountId, CancellationToken cancellationToken = default)
        => Task.FromResult(accesses.TryGetValue(accountId, out var access)
            ? Result.For(access)
            : Result.WithMessages<Access>(ValidationMessage.Error("Er is nog geen aanvraag.", nameof(accountId))));

    public Task<IReadOnlyList<Access>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Access>>([.. accesses.Values]);

    public Task<int> CountRequestedAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(accesses.Values.Count(x => x.State == AccessState.Requested));

    public Task<Result> SaveAsync(Access access, CancellationToken cancellationToken = default)
    {
        accesses[access.Id] = access;
        return Task.FromResult(Result.OK);
    }
}

/// <summary>Pre-approvals in a dictionary, keyed by email like the Marten store.</summary>
internal sealed class InMemoryPreApprovalStore : IPreApprovalStore
{
    private readonly Dictionary<string, PreApproval> preApprovals = [];

    public IReadOnlyCollection<PreApproval> All => preApprovals.Values;

    public Task<bool> IsPreApprovedAsync(string email, CancellationToken cancellationToken = default)
        => Task.FromResult(preApprovals.ContainsKey(email));

    public Task<IReadOnlyList<PreApproval>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PreApproval>>([.. preApprovals.Values]);

    public Task<Result> SaveAsync(PreApproval preApproval, CancellationToken cancellationToken = default)
    {
        preApprovals[preApproval.Id] = preApproval;
        return Task.FromResult(Result.OK);
    }

    public Task<Result> DeleteAsync(string email, CancellationToken cancellationToken = default)
    {
        preApprovals.Remove(email);
        return Task.FromResult(Result.OK);
    }
}
