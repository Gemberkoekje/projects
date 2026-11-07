using System.Collections.Concurrent;
using System.IO;
using PdfSharp.Fonts;

namespace CharacterCreator.infrastructure.Printing;

/// <summary>
/// Guild Hall's fonts for the sheet, embedded in this assembly (SIL Open Font License 1.1,
/// <c>Printing/Fonts/OFL-*.txt</c>): Spectral for text, Inknut Antiqua for headings. The PDF looks
/// the same on a laptop, in CI and in the container, which has no fonts of its own.
/// </summary>
/// <remarks>
/// PDFsharp resolves fonts through one process-wide resolver, which may only ever be set to the same
/// instance: <see cref="Install"/> sets <see cref="Instance"/> once and is safe to call again (after
/// landerijen's print pack). Any other family resolves to Spectral, so nothing MigraDoc asks for
/// internally falls through to a system font. There is no italic face: PDFsharp slants Spectral.
/// </remarks>
internal sealed class EmbeddedFonts : IFontResolver
{
    /// <summary>The text family.</summary>
    public const string Text = "Spectral";

    /// <summary>The heading family.</summary>
    public const string Display = "Inknut Antiqua";

    /// <summary>The one instance PDFsharp is given.</summary>
    public static readonly EmbeddedFonts Instance = new ();

    private static readonly Lock Gate = new ();
    private readonly ConcurrentDictionary<string, byte[]> faces = new (StringComparer.Ordinal);

    private EmbeddedFonts()
    {
    }

    /// <summary>Makes <see cref="Instance"/> PDFsharp's font resolver, once.</summary>
    public static void Install()
    {
        lock (Gate)
        {
            if (!ReferenceEquals(GlobalFontSettings.FontResolver, Instance))
            {
                GlobalFontSettings.FontResolver = Instance;
            }
        }
    }

    /// <inheritdoc />
    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var display = string.Equals(familyName, Display, StringComparison.OrdinalIgnoreCase);
        var face = (display, bold) switch
        {
            (true, true) => "InknutAntiqua-Bold",
            (true, false) => "InknutAntiqua-SemiBold",
            (false, true) => "Spectral-SemiBold",
            _ => "Spectral-Regular",
        };
        return new FontResolverInfo(face, false, italic);
    }

    /// <inheritdoc />
    public byte[] GetFont(string faceName) => faces.GetOrAdd(faceName, Read);

    private static byte[] Read(string faceName)
    {
        var name = $"CharacterCreator.infrastructure.Printing.Fonts.{faceName}.ttf";
        using var stream = typeof(EmbeddedFonts).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The font {faceName} is not embedded as {name}.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
