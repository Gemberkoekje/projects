using Godot;

namespace Curator.Presentation.Ui;

/// <summary>Bare space behind the documents: clicking it raises <see cref="Clicked"/>, so a note being written lets go of the keyboard.</summary>
public partial class FocusCatcher : Control
{
    /// <summary>Raised when the bare space is clicked.</summary>
    public event Action Clicked = delegate { };

    /// <inheritdoc />
    public override void _Ready() => MouseFilter = MouseFilterEnum.Stop;

    /// <inheritdoc />
    public override void _GuiInput(InputEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left or MouseButton.Right })
        {
            Clicked();
        }
    }
}
