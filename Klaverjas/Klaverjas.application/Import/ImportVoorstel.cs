namespace Klaverjas.application.Import;

/// <summary>
/// A read workbook, planned but not applied: who is who, what gets written, and every place where
/// the app's sums differ from the workbook's. Kept as a document until Dad presses "Inlezen".
/// </summary>
public sealed record ImportVoorstel
{
    /// <summary>The document identity.</summary>
    public Guid Id { get; init; }

    /// <summary>The uploaded file's name, to show.</summary>
    public string Bestandsnaam { get; init; } = string.Empty;

    /// <summary>When the file was read (UTC).</summary>
    public DateTime GemaaktOp { get; init; }

    /// <summary>The workbook as read.</summary>
    public StandenBestand Bestand { get; init; } = new StandenBestand();

    /// <summary>One per `Stand` row: the player it becomes, and the `pit` row coupled to it.</summary>
    public IReadOnlyList<ImportSpeler> Spelers { get; init; } = [];

    /// <summary>The player the unnamed "invallers" row becomes; <see cref="Guid.Empty"/> when that row is empty.</summary>
    public Guid InvallerId { get; init; }

    /// <summary>Whether <see cref="InvallerId"/> is a new player.</summary>
    public bool InvallerIsNieuw { get; init; }

    /// <summary>The `pit` rows coupled to nobody.</summary>
    public IReadOnlyList<string> LossePitNamen { get; init; } = [];

    /// <summary>How many evenings already in the app the import replaces (the old ones are kept as versions).</summary>
    public int VervangenAvonden { get; init; }

    /// <summary>Where the app's sums differ from the workbook's.</summary>
    public IReadOnlyList<Verschil> Verschillen { get; init; } = [];
}

/// <summary>One `Stand` row and what it becomes.</summary>
public sealed record ImportSpeler
{
    /// <summary>The name on the `Stand` sheet; the player gets this name.</summary>
    public string Naam { get; init; } = string.Empty;

    /// <summary>The coupled `pit` row's name; empty when none.</summary>
    public string PitNaam { get; init; } = string.Empty;

    /// <summary>How the two rows were coupled.</summary>
    public Koppeling Koppeling { get; init; }

    /// <summary>The player: an existing one with the same name, or a new one.</summary>
    public Guid SpelerId { get; init; }

    /// <summary>Whether <see cref="SpelerId"/> is a new player.</summary>
    public bool IsNieuw { get; init; }
}

/// <summary>How a `Stand` row and a `pit` row were coupled (R9: the two sheets spell some names differently).</summary>
public enum Koppeling
{
    /// <summary>Not coupled: the player gets no pits from the workbook.</summary>
    None = 0,

    /// <summary>The same name (ignoring capitals, accents and spacing).</summary>
    Precies = 1,

    /// <summary>A name that looks like it (a letter or so different): Dad confirms it.</summary>
    LijktOp = 2,

    /// <summary>Chosen by Dad.</summary>
    Handmatig = 3,
}

/// <summary>A place where the app's sum differs from the workbook's.</summary>
public sealed record Verschil
{
    /// <summary>Which sum.</summary>
    public VerschilSoort Soort { get; init; }

    /// <summary>Whose total; empty for a week total.</summary>
    public string Wie { get; init; } = string.Empty;

    /// <summary>Which evening, for a week total.</summary>
    public DateOnly Datum { get; init; }

    /// <summary>What the workbook says.</summary>
    public int Excel { get; init; }

    /// <summary>What the app adds up to.</summary>
    public int Programma { get; init; }
}

/// <summary>Which sum differs.</summary>
public enum VerschilSoort
{
    /// <summary>Not set.</summary>
    None = 0,

    /// <summary>A player's points total.</summary>
    TotaalPunten = 1,

    /// <summary>A player's pits total.</summary>
    TotaalPitten = 2,

    /// <summary>A "Week totalen" cell of the pits.</summary>
    WeekTotaalPitten = 3,

    /// <summary>Pits on a date the points sheet has no evening for (an evening without play): not imported.</summary>
    PittenZonderAvond = 4,
}
