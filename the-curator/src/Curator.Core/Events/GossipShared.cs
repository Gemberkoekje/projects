namespace Curator.Core.Events;

/// <summary>A patron passed on gossip about an outcome.</summary>
/// <param name="ResolutionId">The outcome.</param>
/// <param name="TellerPatronId">Who told it.</param>
/// <param name="Text">What they said.</param>
public sealed record GossipShared(string ResolutionId, string TellerPatronId, string Text) : GameEvent;
