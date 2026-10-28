using Microsoft.AspNetCore.Html;
using System.Globalization;
using System.Text;

namespace Kluskat.WebApp.Kat;

/// <summary>The small pixel icons, drawn like the cat so everything on screen is one style.</summary>
internal static class Pictogram
{
    private static readonly Dictionary<char, string> Kleuren = new Dictionary<char, string>
    {
        ['o'] = "#3b2417",
        ['r'] = "#e8553e",
        ['R'] = "#a3262f",
        ['y'] = "#f7c948",
        ['Y'] = "#c98f22",
        ['u'] = "#7fb2f0",
        ['U'] = "#3f74c9",
        ['w'] = "#ffffff",
    };

    /// <summary>The streak flame.</summary>
    internal static IHtmlContent Vlam => Html(
        "vlam",
        [
            "...o...",
            "..oro..",
            "..orro.",
            ".oryro.",
            ".oryyro",
            "oryyyro",
            "oryyyro",
            ".ooooo.",
        ]);

    /// <summary>A freeze, which covers a week that did not count.</summary>
    internal static IHtmlContent Bevriezing => Html(
        "bevriezing",
        [
            "...u...",
            ".u.u.u.",
            "..uUu..",
            "uuUwUuu",
            "..uUu..",
            ".u.u.u.",
            "...u...",
        ]);

    /// <summary>An achievement medal.</summary>
    internal static IHtmlContent Medaille => Html(
        "medaille",
        [
            ".rR.Rr.",
            ".rR.Rr.",
            "..rRr..",
            "..ooo..",
            ".oyyyo.",
            "oyywyyo",
            "oyyyyYo",
            ".oyyYo.",
            "..ooo..",
        ]);

    private static HtmlString Html(string naam, string[] rijen)
    {
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg class=\"pictogram pictogram-{naam}\" viewBox=\"0 0 {rijen[0].Length} {rijen.Length}\" xmlns=\"http://www.w3.org/2000/svg\" shape-rendering=\"crispEdges\" aria-hidden=\"true\">");
        Pixels.Rechthoeken(svg, [.. rijen.Select(r => r.ToCharArray())], Kleuren);
        svg.Append("</svg>");
        return new HtmlString(svg.ToString());
    }
}
