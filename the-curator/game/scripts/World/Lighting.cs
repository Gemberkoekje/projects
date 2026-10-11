using Curator.Core.Game;
using Curator.Presentation.Art;
using Godot;

namespace Curator.Presentation.World;

/// <summary>
/// The light: an ambient tint for the time of day, a flickering candle over the desk, a warm light
/// behind the patron and a soft window light across the far layers (BUILD_BRIEF §7.4).
/// Phases blend over the layout's blendSeconds.
/// </summary>
public partial class Lighting : Node2D
{
    private const int FarLayers = 2;
    private const int ArchwayLayers = 4;
    private const int DeskLayers = 8;

    private readonly CanvasModulate ambient = new() { Name = "Ambient" };
    private readonly PointLight2D candle = new() { Name = "Candle" };
    private readonly PointLight2D archway = new() { Name = "Archway" };
    private readonly PointLight2D window = new() { Name = "Window" };
    private readonly FastNoiseLite noise = new() { NoiseType = FastNoiseLite.NoiseTypeEnum.Simplex, Seed = 7 };
    private ArtLayout layout = ArtLayout.Empty;
    private Vector2 candleHome;
    private float candleBase;
    private float time;
    // A blend may or may not be running; null means none (a justified nullable: entity or none).
    private Tween? blend;

    /// <summary>The current phase.</summary>
    public DayPhase Phase { get; private set; } = DayPhase.Morning;

    /// <summary>Builds the lights from the layout.</summary>
    /// <param name="artLayout">The layout.</param>
    public void Setup(ArtLayout artLayout)
    {
        layout = artLayout;
        AddChild(ambient);
        var glow = Glow();
        candleHome = layout.FlamePosition;
        candle.Texture = glow;
        candle.Color = layout.CandleColor;
        candle.Position = candleHome;
        candle.TextureScale = layout.CandleRadius * 2f / glow.GetWidth();
        candle.RangeItemCullMask = DeskLayers;
        AddChild(candle);
        archway.Texture = glow;
        archway.Color = layout.ArchwayColor;
        archway.Position = layout.ArchwayPosition;
        archway.TextureScale = layout.ArchwayRadius * 2f / glow.GetWidth();
        archway.RangeItemCullMask = ArchwayLayers;
        AddChild(archway);
        window.Texture = glow;
        window.Position = layout.WindowPosition;
        window.TextureScale = layout.WindowRadius * 2f / glow.GetWidth();
        window.RangeItemCullMask = FarLayers;
        AddChild(window);
        SetPhase(DayPhase.Morning, immediate: true);
    }

    /// <summary>Moves the light to a time of day.</summary>
    /// <param name="phase">The phase.</param>
    /// <param name="immediate">Skip the blend (screenshots, loading a save).</param>
    public void SetPhase(DayPhase phase, bool immediate)
    {
        if (!layout.Phases.TryGetValue(phase, out var light))
        {
            return;
        }

        Phase = phase;
        blend?.Kill();
        if (immediate)
        {
            ambient.Color = light.Ambient;
            window.Color = light.Window;
            window.Energy = light.WindowEnergy;
            archway.Energy = light.ArchwayEnergy;
            candleBase = light.CandleEnergy;
            return;
        }

        blend = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        blend.TweenProperty(ambient, "color", light.Ambient, layout.BlendSeconds);
        blend.TweenProperty(window, "color", light.Window, layout.BlendSeconds);
        blend.TweenProperty(window, "energy", light.WindowEnergy, layout.BlendSeconds);
        blend.TweenProperty(archway, "energy", light.ArchwayEnergy, layout.BlendSeconds);
        blend.TweenMethod(Callable.From<float>(v => candleBase = v), candleBase, light.CandleEnergy, layout.BlendSeconds);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        // Noise sampled over time, not per-frame randomness: the candle breathes rather than strobes.
        time += (float)delta;
        var flicker = noise.GetNoise1D(time * layout.FlickerHz * 10f) * layout.FlickerAmplitude;
        candle.Energy = Mathf.Max(0f, candleBase * (1f + flicker));
        var jitter = layout.JitterPixels;
        candle.Position = candleHome + new Vector2(
            noise.GetNoise2D(time * 7f, 13f) * jitter,
            noise.GetNoise2D(29f, time * 7f) * jitter);
    }

    private static GradientTexture2D Glow()
    {
        var gradient = new Gradient
        {
            Offsets = [0f, 0.45f, 1f],
            Colors = [Colors.White, new Color(1f, 1f, 1f, 0.45f), new Color(1f, 1f, 1f, 0f)],
        };
        return new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
            Width = 512,
            Height = 512,
        };
    }
}
