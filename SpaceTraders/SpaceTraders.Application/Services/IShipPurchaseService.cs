using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Services;

public interface IShipPurchaseService
{
    Task<ShipPurchaseResult> TryPurchaseAsync(
        string shipType,
        string shipyardWaypoint,
        CancellationToken cancellationToken = default);
}

public sealed record ShipPurchaseResult
{
    public bool IsSuccess { get; init; }

    public string? FailureReason { get; init; }

    public long EstimatedCost { get; init; }

    public ShipModel? PurchasedShip { get; init; }

    public long ActualCost { get; init; }
}
