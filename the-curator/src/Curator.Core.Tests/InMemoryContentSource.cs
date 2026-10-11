using System.Text.Json;
using System.Text.Json.Nodes;
using Curator.Core.Content;

namespace Curator.Core.Tests;

/// <summary>Content held in memory, so tests can break it on purpose.</summary>
internal sealed class InMemoryContentSource : IContentSource
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    private readonly SortedDictionary<string, string> files = new(StringComparer.Ordinal);

    public static InMemoryContentSource FromDirectory(string root)
    {
        var source = new InMemoryContentSource();
        foreach (var path in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            source.files[relative] = File.ReadAllText(path);
        }

        return source;
    }

    public IReadOnlyList<string> ListJsonFiles(string folder) =>
        files.Keys.Where(k => (Path.GetDirectoryName(k) ?? "").Replace('\\', '/') == folder).ToList();

    public bool Exists(string relativePath) => files.ContainsKey(relativePath);

    public string ReadText(string relativePath) => files[relativePath];

    public void Set(string relativePath, string text) => files[relativePath] = text;

    public void Remove(string relativePath) => files.Remove(relativePath);

    public void Edit(string relativePath, Action<JsonNode> edit)
    {
        var node = JsonNode.Parse(files[relativePath]) ?? throw new InvalidOperationException($"{relativePath} is empty.");
        edit(node);
        files[relativePath] = node.ToJsonString(Indented);
    }

    public ContentSet Load() => ContentLoader.Load(this);

    public IReadOnlyList<ContentProblem> Check() => ContentLoader.Check(this);
}
