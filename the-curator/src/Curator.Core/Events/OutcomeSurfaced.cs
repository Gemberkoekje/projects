using Curator.Core.Content;

namespace Curator.Core.Events;

/// <summary>The curator heard of an outcome.</summary>
/// <param name="ResolutionId">The resolved outcome.</param>
/// <param name="Channel">How it reached the curator.</param>
public sealed record OutcomeSurfaced(string ResolutionId, OutcomeChannel Channel) : GameEvent;
