namespace Curator.Core.Events;

/// <summary>Money changed.</summary>
/// <param name="Delta">The change.</param>
/// <param name="Money">Money now.</param>
/// <param name="Reason">fee, upkeep or start.</param>
public sealed record MoneyChanged(int Delta, int Money, string Reason) : GameEvent;
