namespace SpaceTraders.Application.Services;

public interface IShipyardRefreshService
{
    Task RefreshIfApplicableAsync(string waypointSymbol, CancellationToken cancellationToken);
}
