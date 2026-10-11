using Godot;

namespace Curator.Presentation.Art;

/// <summary>One layer of the painted scene: its painting scaled to the slot, or its flat placeholder.</summary>
public partial class SlotNode : Node2D
{
    private ArtSlot slot = new("", default, 0, 1, "", Colors.Black, default);
    private Texture2D texture = new PlaceholderTexture2D();
    private bool hasArt;

    /// <summary>The slot this node shows.</summary>
    public ArtSlot Slot => slot;

    /// <summary>Whether a painting (rather than the placeholder) is showing.</summary>
    public bool HasArt => hasArt;

    /// <summary>Points the node at a slot and loads its art.</summary>
    /// <param name="artSlot">The slot.</param>
    public void Show(ArtSlot artSlot)
    {
        ArgumentNullException.ThrowIfNull(artSlot);
        slot = artSlot;
        Name = artSlot.Id;
        Position = artSlot.Rect.Position;
        ZIndex = artSlot.Z;
        LightMask = artSlot.LightMask;
        hasArt = ArtLibrary.TryGet(artSlot.Id, out texture);
        QueueRedraw();
    }

    /// <summary>Shows another slot's art in this slot's rect (patron_&lt;id&gt; falling back to patron_default).</summary>
    /// <param name="slotId">The art to show.</param>
    public void ShowArt(string slotId)
    {
        hasArt = ArtLibrary.TryGet(slotId, out texture) || ArtLibrary.TryGet(slot.Id, out texture);
        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        if (hasArt)
        {
            DrawTextureRect(texture, new Rect2(Vector2.Zero, slot.Rect.Size), false);
        }
        else
        {
            PlaceholderPainter.Draw(this, slot);
        }
    }
}
