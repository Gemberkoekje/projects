namespace Curator.Core.Events;

/// <summary>A story flag was set.</summary>
/// <param name="Flag">The flag, '&lt;patronId&gt;:&lt;flag&gt;'.</param>
public sealed record FlagSet(string Flag) : GameEvent;
