namespace SpaceTraders.Application.Interfaces;

public interface IApiEndpointUsageRecorder
{
    Task RecordAsync(string httpMethod, string endpoint, string agentToken, CancellationToken cancellationToken = default);

    /// <summary>Returns the sum of all recorded API calls across all endpoints for this agent.</summary>
    Task<long> GetTotalCallsAsync(CancellationToken cancellationToken = default);
}
