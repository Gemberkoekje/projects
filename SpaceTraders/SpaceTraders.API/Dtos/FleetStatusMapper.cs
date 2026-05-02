using SpaceTraders.Application.DTOs;
using SpaceTraders.Application.Orchestration;

namespace SpaceTraders.API.Dtos;

/// <summary>
/// Static mapper that converts fleet status domain read-models to their API DTO equivalents.
/// </summary>
/// <remarks>Phase 16b: response DTO mapping.</remarks>
public static class FleetStatusMapper
{
    /// <summary>Maps an <see cref="OrchestratorGoalChain"/> to a <see cref="FleetGoalChainDto"/>.</summary>
    public static FleetGoalChainDto ToDto(OrchestratorGoalChain chain) =>
        new()
        {
            FleetGoalId = chain.FleetGoalId,
            FleetGoalKind = chain.FleetGoalKind.ToString(),
            Priority = chain.Priority,
            FleetGoalDescription = chain.FleetGoalDescription,
            ResourceNeeds = chain.ResourceNeeds.Select(ToDto).ToList(),
        };

    /// <summary>Maps a <see cref="ResourceNeedEntry"/> to a <see cref="ResourceNeedEntryDto"/>.</summary>
    public static ResourceNeedEntryDto ToDto(ResourceNeedEntry entry) =>
        new()
        {
            TradeSymbol = entry.TradeSymbol,
            UnitsNeeded = entry.UnitsNeeded,
            UnitsDelivered = entry.UnitsDelivered,
            PurposeDescription = entry.PurposeDescription,
            AssignedShips = entry.AssignedShips,
        };

    /// <summary>Maps a <see cref="ShipAssignmentSnapshot"/> to a <see cref="ShipAssignmentDto"/>.</summary>
    public static ShipAssignmentDto ToDto(ShipAssignmentSnapshot snapshot) =>
        new()
        {
            ShipSymbol = snapshot.ShipSymbol,
            GoalKind = snapshot.GoalKind.ToString(),
            GoalDescription = snapshot.GoalDescription,
            SourceWaypoint = snapshot.SourceWaypoint,
            DestinationWaypoint = snapshot.DestinationWaypoint,
            FleetGoalId = snapshot.FleetGoalId,
            FleetGoalDescription = snapshot.FleetGoalDescription,
            AssignedAt = snapshot.AssignedAt,
        };

    /// <summary>Maps a <see cref="ShipActivitySnapshot"/> to a <see cref="ShipActivityDto"/>.</summary>
    public static ShipActivityDto ToDto(ShipActivitySnapshot snapshot) =>
        new()
        {
            ShipSymbol = snapshot.ShipSymbol,
            LocalStatus = snapshot.LocalStatus.ToString(),
            CurrentWaypoint = snapshot.CurrentWaypoint,
            DestinationWaypoint = snapshot.DestinationWaypoint,
            EstimatedArrival = snapshot.EstimatedArrival,
            OnCooldown = snapshot.OnCooldown,
            CooldownExpiresAt = snapshot.CooldownExpiresAt,
            CargoUsed = snapshot.CargoUsed,
            CargoCapacity = snapshot.CargoCapacity,
            FuelCurrent = snapshot.FuelCurrent,
            FuelCapacity = snapshot.FuelCapacity,
            ActivityDescription = snapshot.ActivityDescription,
        };

    /// <summary>Maps a <see cref="ShipGoalHistoryEntry"/> to a <see cref="ShipGoalHistoryDto"/>.</summary>
    public static ShipGoalHistoryDto ToDto(ShipGoalHistoryEntry entry) =>
        new()
        {
            Id = entry.Id,
            GoalKind = entry.GoalKind,
            Outcome = entry.Outcome,
            Reason = entry.Reason,
            StartedAt = entry.StartedAt,
            EndedAt = entry.EndedAt,
        };
}
