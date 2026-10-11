namespace Curator.Core.Commands;

/// <summary>Debug builds only: add mana.</summary>
/// <param name="Amount">How much.</param>
public sealed record DebugAddMana(int Amount) : Command;
