using System.IO;

namespace Klaverjas.WebApp.Pages;

/// <summary>
/// Excel inlezen (D4) and "Alle scores downloaden" (D17). Reading a workbook writes nothing: the check
/// screen shows what would come in and where the sums differ from Excel's, and only "Inlezen" writes.
/// </summary>
[RequestFormLimits(MultipartBodyLengthLimit = MaxBytes)]
[RequestSizeLimit(MaxBytes)]
public sealed class InlezenModel(
    ImportVoorbereidenHandler voorbereiden,
    ImportVoorstelOphalenHandler ophalen,
    ImportKoppelingWijzigenHandler koppelen,
    ImportUitvoerenHandler uitvoeren,
    ImportAnnulerenHandler annuleren,
    ExportMakenHandler exporteren) : PageModel
{
    /// <summary>The largest workbook accepted; Dad's is about 200 kB.</summary>
    internal const int MaxBytes = 5 * 1024 * 1024;

    private readonly ImportVoorbereidenHandler voorbereiden = voorbereiden ?? throw new ArgumentNullException(nameof(voorbereiden));
    private readonly ImportVoorstelOphalenHandler ophalen = ophalen ?? throw new ArgumentNullException(nameof(ophalen));
    private readonly ImportKoppelingWijzigenHandler koppelen = koppelen ?? throw new ArgumentNullException(nameof(koppelen));
    private readonly ImportUitvoerenHandler uitvoeren = uitvoeren ?? throw new ArgumentNullException(nameof(uitvoeren));
    private readonly ImportAnnulerenHandler annuleren = annuleren ?? throw new ArgumentNullException(nameof(annuleren));
    private readonly ExportMakenHandler exporteren = exporteren ?? throw new ArgumentNullException(nameof(exporteren));

    /// <summary>The plan on screen.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    /// <summary>The name on the points sheet whose coupling changes.</summary>
    [BindProperty]
    public string Naam { get; set; } = string.Empty;

    /// <summary>The pits-sheet name chosen for it; empty for none.</summary>
    [BindProperty]
    public string PitNaam { get; set; } = string.Empty;

    /// <summary>Why the workbook could not be read, shown on Instellingen.</summary>
    [TempData]
    public string ExcelFout { get; set; } = string.Empty;

    /// <summary>The confirmation shown on Avonden after "Inlezen".</summary>
    [TempData]
    public string Ingelezen { get; set; } = string.Empty;

    /// <summary>The plan.</summary>
    public ImportVoorstel Voorstel { get; private set; } = new ImportVoorstel();

    /// <summary>Whether the plan is gone (applied or cancelled).</summary>
    public bool IsWeg { get; private set; }

    /// <summary>How many of the workbook's members are already players in the app.</summary>
    public int AlBekend { get; private set; }

    /// <summary>Shows the check screen.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes once loaded.</returns>
    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var voorstel = await ophalen.HandleAsync(new ImportVoorstelOphalen { Id = Id }, cancellationToken);
        IsWeg = !voorstel.IsValid;

        if (voorstel.IsValid)
        {
            Voorstel = voorstel.Value;
            AlBekend = Voorstel.Spelers.Count(s => !s.IsNieuw);
        }
    }

    /// <summary>"Bestand lezen" on Instellingen.</summary>
    /// <param name="bestand">The uploaded workbook.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The check screen, or Instellingen with why the file could not be read.</returns>
    public async Task<IActionResult> OnPostLezenAsync(IFormFile bestand, CancellationToken cancellationToken)
    {
        if (bestand is null || bestand.Length == 0)
        {
            ExcelFout = "Kies eerst het Excel-bestand met de standen.";
            return RedirectToPage("/Instellingen");
        }

        using var geheugen = new MemoryStream();
        await bestand.CopyToAsync(geheugen, cancellationToken);

        var result = await voorbereiden.HandleAsync(new ImportVoorbereiden { Inhoud = geheugen.ToArray(), Bestandsnaam = Path.GetFileName(bestand.FileName) }, cancellationToken);
        if (!result.IsValid)
        {
            ExcelFout = string.Join(" ", result.Messages.Select(m => m.Message ?? string.Empty));
            return RedirectToPage("/Instellingen");
        }

        return RedirectToPage(new { id = result.Value });
    }

    /// <summary>Another pits-sheet name chosen for a points-sheet name.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The check screen with the differences recomputed.</returns>
    public async Task<IActionResult> OnPostKoppelAsync(CancellationToken cancellationToken)
    {
        await koppelen.HandleAsync(new ImportKoppelingWijzigen { Id = Id, Naam = Naam, PitNaam = PitNaam ?? string.Empty }, cancellationToken);
        return RedirectToPage(new { id = Id });
    }

    /// <summary>"Inlezen".</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Avonden with a confirmation, or the check screen when the plan is gone.</returns>
    public async Task<IActionResult> OnPostInlezenAsync(CancellationToken cancellationToken)
    {
        var result = await uitvoeren.HandleAsync(new ImportUitvoeren { Id = Id }, cancellationToken);
        if (!result.IsValid)
        {
            return RedirectToPage(new { id = Id });
        }

        var r = result.Value;
        Ingelezen = $"Ingelezen: {r.Avonden} avonden, {r.NieuweSpelers} nieuwe spelers en {r.GeenSpeelavonden} avonden zonder klaverjassen. Kijk bij Stand of alles klopt.";
        return RedirectToPage("/Index");
    }

    /// <summary>"Annuleren": forgets the plan.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Instellingen.</returns>
    public async Task<IActionResult> OnPostAnnulerenAsync(CancellationToken cancellationToken)
    {
        await annuleren.HandleAsync(new ImportAnnuleren { Id = Id }, cancellationToken);
        return RedirectToPage("/Instellingen");
    }

    /// <summary>"Downloaden (Excel)": every player and every score.</summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The .xlsx file.</returns>
    public async Task<IActionResult> OnGetDownloadenAsync(CancellationToken cancellationToken)
    {
        var export = (await exporteren.HandleAsync(new ExportMaken(), cancellationToken)).Value;
        return File([.. export.Inhoud], "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", export.Bestandsnaam);
    }

    /// <summary>The pits-sheet names Dad can choose from for a row.</summary>
    /// <returns>Every pits-sheet name, in the sheet's order.</returns>
    public IEnumerable<string> PitNamen() => Voorstel.Bestand.Pitten.Select(p => p.Naam);

    /// <summary>A week as on the sheet: "wk 3 · 12 jan".</summary>
    /// <param name="datum">The evening.</param>
    /// <returns>The label.</returns>
    public static string WeekLabel(DateOnly datum) => $"wk {Kalender.Week(datum)} · {Weergave.DagMaand(datum)}";
}
