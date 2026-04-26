namespace SpaceTraders.Application.Interfaces;

public interface IApiEndpointUsageRecorder
{
    Task RecordAsync(string httpMethod, string endpoint, string agentToken, CancellationToken cancellationToken = default);
}
