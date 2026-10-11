namespace Curator.Core.Events;

/// <summary>A new game began.</summary>
/// <param name="Seed">The game seed.</param>
/// <param name="ContentVersion">The content hash.</param>
/// <param name="StartMoney">Money at the start.</param>
/// <param name="StartReputation">Hidden reputation at the start.</param>
public sealed record GameStarted(long Seed, string ContentVersion, int StartMoney, int StartReputation) : GameEvent;
