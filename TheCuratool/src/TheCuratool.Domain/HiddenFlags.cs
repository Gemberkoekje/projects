namespace TheCuratool.Domain;

/// <summary>
/// Storyteller-only hidden flags attached to a chosen character.
/// These affect setup math (e.g. Drunk counts as an Outsider; Lunatic requires a real Demon)
/// without being visible to the player.
/// </summary>
/// <param name="IsDrunk">When <see langword="true"/>, the character is secretly the Drunk and counts as an Outsider for distribution purposes.</param>
/// <param name="IsLunatic">When <see langword="true"/>, the character is secretly the Lunatic; the script must still include a real Demon slot.</param>
public sealed record HiddenFlags(bool IsDrunk, bool IsLunatic);
