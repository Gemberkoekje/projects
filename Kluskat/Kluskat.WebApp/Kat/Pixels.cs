using System.Globalization;
using System.Text;

namespace Kluskat.WebApp.Kat;

/// <summary>Turns a character grid into SVG rects: one per horizontal run of equal colour.</summary>
internal static class Pixels
{
    /// <summary>Appends the rects of a grid.</summary>
    /// <param name="svg">Where to write.</param>
    /// <param name="pixels">The grid; characters missing from the palette are transparent.</param>
    /// <param name="kleuren">The palette.</param>
    /// <param name="yVerschuiving">Added to every row index, for grids with headroom.</param>
    internal static void Rechthoeken(StringBuilder svg, char[][] pixels, IReadOnlyDictionary<char, string> kleuren, int yVerschuiving = 0)
    {
        for (var y = 0; y < pixels.Length; y++)
        {
            var x = 0;
            while (x < pixels[y].Length)
            {
                var teken = pixels[y][x];
                var start = x;
                while (x < pixels[y].Length && pixels[y][x] == teken)
                {
                    x++;
                }

                if (kleuren.TryGetValue(teken, out var kleur))
                {
                    svg.Append(CultureInfo.InvariantCulture, $"<rect x=\"{start}\" y=\"{y + yVerschuiving}\" width=\"{x - start}\" height=\"1\" fill=\"{kleur}\"/>");
                }
            }
        }
    }
}
