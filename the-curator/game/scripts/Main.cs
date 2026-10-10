using Godot;

namespace Curator.Presentation;

/// <summary>The root of the main scene. M0: proves the Godot project runs and can reach Core.</summary>
public partial class Main : Node
{
    /// <inheritdoc />
    public override void _Ready()
    {
        var probe = Core.Game.Prng.NextInt(1, 1, 6);
        GD.Print($"The Curator: main scene ready (Godot {Engine.GetVersionInfo()["string"]}, .NET {System.Environment.Version}, Core probe {probe}).");
    }
}
