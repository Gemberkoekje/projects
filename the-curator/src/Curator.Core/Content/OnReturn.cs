namespace Curator.Core.Content;

/// <summary>What happens to the book when it comes back.</summary>
public sealed record OnReturn
{
    /// <summary>Nothing happens.</summary>
    public static readonly OnReturn Nothing = new();

    /// <summary>A page id torn out of the book on its return, or empty.</summary>
    public string RemovePage { get; init; } = "";
}
