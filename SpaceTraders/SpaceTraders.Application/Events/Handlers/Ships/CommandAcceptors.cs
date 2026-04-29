using SpaceTraders.Application.Commands.Contracts;
using SpaceTraders.Application.Commands.Ships;
using SpaceTraders.Domain.Events.Ships;
using Wolverine;

namespace SpaceTraders.Application.Events.Handlers.Ships;

public interface IDockedCommandAcceptor
{
    Task OrbitAsync(string shipSymbol, CancellationToken cancellationToken);

    Task BuyCargoAsync(string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken);

    Task SellCargoAsync(string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken);

    Task RefuelAsync(string shipSymbol, bool fromCargo, CancellationToken cancellationToken);

    Task DeliverContractAsync(
        string contractId,
        string shipSymbol,
        string tradeSymbol,
        int units,
        string destinationWaypoint,
        CancellationToken cancellationToken);

    Task RepairAsync(string shipSymbol, CancellationToken cancellationToken);

    Task ScrapAsync(string shipSymbol, CancellationToken cancellationToken);

    Task InstallMountAsync(string shipSymbol, string mountSymbol, CancellationToken cancellationToken);

    Task InstallModuleAsync(string shipSymbol, string moduleSymbol, CancellationToken cancellationToken);
}

public interface IInOrbitCommandAcceptor
{
    Task DockAsync(string shipSymbol, CancellationToken cancellationToken);

    Task NavigateAsync(string shipSymbol, string destinationWaypoint, CancellationToken cancellationToken);

    Task ExtractAsync(string shipSymbol, CancellationToken cancellationToken);

    Task SurveyAsync(string shipSymbol, CancellationToken cancellationToken);

    Task SiphonAsync(string shipSymbol, CancellationToken cancellationToken);

    Task SupplyConstructionAsync(
        string shipSymbol,
        string systemSymbol,
        string waypointSymbol,
        string tradeSymbol,
        int units,
        Guid correlationId,
        Guid causationId,
        CancellationToken cancellationToken);
}

public interface IInTransitCommandAcceptor
{
    Task ScheduleArrivalAsync(ShipArrivedEvent arrivedEvent, DateTimeOffset dueAt, CancellationToken cancellationToken);

    Task PublishInOrbitAsync(ShipInOrbitEvent inOrbitEvent, CancellationToken cancellationToken);
}

public sealed class DockedCommandAcceptor(IMessageBus bus) : IDockedCommandAcceptor
{
    public Task OrbitAsync(string shipSymbol, CancellationToken cancellationToken)
        => bus.SendAsync(new OrbitShipCommand(shipSymbol)).AsTask();

    public Task BuyCargoAsync(string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken)
        => bus.SendAsync(new BuyCargoCommand(shipSymbol, tradeSymbol, units)).AsTask();

    public Task SellCargoAsync(string shipSymbol, string tradeSymbol, int units, CancellationToken cancellationToken)
        => bus.SendAsync(new SellCargoCommand(shipSymbol, tradeSymbol, units)).AsTask();

    public Task RefuelAsync(string shipSymbol, bool fromCargo, CancellationToken cancellationToken)
        => bus.SendAsync(new RefuelShipCommand(shipSymbol, fromCargo)).AsTask();

    public Task DeliverContractAsync(
        string contractId,
        string shipSymbol,
        string tradeSymbol,
        int units,
        string destinationWaypoint,
        CancellationToken cancellationToken)
        => bus.SendAsync(new DeliverContractCommand(contractId, shipSymbol, tradeSymbol, units, destinationWaypoint)).AsTask();

    public Task RepairAsync(string shipSymbol, CancellationToken cancellationToken)
        => bus.SendAsync(new RepairShipCommand(shipSymbol)).AsTask();

    public Task ScrapAsync(string shipSymbol, CancellationToken cancellationToken)
        => bus.SendAsync(new ScrapShipCommand(shipSymbol)).AsTask();

    public Task InstallMountAsync(string shipSymbol, string mountSymbol, CancellationToken cancellationToken)
        => bus.SendAsync(new InstallMountCommand(shipSymbol, mountSymbol)).AsTask();

    public Task InstallModuleAsync(string shipSymbol, string moduleSymbol, CancellationToken cancellationToken)
        => bus.SendAsync(new InstallModuleCommand(shipSymbol, moduleSymbol)).AsTask();
}

public sealed class InOrbitCommandAcceptor(IMessageBus bus) : IInOrbitCommandAcceptor
{
    public Task DockAsync(string shipSymbol, CancellationToken cancellationToken)
        => bus.SendAsync(new DockShipCommand(shipSymbol)).AsTask();

    public Task NavigateAsync(string shipSymbol, string destinationWaypoint, CancellationToken cancellationToken)
        => bus.SendAsync(new NavigateShipCommand(shipSymbol, destinationWaypoint)).AsTask();

    public Task ExtractAsync(string shipSymbol, CancellationToken cancellationToken)
        => bus.SendAsync(new ExtractResourcesCommand(shipSymbol)).AsTask();

    public Task SurveyAsync(string shipSymbol, CancellationToken cancellationToken)
        => bus.SendAsync(new SurveyCommand(shipSymbol)).AsTask();

    public Task SiphonAsync(string shipSymbol, CancellationToken cancellationToken)
        => bus.SendAsync(new SiphonResourcesCommand(shipSymbol)).AsTask();

    public Task SupplyConstructionAsync(
        string shipSymbol,
        string systemSymbol,
        string waypointSymbol,
        string tradeSymbol,
        int units,
        Guid correlationId,
        Guid causationId,
        CancellationToken cancellationToken)
        => bus.SendAsync(new SupplyConstructionCommand(
            shipSymbol,
            systemSymbol,
            waypointSymbol,
            tradeSymbol,
            units,
            correlationId,
            causationId)).AsTask();
}

public sealed class InTransitCommandAcceptor(IMessageBus bus) : IInTransitCommandAcceptor
{
    public Task ScheduleArrivalAsync(ShipArrivedEvent arrivedEvent, DateTimeOffset dueAt, CancellationToken cancellationToken)
        => bus.ScheduleAsync(arrivedEvent, dueAt).AsTask();

    public Task PublishInOrbitAsync(ShipInOrbitEvent inOrbitEvent, CancellationToken cancellationToken)
        => bus.PublishAsync(inOrbitEvent).AsTask();
}
