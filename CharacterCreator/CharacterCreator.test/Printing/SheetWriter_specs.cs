using System.IO;
using System.Text;
using CharacterCreator.application.Sheets;
using CharacterCreator.infrastructure.Printing;
using PdfSharp.Pdf.IO;

namespace CharacterCreator.test.Printing;

/// <summary>The PDF writer (DESIGN.md §5, Print): MigraDoc with Guild Hall's embedded fonts.</summary>
public sealed class SheetWriter_specs
{
    private static readonly MigraDocSheetWriter Writer = new ();

    [Test]
    public void it_writes_a_pdf()
    {
        var pdf = Writer.Write(Sheet());

        Encoding.ASCII.GetString(pdf.Span[..5]).Should().Be("%PDF-");
        Pages(pdf).Should().BeGreaterThanOrEqualTo(2, "the sheet, then the texts");
    }

    [Test]
    public void it_carries_guild_halls_fonts_so_the_container_needs_none()
    {
        var text = Encoding.Latin1.GetString(Writer.Write(Sheet()).Span);

        // PDF names escape a space as #20: "Spectral Regular" is written Spectral#20Regular.
        text.Should().Contain("+Spectral#20Regular").And.Contain("+Inknut#20Antiqua");
    }

    [Test]
    public void a_loresheet_is_a_link_that_works_on_screen()
        => Encoding.Latin1.GetString(Writer.Write(Sheet()).Span).Should().Contain("https://the-vortex.nl/loresheet.pdf");

    [Test]
    public void long_texts_run_on_over_more_pages()
    {
        var many = Sheet() with
        {
            SkillTexts = [.. Enumerable.Range(1, 40).Select(number => new SheetEntry
            {
                Title = $"Vaardigheid {number}",
                Facts = [new () { Label = "Kosten", Value = "2 XP" }],
                Text = string.Join("\n\n", Enumerable.Repeat("Een lange tekst over wat deze vaardigheid doet, en hoe je haar gebruikt.", 4)),
            })],
        };

        Pages(Writer.Write(many)).Should().BeGreaterThan(4);
    }

    [Test]
    public void an_empty_plan_still_prints()
    {
        var empty = Sheet() with
        {
            Facts = [],
            Energy = [],
            Notes = [],
            Skills = Sheet().Skills with { Rows = [] },
            SkillTexts = [],
            Spells = [],
            Recipes = [],
            Loresheets = [],
        };

        Pages(Writer.Write(empty)).Should().BeGreaterThanOrEqualTo(1);
    }

    [Test]
    public void sheets_can_be_written_at_the_same_time()
    {
        var pdfs = Enumerable.Range(0, 6).AsParallel().Select(_ => Writer.Write(Sheet())).ToList();

        pdfs.Should().AllSatisfy(pdf => Pages(pdf).Should().BeGreaterThanOrEqualTo(2));
    }

    private static int Pages(ReadOnlyMemory<byte> pdf)
    {
        using var stream = new MemoryStream(pdf.ToArray());
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    private static CharacterSheet Sheet() => new ()
    {
        Language = "nl",
        Title = "Ilse van de Vijfhoek",
        Subtitle = "Nieuw personage · 15 XP",
        Rules = "Regels 2026-v07@test (voorlopig)",
        MadeOn = "Gemaakt op 7-11-2026",
        Facts = [new () { Label = "XP", Value = "15" }, new () { Label = "Uitgegeven", Value = "12" }, new () { Label = "Over", Value = "3" }, new () { Label = "Levenspunten", Value = "2" }, new () { Label = "Pantserpunten", Value = "4" }],
        Energy = [new () { Label = "Spiritueel", Value = "10" }],
        Notes = ["Harnas II: vereist Harnas I", "3,5 XP over; alles boven 3 vervalt"],
        Skills = new SheetTable
        {
            Headers = ["Vaardigheid", "Aantal", "XP", "Blz.", "Opmerkingen"],
            Widths = [5, 1, 1, 1, 5],
            Numeric = [1, 2, 3],
            Rows = [["Harnas II", "1×", "2", "14", "vereist Harnas I"], ["Geestbeïnvloedend spreken", "1×", "4", "38", string.Empty]],
        },
        SkillTexts = [new () { Title = "Harnas II", Facts = [new () { Label = "Kosten", Value = "2 XP" }], Text = "Met deze vaardigheid kan je tot 4 armourpoints krijgen.\nRoep de “NO EFFECT” call." }],
        Spells = [new () { Title = "Heal Wounds", Facts = [new () { Label = "Energie", Value = "Spiritueel 1" }], Text = "De genezer moet de hand op de persoon houden." }],
        Recipes = [new () { Title = "Gif", Facts = [new () { Label = "Effect", Value = "Paralyze" }], Text = string.Empty }],
        Loresheets = [new () { Title = "Harnas I", Url = "https://the-vortex.nl/loresheet.pdf" }],
        XpPerEvent = new SheetTable
        {
            Headers = ["Evenement", "XP ervoor", "Erbij", "XP erna"],
            Widths = [3, 2, 2, 2],
            Numeric = [1, 2, 3],
            Rows = [["Evenement 1", "15", "+1,5", "16,5"]],
        },
        Labels = new SheetLabels
        {
            Overview = "Overzicht",
            Energy = "Energie per dag",
            Notes = "Let op",
            Skills = "Vaardigheden",
            Spells = "Spreuken en technieken",
            Recipes = "Recepten",
            Loresheets = "Loresheets",
            XpPerEvent = "XP per evenement",
            None = "Geen.",
            Page = "Pagina",
            Of = "van",
        },
    };
}
