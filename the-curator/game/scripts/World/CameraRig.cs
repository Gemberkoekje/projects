using Godot;

namespace Curator.Presentation.World;

/// <summary>
/// The camera on the tall canvas: the up view (y 0–1080, the patron) and the down view
/// (y 720–1800, the desk), with a short eased pan between them (BUILD_BRIEF §7.2).
/// </summary>
public partial class CameraRig : Camera2D
{
    private const float PanSeconds = 0.45f;
    private static readonly Vector2 UpCenter = new(960, 540);
    private static readonly Vector2 DownCenter = new(960, 1260);
    // A pan may or may not be running; null means none (a justified nullable: entity or none).
    private Tween? pan;

    /// <summary>Raised when the view changes; true for the up view.</summary>
    public event Action<bool> ViewChanged = delegate { };

    /// <summary>Whether the camera is on (or heading to) the up view.</summary>
    public bool LookingUp { get; private set; } = true;

    /// <inheritdoc />
    public override void _Ready()
    {
        Position = UpCenter;
        MakeCurrent();
    }

    /// <summary>Pans to the up view.</summary>
    /// <param name="immediate">Jump without the tween.</param>
    public void LookUp(bool immediate = false) => MoveTo(true, immediate);

    /// <summary>Pans to the down view.</summary>
    /// <param name="immediate">Jump without the tween.</param>
    public void LookDown(bool immediate = false) => MoveTo(false, immediate);

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (@event.IsActionPressed("pan_up"))
        {
            LookUp();
            GetViewport().SetInputAsHandled();
        }
        else if (@event.IsActionPressed("pan_down"))
        {
            LookDown();
            GetViewport().SetInputAsHandled();
        }
    }

    private void MoveTo(bool up, bool immediate)
    {
        var target = up ? UpCenter : DownCenter;
        var changed = LookingUp != up;
        LookingUp = up;
        pan?.Kill();
        if (immediate)
        {
            Position = target;
        }
        else
        {
            pan = CreateTween().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
            pan.TweenProperty(this, "position", target, PanSeconds);
        }

        if (changed)
        {
            ViewChanged(up);
        }
    }
}
