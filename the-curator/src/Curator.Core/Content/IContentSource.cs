namespace Curator.Core.Content;

/// <summary>
/// File access for content (BUILD_BRIEF §4.5). Tests read from disk; Godot reads res://content.
/// Paths are relative to the content root and use forward slashes.
/// </summary>
public interface IContentSource
{
    /// <summary>The JSON files directly inside a folder.</summary>
    /// <param name="folder">"" for the content root, or a subfolder such as "books".</param>
    /// <returns>Relative paths such as "books/lanterns.json", sorted ordinally.</returns>
    IReadOnlyList<string> ListJsonFiles(string folder);

    /// <summary>Whether a file exists.</summary>
    /// <param name="relativePath">A path such as "balance.json".</param>
    /// <returns>True when the file exists.</returns>
    bool Exists(string relativePath);

    /// <summary>A file's text.</summary>
    /// <param name="relativePath">A path such as "balance.json".</param>
    /// <returns>The UTF-8 text.</returns>
    string ReadText(string relativePath);
}
