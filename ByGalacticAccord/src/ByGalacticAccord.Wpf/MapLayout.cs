using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using ByGalacticAccord.Engine.Domain;

namespace ByGalacticAccord.Wpf;

/// <summary>
/// Screen layout and palette for the map. The slice-0 sector is a fixed set of seven named
/// locations, so we lay them out by name in normalised [0,1] space (Helios core on the left, the
/// Marches frontier on the right) and scale to the canvas. Unknown names fall back to a ring.
/// </summary>
internal static class MapLayout
{
    private static readonly Dictionary<string, Point> Normalised = new()
    {
        ["Meridian Station"] = new Point(0.16, 0.46),
        ["Verdant"] = new Point(0.30, 0.18),
        ["Helios Prime"] = new Point(0.28, 0.78),
        ["Tycho Foundry"] = new Point(0.46, 0.44),
        ["Halcyon Refinery"] = new Point(0.56, 0.72),
        ["Rust Belt"] = new Point(0.76, 0.36),
        ["Edge Station"] = new Point(0.92, 0.62),
    };

    public static Point Of(string name, double width, double height, double margin)
    {
        if (!Normalised.TryGetValue(name, out Point p))
        {
            p = new Point(0.5, 0.5);
        }

        return new Point(
            margin + (p.X * (width - (2 * margin))),
            margin + (p.Y * (height - (2 * margin))));
    }

    // ---- palette ----
    public static readonly Color Background = Color.FromRgb(0x0B, 0x0F, 0x1A);
    public static readonly Color Panel = Color.FromRgb(0x12, 0x18, 0x28);
    public static readonly Color Ink = Color.FromRgb(0xE6, 0xED, 0xF6);
    public static readonly Color Muted = Color.FromRgb(0x8A, 0x97, 0xAD);
    public static readonly Color Accent = Color.FromRgb(0x5E, 0xC8, 0xF2);     // player / highlight
    public static readonly Color Gold = Color.FromRgb(0xF2, 0xC8, 0x5E);

    public static Brush DangerBrush(DangerBand band) => band switch
    {
        DangerBand.Green => new SolidColorBrush(Color.FromRgb(0x4F, 0xB0, 0x6A)),
        DangerBand.Amber => new SolidColorBrush(Color.FromRgb(0xD9, 0xA1, 0x3A)),
        _ => new SolidColorBrush(Color.FromRgb(0xD9, 0x4F, 0x4F)),
    };

    public static Brush LocationBrush(LocationType type) => type switch
    {
        LocationType.CoreWorld => new SolidColorBrush(Color.FromRgb(0x6E, 0x8E, 0xF2)),
        LocationType.IndustrialWorld => new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xB5)),
        LocationType.AgriculturalWorld => new SolidColorBrush(Color.FromRgb(0x6F, 0xC0, 0x7A)),
        LocationType.MiningColony => new SolidColorBrush(Color.FromRgb(0xC4, 0x8A, 0x5A)),
        LocationType.FrontierOutpost => new SolidColorBrush(Color.FromRgb(0xD0, 0x6F, 0x6F)),
        LocationType.RefineryStation => new SolidColorBrush(Color.FromRgb(0x8E, 0xB8, 0xC4)),
        _ => new SolidColorBrush(Muted),
    };

    public static string ShortType(LocationType type) => type switch
    {
        LocationType.CoreWorld => "core world",
        LocationType.IndustrialWorld => "industrial",
        LocationType.AgriculturalWorld => "agricultural",
        LocationType.MiningColony => "mining",
        LocationType.FrontierOutpost => "frontier outpost",
        LocationType.RefineryStation => "refinery",
        _ => string.Empty,
    };
}
