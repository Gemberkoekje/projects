using Curator.Presentation.Art;
using Curator.Presentation.Shots;
using Curator.Presentation.World;
using Godot;

namespace Curator.Presentation;

/// <summary>The root of the main scene: builds the world, the UI and the debug layer.</summary>
public partial class Main : Node
{
    /// <inheritdoc />
    public override void _Ready()
    {
        var options = CommandLine.Parse(OS.GetCmdlineUserArgs());
        var layout = ArtLayout.Load();
        var world = new WorldView { Name = "World" };
        world.Build(layout);
        AddChild(world);
        AddChild(new CanvasLayer { Name = "Ui", Layer = 10 });
        AddChild(new CanvasLayer { Name = "Debug", Layer = 20 });
        if (options.ShotsDir.Length > 0)
        {
            var shots = new ShotRunner { Name = "Shots" };
            shots.Setup(ResolveOutput(options.ShotsDir), world);
            AddChild(shots);
        }
    }

    /// <summary>
    /// Resolves an output folder from the command line. Godot runs inside game/, so relative paths
    /// are taken from the folder above it (the-curator/), where out/ lives.
    /// </summary>
    /// <param name="path">The path as given.</param>
    /// <returns>An absolute path.</returns>
    public static string ResolveOutput(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.StartsWith("res://", StringComparison.Ordinal) || path.StartsWith("user://", StringComparison.Ordinal))
        {
            return ProjectSettings.GlobalizePath(path);
        }

        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", path));
    }
}
