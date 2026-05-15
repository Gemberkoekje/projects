namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IPlanRepository
{
    Task<TPlan?> GetAsync<TPlan>(string planType, CancellationToken cancellationToken = default)
        where TPlan : class;

    Task UpsertAsync<TPlan>(string planType, TPlan state, CancellationToken cancellationToken = default)
        where TPlan : class;

    Task DeleteAsync(string planType, CancellationToken cancellationToken = default);
}
