using Curator.Core.Game;
using Curator.Presentation.World;
using Godot;

namespace Curator.Presentation.Shots;

/// <summary>
/// Screenshot mode (BUILD_BRIEF §7.11): plays a scripted sequence, saves PNGs and quits. Needs a
/// renderer, so it runs windowed (under Xvfb in the cloud).
/// </summary>
public partial class ShotRunner : Node
{
    private string directory = "";
    private WorldView world = new();

    /// <summary>Prepares the run.</summary>
    /// <param name="outputDirectory">Where the PNGs go.</param>
    /// <param name="worldView">The scene to photograph.</param>
    public void Setup(string outputDirectory, WorldView worldView)
    {
        directory = outputDirectory;
        world = worldView;
    }

    /// <inheritdoc />
    public override async void _Ready()
    {
        try
        {
            DirAccess.MakeDirRecursiveAbsolute(directory);
            GetWindow().Size = new Vector2I(1920, 1080);
            await Frames(6);
            world.ShowPatron("default");
            foreach (var phase in new[] { DayPhase.Morning, DayPhase.Dusk, DayPhase.Night })
            {
                world.Lights.SetPhase(phase, immediate: true);
                world.Camera.LookUp(immediate: true);
                await Shoot($"scene_{phase.ToString().ToLowerInvariant()}_up");
                world.Camera.LookDown(immediate: true);
                await Shoot($"scene_{phase.ToString().ToLowerInvariant()}_down");
            }

            GD.Print($"Shots written to {directory}: {string.Join(", ", world.ArtSources().Select(a => $"{a.Slot}={a.Source}"))}");
            GetTree().Quit(0);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            GD.PushError($"Screenshot run failed: {ex.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private async Task Shoot(string name)
    {
        await Frames(4);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        if (image.GetWidth() != 1920)
        {
            image.Resize(1920, 1080, Image.Interpolation.Lanczos);
        }

        var path = Path.Combine(directory, $"{name}.png");
        var error = image.SavePng(path);
        if (error != Error.Ok)
        {
            throw new IOException($"Couldn't save {path}: {error}");
        }
    }
}
