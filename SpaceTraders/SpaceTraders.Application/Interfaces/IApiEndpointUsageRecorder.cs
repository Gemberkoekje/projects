namespace SpaceTraders.Application.Interfaces;

public interface IApiEndpointUsageRecorder
{
    /// <summary>Counts one call to the endpoint for the active agent.</summary>
    Task RecordAsync(string httpMethod, string endpoint, CancellationToken cancellationToken = default);

    /// <summary>Returns the sum of all recorded API calls across all endpoints for this agent.</summary>
    Task<long> GetTotalCallsAsync(CancellationToken cancellationToken = default);
}
