namespace SpaceTraders.Application.Services;

public interface IMarketRefreshService
{
    Task RefreshIfApplicableAsync(string waypointSymbol, CancellationToken cancellationToken);
}
