namespace Curator.Core.Game;

/// <summary>Where a book is and what the curator has read of it.</summary>
public sealed class BookState
{
    private readonly List<string> identified = [];
    private readonly List<string> resisted = [];
    private readonly List<string> removed = [];

    internal BookState(string id) => Id = id;

    public string Id { get; }

    public BookStatus Status { get; internal set; } = BookStatus.OnShelf;

    /// <summary>The loan it is out on, or empty.</summary>
    public string CurrentLoanId { get; internal set; } = "";

    /// <summary>The morning it last came back, or 0.</summary>
    public int LastReturnedDay { get; internal set; }

    /// <summary>Pages identified, in the order first read.</summary>
    public IReadOnlyList<string> Identified => identified;

    /// <summary>Pages known not to come into focus.</summary>
    public IReadOnlyList<string> Resisted => resisted;

    /// <summary>Pages torn out.</summary>
    public IReadOnlyList<string> Removed => removed;

    /// <summary>Whether the book is in the library: neither lent nor gone.</summary>
    public bool InLibrary => Status == BookStatus.OnShelf;

    internal void Identify(string pageId)
    {
        if (!identified.Contains(pageId))
        {
            identified.Add(pageId);
        }
    }

    internal void Resist(string pageId)
    {
        if (!resisted.Contains(pageId))
        {
            resisted.Add(pageId);
        }
    }

    internal void Remove(string pageId)
    {
        if (!removed.Contains(pageId))
        {
            removed.Add(pageId);
        }
    }
}
