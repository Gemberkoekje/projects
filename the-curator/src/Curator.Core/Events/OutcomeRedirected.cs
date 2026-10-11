using Curator.Core.Content;

namespace Curator.Core.Events;

/// <summary>A 'return' outcome will arrive as a letter because its patron won't come back in time.</summary>
/// <param name="ResolutionId">The resolved outcome.</param>
/// <param name="Channel">The new channel.</param>
/// <param name="SurfaceDay">The new surface day.</param>
public sealed record OutcomeRedirected(string ResolutionId, OutcomeChannel Channel, int SurfaceDay) : GameEvent;
