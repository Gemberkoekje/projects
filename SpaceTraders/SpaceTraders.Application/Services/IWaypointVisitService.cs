namespace SpaceTraders.Application.Services;

public interface IWaypointVisitService
{
    Task MarkVisitedAsync(string waypointSymbol, CancellationToken cancellationToken);
}
