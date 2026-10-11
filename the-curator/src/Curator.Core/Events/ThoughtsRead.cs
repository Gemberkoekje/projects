namespace Curator.Core.Events;

/// <summary>Read Thoughts was cast on the patron.</summary>
/// <param name="Fragment">What surfaced, or empty.</param>
/// <param name="Unlocks">Questions it unlocked.</param>
/// <param name="Noticed">Whether a warded mind noticed.</param>
public sealed record ThoughtsRead(string Fragment, IReadOnlyList<string> Unlocks, bool Noticed) : GameEvent;
