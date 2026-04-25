using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Interfaces.Repositories;

public interface IAgentRepository
{
    Task<AgentModel?> GetAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(AgentModel agent, CancellationToken cancellationToken = default);
}
