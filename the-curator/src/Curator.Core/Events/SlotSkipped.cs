namespace Curator.Core.Events;

/// <summary>Nobody was left to fill a slot.</summary>
/// <param name="SlotIndex">Today's slot.</param>
/// <param name="SlotId">The slot as scheduled.</param>
public sealed record SlotSkipped(int SlotIndex, string SlotId) : GameEvent;
