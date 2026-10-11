using System.Text.Json;
using Curator.Core.Game;
using Godot;

namespace Curator.Presentation.Art;

/// <summary>art/layout.json: the art slots, the candle flame, fallback colours and the lighting by phase.</summary>
public sealed class ArtLayout
{
    private ArtLayout()
    {
    }

    /// <summary>No slots and no light: what nodes hold until they're set up.</summary>
    public static ArtLayout Empty { get; } = new();

    public IReadOnlyList<ArtSlot> Slots { get; private init; } = [];

    public Vector2 FlamePosition { get; private init; }

    public IReadOnlyList<string> FlameFrames { get; private init; } = [];

    public IReadOnlyDictionary<string, Color> CoverColors { get; private init; } = new Dictionary<string, Color>();

    public IReadOnlyDictionary<string, Color> PaperColors { get; private init; } = new Dictionary<string, Color>();

    public IReadOnlyDictionary<DayPhase, PhaseLight> Phases { get; private init; } = new Dictionary<DayPhase, PhaseLight>();

    public float BlendSeconds { get; private init; }

    public Color CandleColor { get; private init; }

    public float CandleRadius { get; private init; }

    public float FlickerAmplitude { get; private init; }

    public float FlickerHz { get; private init; }

    public float JitterPixels { get; private init; }

    public Color ArchwayColor { get; private init; }

    public Vector2 ArchwayPosition { get; private init; }

    public float ArchwayRadius { get; private init; }

    public Vector2 WindowPosition { get; private init; }

    public float WindowRadius { get; private init; }

    /// <summary>Reads res://art/layout.json.</summary>
    /// <returns>The layout.</returns>
    public static ArtLayout Load()
    {
        using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://art/layout.json"));
        var root = document.RootElement;
        var lighting = root.GetProperty("lighting");
        var candle = lighting.GetProperty("candle");
        var archway = lighting.GetProperty("archway");
        var window = lighting.GetProperty("window");
        var flame = root.GetProperty("flame");
        return new ArtLayout
        {
            Slots = root.GetProperty("slots").EnumerateArray().Select(ReadSlot).ToList(),
            FlamePosition = new Vector2(flame.GetProperty("x").GetSingle(), flame.GetProperty("y").GetSingle()),
            FlameFrames = flame.GetProperty("frames").EnumerateArray().Select(f => f.GetString() ?? "").ToList(),
            CoverColors = Colors(root.GetProperty("bookCoverFallback")),
            PaperColors = Colors(root.GetProperty("paperFallback")),
            Phases = lighting.GetProperty("phases").EnumerateObject().ToDictionary(p => PhaseFrom(p.Name), p => ReadPhase(p.Value)),
            BlendSeconds = lighting.GetProperty("blendSeconds").GetSingle(),
            CandleColor = Color.FromHtml(candle.GetProperty("color").GetString()),
            CandleRadius = candle.GetProperty("radius").GetSingle(),
            FlickerAmplitude = candle.GetProperty("flickerAmplitude").GetSingle(),
            FlickerHz = candle.GetProperty("flickerHz").GetSingle(),
            JitterPixels = candle.GetProperty("jitterPixels").GetSingle(),
            ArchwayColor = Color.FromHtml(archway.GetProperty("color").GetString()),
            ArchwayPosition = new Vector2(archway.GetProperty("x").GetSingle(), archway.GetProperty("y").GetSingle()),
            ArchwayRadius = archway.GetProperty("radius").GetSingle(),
            WindowPosition = new Vector2(window.GetProperty("x").GetSingle(), window.GetProperty("y").GetSingle()),
            WindowRadius = window.GetProperty("radius").GetSingle(),
        };
    }

    private static ArtSlot ReadSlot(JsonElement slot)
    {
        var rect = Rect(slot.GetProperty("rect"));
        var arch = slot.TryGetProperty("arch", out var archElement) ? Rect(archElement) : default;
        return new ArtSlot(
            slot.GetProperty("id").GetString() ?? "",
            rect,
            slot.GetProperty("z").GetInt32(),
            slot.GetProperty("lightMask").GetInt32(),
            slot.GetProperty("placeholder").GetString() ?? "",
            Color.FromHtml(slot.GetProperty("color").GetString()),
            arch);
    }

    private static PhaseLight ReadPhase(JsonElement phase) => new(
        Color.FromHtml(phase.GetProperty("ambient").GetString()),
        Color.FromHtml(phase.GetProperty("window").GetString()),
        phase.GetProperty("windowEnergy").GetSingle(),
        phase.GetProperty("candleEnergy").GetSingle(),
        phase.GetProperty("archwayEnergy").GetSingle());

    private static Rect2 Rect(JsonElement values)
    {
        var v = values.EnumerateArray().Select(e => e.GetSingle()).ToArray();
        return new Rect2(v[0], v[1], v[2], v[3]);
    }

    private static Dictionary<string, Color> Colors(JsonElement element) =>
        element.EnumerateObject().ToDictionary(p => p.Name, p => Color.FromHtml(p.Value.GetString()), StringComparer.Ordinal);

    private static DayPhase PhaseFrom(string name) =>
        Enum.Parse<DayPhase>(name, ignoreCase: true);
}
