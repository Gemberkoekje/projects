namespace TheCuratool.Domain;

/// <summary>
/// Represents the lifecycle state of a <see cref="GameSession"/>.
/// </summary>
public enum GameStatus
{
    Unknown = 0,
    Drafting = 1,
    Completed = 2,
}
