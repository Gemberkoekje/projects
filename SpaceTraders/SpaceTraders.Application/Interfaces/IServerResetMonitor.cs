namespace SpaceTraders.Application.Interfaces;

/// <summary>
/// Hears about API calls that failed because the SpaceTraders server was reset: the agent token
/// belongs to the previous reset, so every call with it fails from then on.
/// </summary>
public interface IServerResetMonitor
{
    /// <summary>Reports a call that failed with the reset error; <paramref name="detail"/> is the API's message.</summary>
    Task ReportAsync(string detail, CancellationToken cancellationToken);
}
