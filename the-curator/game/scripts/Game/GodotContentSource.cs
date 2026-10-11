using Curator.Core.Content;
using Godot;

namespace Curator.Presentation.Game;

/// <summary>Reads content from res://content (BUILD_BRIEF §4.5). Exports need *.json in the non-resource filter.</summary>
public sealed class GodotContentSource : IContentSource
{
    private const string Root = "res://content/";

    /// <inheritdoc />
    public IReadOnlyList<string> ListJsonFiles(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        using var directory = DirAccess.Open(Root + folder);
        if (directory is null)
        {
            return [];
        }

        return directory.GetFiles()
            .Where(name => name.EndsWith(".json", StringComparison.Ordinal))
            .Select(name => folder.Length == 0 ? name : $"{folder}/{name}")
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public bool Exists(string relativePath) => Godot.FileAccess.FileExists(Root + relativePath);

    /// <inheritdoc />
    public string ReadText(string relativePath) => Godot.FileAccess.GetFileAsString(Root + relativePath);
}
