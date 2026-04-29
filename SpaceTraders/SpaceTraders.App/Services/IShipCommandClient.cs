namespace SpaceTraders.App.Services;

public interface IShipCommandClient
{
    Task EnqueueDockAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task EnqueueOrbitAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task EnqueueRefuelAsync(string shipSymbol, bool fromCargo, CancellationToken cancellationToken = default);

    Task EnqueueNavigateAsync(string shipSymbol, string destinationWaypoint, CancellationToken cancellationToken = default);

    Task EnqueueSetFlightModeAsync(string shipSymbol, string flightMode, CancellationToken cancellationToken = default);

    Task EnqueueRepairAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task EnqueueScrapAsync(string shipSymbol, CancellationToken cancellationToken = default);

    Task EnqueueInstallMountAsync(string shipSymbol, string mountSymbol, CancellationToken cancellationToken = default);

    Task EnqueueRemoveMountAsync(string shipSymbol, string mountSymbol, CancellationToken cancellationToken = default);

    Task EnqueueInstallModuleAsync(string shipSymbol, string moduleSymbol, CancellationToken cancellationToken = default);

    Task EnqueueRemoveModuleAsync(string shipSymbol, string moduleSymbol, CancellationToken cancellationToken = default);
}
