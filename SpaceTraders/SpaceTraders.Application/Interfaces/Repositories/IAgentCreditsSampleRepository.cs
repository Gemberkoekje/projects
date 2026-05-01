namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IAgentCreditsSampleRepository
{
    Task AppendAsync(long credits, CancellationToken cancellationToken = default);
}
