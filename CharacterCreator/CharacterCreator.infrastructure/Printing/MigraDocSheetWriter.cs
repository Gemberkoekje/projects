using System.IO;
using CharacterCreator.application.Ports;
using CharacterCreator.application.Sheets;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace CharacterCreator.infrastructure.Printing;

/// <summary>
/// Lays a character sheet out as a PDF with MigraDoc (PDFsharp 6.2.4, MIT), in Guild Hall's print
/// look: always light, crimson Inknut Antiqua headings, Spectral text, vellum rules (DESIGN.md §5).
/// </summary>
/// <remarks>
/// <para>
/// The first page is the sheet itself: name, plan, the numbers, energy, what breaks a rule, and the
/// skills table. From the next page on: the skills' texts, the spells and techniques, the recipes,
/// the loresheets and XP per event. Every page's footer names the rules and the page.
/// </para>
/// <para>Stateless and thread-safe: every call builds its own document.</para>
/// </remarks>
public sealed class MigraDocSheetWriter : ISheetWriter
{
    private const string Small = "Small";
    private const string EntryTitle = "EntryTitle";
    private const string Eyebrow = "Eyebrow";

    // Guild Hall's light tokens (vortex-tools-style tokens.json), as literals: a PDF has no CSS.
    private static readonly Color Crimson = Color.FromRgb(0x93, 0x00, 0x00);
    private static readonly Color Ink = Color.FromRgb(0x22, 0x1c, 0x14);
    private static readonly Color Ink2 = Color.FromRgb(0x4a, 0x3e, 0x2d);
    private static readonly Color Ink3 = Color.FromRgb(0x6b, 0x5c, 0x46);
    private static readonly Color Gilt = Color.FromRgb(0x9a, 0x77, 0x26);
    private static readonly Color VellumLine = Color.FromRgb(0xe2, 0xd5, 0xb8);
    private static readonly Color PaperSunken = Color.FromRgb(0xf1, 0xe7, 0xd2);
    private static readonly Color NoticeBg = Color.FromRgb(0xef, 0xd9, 0xc2);
    private static readonly Color NoticeBorder = Color.FromRgb(0xd5, 0xae, 0x84);
    private static readonly Color NoticeInk = Color.FromRgb(0x5a, 0x3a, 0x1b);

    private static readonly Unit PageWidth = Unit.FromCentimeter(21.0 - (2 * 1.8));

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Write(CharacterSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        EmbeddedFonts.Install();

        var document = DocumentFor(sheet);
        var section = SectionFor(document, sheet);

        Front(section, sheet);
        Entries(section, sheet.Labels.Skills, sheet.SkillTexts, sheet.Labels.None, newPage: true);
        if (sheet.Spells.Count > 0)
        {
            Entries(section, sheet.Labels.Spells, sheet.Spells, sheet.Labels.None, newPage: false);
        }

        if (sheet.Recipes.Count > 0)
        {
            Entries(section, sheet.Labels.Recipes, sheet.Recipes, sheet.Labels.None, newPage: false);
        }

        Back(section, sheet);

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();

        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    private static Document DocumentFor(CharacterSheet sheet)
    {
        var document = new Document();
        document.Info.Title = sheet.Title;
        document.Info.Subject = sheet.Subtitle;
        document.Info.Author = "Vortex Adventures Character Creator";

        var normal = document.Styles[StyleNames.Normal];
        normal.Font.Name = EmbeddedFonts.Text;
        normal.Font.Size = 10;
        normal.Font.Color = Ink;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(4);

        var title = document.Styles[StyleNames.Heading1];
        title.Font.Name = EmbeddedFonts.Display;
        title.Font.Size = 22;
        title.Font.Bold = false;
        title.Font.Color = Crimson;
        title.ParagraphFormat.SpaceAfter = Unit.FromPoint(2);
        title.ParagraphFormat.KeepWithNext = true;

        var heading = document.Styles[StyleNames.Heading2];
        heading.Font.Name = EmbeddedFonts.Display;
        heading.Font.Size = 14;
        heading.Font.Bold = false;
        heading.Font.Color = Crimson;
        heading.ParagraphFormat.SpaceBefore = Unit.FromPoint(12);
        heading.ParagraphFormat.SpaceAfter = Unit.FromPoint(5);
        heading.ParagraphFormat.KeepWithNext = true;

        var entry = document.Styles.AddStyle(EntryTitle, StyleNames.Normal);
        entry.Font.Size = 11.5;
        entry.Font.Bold = true;
        entry.ParagraphFormat.SpaceBefore = Unit.FromPoint(9);
        entry.ParagraphFormat.SpaceAfter = Unit.FromPoint(1);
        entry.ParagraphFormat.KeepWithNext = true;

        var small = document.Styles.AddStyle(Small, StyleNames.Normal);
        small.Font.Size = 8.5;
        small.Font.Color = Ink3;
        small.ParagraphFormat.SpaceAfter = Unit.FromPoint(2);

        var eyebrow = document.Styles.AddStyle(Eyebrow, StyleNames.Normal);
        eyebrow.Font.Name = EmbeddedFonts.Display;
        eyebrow.Font.Size = 8;
        eyebrow.Font.Color = Gilt;
        eyebrow.ParagraphFormat.SpaceAfter = Unit.FromPoint(2);

        return document;
    }

    private static Section SectionFor(Document document, CharacterSheet sheet)
    {
        var section = document.AddSection();
        section.PageSetup = document.DefaultPageSetup.Clone();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2.0);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.FooterDistance = Unit.FromCentimeter(1.0);

        var footer = section.Footers.Primary.AddParagraph();
        footer.Style = Small;
        footer.Format.Borders.Top.Color = VellumLine;
        footer.Format.Borders.Top.Width = Unit.FromPoint(0.75);
        footer.Format.Borders.DistanceFromTop = Unit.FromPoint(4);
        footer.Format.TabStops.AddTabStop(PageWidth, TabAlignment.Right);
        footer.AddText($"{sheet.Title} · {sheet.Rules}");
        footer.AddTab();
        footer.AddText($"{sheet.Labels.Page} ");
        footer.AddPageField();
        footer.AddText($" {sheet.Labels.Of} ");
        footer.AddNumPagesField();

        return section;
    }

    /// <summary>The sheet itself: who, the numbers, what to watch, and the skills.</summary>
    private static void Front(Section section, CharacterSheet sheet)
    {
        section.AddParagraph("VORTEX ADVENTURES · CHARACTER CREATOR", Eyebrow);
        section.AddParagraph(sheet.Title, StyleNames.Heading1);

        var subtitle = section.AddParagraph(sheet.Subtitle);
        subtitle.Format.Font.Size = 12;
        subtitle.Format.Font.Color = Ink2;
        subtitle.Format.SpaceAfter = Unit.FromPoint(1);

        var made = section.AddParagraph($"{sheet.MadeOn} · {sheet.Rules}", Small);
        made.Format.Borders.Bottom.Color = Gilt;
        made.Format.Borders.Bottom.Width = Unit.FromPoint(1);
        made.Format.Borders.DistanceFromBottom = Unit.FromPoint(6);
        made.Format.SpaceAfter = Unit.FromPoint(8);

        Facts(section, sheet.Facts);

        section.AddParagraph(sheet.Labels.Energy, StyleNames.Heading2);
        if (sheet.Energy.Count == 0)
        {
            section.AddParagraph(sheet.Labels.None, Small);
        }
        else
        {
            Facts(section, sheet.Energy);
        }

        if (sheet.Notes.Count > 0)
        {
            section.AddParagraph(sheet.Labels.Notes, StyleNames.Heading2);
            Notes(section, sheet.Notes);
        }

        section.AddParagraph(sheet.Labels.Skills, StyleNames.Heading2);
        if (sheet.Skills.Rows.Count == 0)
        {
            section.AddParagraph(sheet.Labels.None, Small);
        }
        else
        {
            Grid(section, sheet.Skills);
        }
    }

    /// <summary>The loresheets and XP per event, after the texts.</summary>
    private static void Back(Section section, CharacterSheet sheet)
    {
        section.AddParagraph(sheet.Labels.Loresheets, StyleNames.Heading2).Format.SpaceBefore = Unit.FromPoint(20);
        if (sheet.Loresheets.Count == 0)
        {
            section.AddParagraph(sheet.Labels.None, Small);
        }

        foreach (var link in sheet.Loresheets)
        {
            var paragraph = section.AddParagraph();
            paragraph.AddFormattedText($"{link.Title}: ", TextFormat.Bold);
            var hyperlink = paragraph.AddHyperlink(link.Url, HyperlinkType.Web);
            hyperlink.Font.Color = Crimson;
            hyperlink.Font.Underline = Underline.Single;
            hyperlink.AddText(link.Url);
        }

        section.AddParagraph(sheet.Labels.XpPerEvent, StyleNames.Heading2);
        Grid(section, sheet.XpPerEvent);
    }

    /// <summary>Labels and values in a grid of four, the value large, as the screen's property boxes.</summary>
    private static void Facts(Section section, IReadOnlyList<SheetFact> facts)
    {
        const int Columns = 4;
        var table = section.AddTable();
        table.Borders.Visible = false;
        for (var column = 0; column < Columns; column++)
        {
            table.AddColumn(PageWidth / Columns);
        }

        for (var start = 0; start < facts.Count; start += Columns)
        {
            var row = table.AddRow();
            row.TopPadding = Unit.FromPoint(4);
            row.BottomPadding = Unit.FromPoint(4);
            for (var column = 0; column < Columns && start + column < facts.Count; column++)
            {
                var cell = row.Cells[column];
                cell.Shading.Color = PaperSunken;
                cell.Borders.Left.Color = Colors.White;
                cell.Borders.Left.Width = Unit.FromPoint(3);
                var label = cell.AddParagraph(facts[start + column].Label);
                label.Style = Small;
                var value = cell.AddParagraph(facts[start + column].Value);
                value.Format.Font.Name = EmbeddedFonts.Display;
                value.Format.Font.Size = 13;
                value.Format.SpaceAfter = Unit.FromPoint(1);
            }
        }

        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(2);
    }

    /// <summary>What breaks a rule or should be known, in Guild Hall's notice colours.</summary>
    private static void Notes(Section section, IReadOnlyList<string> notes)
    {
        var table = section.AddTable();
        table.AddColumn(PageWidth);
        var cell = table.AddRow().Cells[0];
        cell.Shading.Color = NoticeBg;
        cell.Borders.Color = NoticeBorder;
        cell.Borders.Width = Unit.FromPoint(0.75);
        table.Rows.LeftIndent = 0;
        table.TopPadding = Unit.FromPoint(5);
        table.BottomPadding = Unit.FromPoint(3);
        table.LeftPadding = Unit.FromPoint(7);
        table.RightPadding = Unit.FromPoint(7);
        foreach (var note in notes)
        {
            var paragraph = cell.AddParagraph($"• {note}");
            paragraph.Format.Font.Color = NoticeInk;
            paragraph.Format.SpaceAfter = Unit.FromPoint(2);
        }
    }

    /// <summary>A table with a shaded header row, vellum rules and numbers set right.</summary>
    private static void Grid(Section section, SheetTable data)
    {
        var table = section.AddTable();
        table.Borders.Visible = false;
        table.TopPadding = Unit.FromPoint(3);
        table.BottomPadding = Unit.FromPoint(3);
        table.LeftPadding = Unit.FromPoint(4);
        table.RightPadding = Unit.FromPoint(4);

        var total = data.Widths.Sum();
        for (var column = 0; column < data.Headers.Count; column++)
        {
            var added = table.AddColumn(PageWidth * (data.Widths[column] / total));
            added.Format.Alignment = data.Numeric.Contains(column) ? ParagraphAlignment.Right : ParagraphAlignment.Left;
        }

        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Shading.Color = PaperSunken;
        for (var column = 0; column < data.Headers.Count; column++)
        {
            var paragraph = header.Cells[column].AddParagraph(data.Headers[column]);
            paragraph.Format.Font.Size = 8.5;
            paragraph.Format.Font.Bold = true;
            paragraph.Format.Font.Color = Ink2;
            paragraph.Format.SpaceAfter = 0;
        }

        foreach (var cells in data.Rows)
        {
            var row = table.AddRow();
            row.Borders.Bottom.Color = VellumLine;
            row.Borders.Bottom.Width = Unit.FromPoint(0.5);
            for (var column = 0; column < data.Headers.Count && column < cells.Count; column++)
            {
                var paragraph = row.Cells[column].AddParagraph();
                paragraph.Format.SpaceAfter = 0;
                Lines(paragraph, cells[column]);
            }
        }
    }

    /// <summary>
    /// A section of entries: a title, its facts in a line, its text. The skills' texts start a new
    /// page, after the sheet; the rest follows on, so a printed sheet wastes no paper.
    /// </summary>
    private static void Entries(Section section, string heading, IReadOnlyList<SheetEntry> entries, string none, bool newPage)
    {
        if (newPage)
        {
            section.AddPageBreak();
        }

        var title = section.AddParagraph(heading, StyleNames.Heading2);
        title.Format.SpaceBefore = newPage ? 0 : Unit.FromPoint(20);

        if (entries.Count == 0)
        {
            section.AddParagraph(none, Small);
        }

        foreach (var entry in entries)
        {
            section.AddParagraph(entry.Title, EntryTitle);
            if (entry.Facts.Count > 0)
            {
                var facts = section.AddParagraph();
                facts.Style = Small;
                facts.Format.KeepWithNext = entry.Text.Length > 0;
                for (var index = 0; index < entry.Facts.Count; index++)
                {
                    if (index > 0)
                    {
                        facts.AddText("  ·  ");
                    }

                    facts.AddFormattedText($"{entry.Facts[index].Label}: ", TextFormat.Bold);
                    facts.AddText(entry.Facts[index].Value);
                }
            }

            foreach (var block in entry.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Lines(section.AddParagraph(), block);
            }
        }
    }

    /// <summary>Text with its line breaks kept.</summary>
    private static void Lines(Paragraph paragraph, string text)
    {
        var lines = (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                paragraph.AddLineBreak();
            }

            paragraph.AddText(lines[index]);
        }
    }
}
