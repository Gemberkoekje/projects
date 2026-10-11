using Curator.Presentation.Art;
using Godot;

namespace Curator.Presentation.World;

/// <summary>
/// The tall painted canvas (BUILD_BRIEF §7.1): the art layers built from art/layout.json, the
/// patron's silhouette slot, the desk, the lights and the camera.
/// </summary>
public partial class WorldView : Node2D
{
    private readonly Dictionary<string, SlotNode> slots = new(StringComparer.Ordinal);

    /// <summary>The camera.</summary>
    public CameraRig Camera { get; } = new() { Name = "Camera2D" };

    /// <summary>The light.</summary>
    public Lighting Lights { get; } = new() { Name = "Lights" };

    /// <summary>Where desk objects are added (M4).</summary>
    public Node2D Desk { get; } = new() { Name = "Desk", ZIndex = 15 };

    /// <summary>The patron's silhouette.</summary>
    public SlotNode Patron { get; private set; } = new();

    /// <summary>Builds the scene from the layout.</summary>
    /// <param name="layout">The layout.</param>
    public void Build(ArtLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var layers = new Node2D { Name = "Layers" };
        AddChild(layers);
        foreach (var slot in layout.Slots.Where(s => !s.Id.StartsWith("patron_", StringComparison.Ordinal)))
        {
            var node = new SlotNode();
            node.Show(slot);
            layers.AddChild(node);
            slots[slot.Id] = node;
        }

        var patronRoot = new Node2D { Name = "Patron" };
        AddChild(patronRoot);
        var patronSlot = layout.Slots.First(s => s.Id == "patron_default");
        Patron = new SlotNode();
        Patron.Show(patronSlot);
        Patron.Visible = false;
        patronRoot.AddChild(Patron);

        // The flame gives light rather than receiving it: its own layer keeps the ambient tint off it.
        var flameLayer = new CanvasLayer { Name = "FlameLayer", Layer = 1, FollowViewportEnabled = true };
        var flame = new CandleFlame { Name = "CandleFlame", Position = layout.FlamePosition };
        flame.Setup(layout.FlameFrames);
        flameLayer.AddChild(flame);
        AddChild(flameLayer);
        AddChild(Desk);
        Lights.Setup(layout);
        AddChild(Lights);
        AddChild(Camera);
    }

    /// <summary>Shows a patron's silhouette (their own painting if there is one), or hides it.</summary>
    /// <param name="patronId">The patron, or empty to empty the archway.</param>
    public void ShowPatron(string patronId)
    {
        ArgumentNullException.ThrowIfNull(patronId);
        Patron.Visible = patronId.Length > 0;
        if (Patron.Visible)
        {
            Patron.ShowArt($"patron_{patronId}");
        }
    }

    /// <summary>Where each slot's art comes from, for the debug overlay and screenshots.</summary>
    /// <returns>Slot id and source.</returns>
    public IReadOnlyList<(string Slot, string Source)> ArtSources() =>
        slots.Keys.Append("patron_default").Select(id => (id, ArtLibrary.SourceOf(id))).ToList();
}
