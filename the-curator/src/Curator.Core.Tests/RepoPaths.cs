namespace Curator.Core.Tests;

/// <summary>Finds the repository's folders from the test assembly's location.</summary>
internal static class RepoPaths
{
    private static readonly Lazy<string> RootPath = new(FindRoot);

    /// <summary>The folder holding TheCurator.sln.</summary>
    public static string Root => RootPath.Value;

    /// <summary>The real game content, game/content.</summary>
    public static string GameContent => Path.Combine(Root, "game", "content");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "TheCurator.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException($"TheCurator.sln not found above {AppContext.BaseDirectory}.");
    }
}
