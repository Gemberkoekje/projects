namespace Curator.Core.Content;

/// <summary>Reads content from a folder on disk.</summary>
public sealed class DirectoryContentSource : IContentSource
{
    private readonly string root;

    /// <summary>Reads content from <paramref name="root"/>.</summary>
    /// <param name="root">The content folder, e.g. game/content.</param>
    public DirectoryContentSource(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        this.root = root;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListJsonFiles(string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var directory = Path.Combine(root, folder);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.GetFiles(directory, "*.json")
            .Select(path => folder.Length == 0 ? Path.GetFileName(path) : $"{folder}/{Path.GetFileName(path)}")
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc />
    public bool Exists(string relativePath) => File.Exists(Path.Combine(root, relativePath));

    /// <inheritdoc />
    public string ReadText(string relativePath) => File.ReadAllText(Path.Combine(root, relativePath));
}
