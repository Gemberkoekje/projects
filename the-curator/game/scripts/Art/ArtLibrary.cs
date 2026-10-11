using Godot;

namespace Curator.Presentation.Art;

/// <summary>
/// Finds the art for a slot: the designer's photographed painting in art/painted/ first, then the
/// interim Easel painting in art/easel/, else nothing (the slot draws its flat placeholder).
/// Unimported PNGs are read straight from disk, so a painting dropped in shows up even before the
/// editor has imported it.
/// </summary>
public static class ArtLibrary
{
    private static readonly string[] Folders = ["res://art/painted/", "res://art/easel/"];
    private static readonly Dictionary<string, Texture2D> Cache = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Missing = new(StringComparer.Ordinal);

    /// <summary>The texture for a slot, or false when it should draw its placeholder.</summary>
    /// <param name="slotId">The slot id.</param>
    /// <param name="texture">The texture found.</param>
    /// <returns>True when art exists for the slot.</returns>
    public static bool TryGet(string slotId, out Texture2D texture)
    {
        if (Cache.TryGetValue(slotId, out var cached))
        {
            texture = cached;
            return true;
        }

        if (!Missing.Contains(slotId))
        {
            foreach (var folder in Folders)
            {
                if (TryLoad($"{folder}{slotId}.png", out var found))
                {
                    Cache[slotId] = found;
                    texture = found;
                    return true;
                }
            }

            Missing.Add(slotId);
        }

        texture = new PlaceholderTexture2D();
        return false;
    }

    /// <summary>Where a slot's art came from: "painted", "easel" or "placeholder".</summary>
    /// <param name="slotId">The slot id.</param>
    /// <returns>The source.</returns>
    public static string SourceOf(string slotId)
    {
        foreach (var folder in Folders)
        {
            var path = $"{folder}{slotId}.png";
            if (ResourceLoader.Exists(path) || Godot.FileAccess.FileExists(path))
            {
                return folder.Contains("painted", StringComparison.Ordinal) ? "painted" : "easel";
            }
        }

        return "placeholder";
    }

    private static bool TryLoad(string path, out Texture2D texture)
    {
        if (ResourceLoader.Exists(path))
        {
            texture = GD.Load<Texture2D>(path);
            return true;
        }

        if (Godot.FileAccess.FileExists(path))
        {
            var image = Image.LoadFromFile(ProjectSettings.GlobalizePath(path));
            if (image is not null && !image.IsEmpty())
            {
                texture = ImageTexture.CreateFromImage(image);
                return true;
            }
        }

        texture = new PlaceholderTexture2D();
        return false;
    }
}
