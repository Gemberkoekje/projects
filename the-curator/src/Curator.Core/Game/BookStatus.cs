namespace Curator.Core.Game;

/// <summary>Where a book is.</summary>
public enum BookStatus
{
    None = 0,
    OnShelf,
    Lent,

    /// <summary>Lent and never coming back.</summary>
    Gone,
}
