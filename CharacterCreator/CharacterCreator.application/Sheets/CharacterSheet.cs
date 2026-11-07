namespace CharacterCreator.application.Sheets;

/// <summary>
/// A character sheet ready to lay out (DESIGN.md §5, Print): every text already in the reader's
/// language and every number already written. The writer only arranges it.
/// </summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record CharacterSheet
{
    /// <summary>The language's code, <c>nl</c> or <c>en</c>, for the document's metadata.</summary>
    required public string Language { get; init; }

    /// <summary>The character's name, or what to call an unnamed one.</summary>
    required public string Title { get; init; }

    /// <summary>The kind of plan and its XP, such as "New character · 15 XP".</summary>
    required public string Subtitle { get; init; }

    /// <summary>Which rules it was checked against, such as "Rules 2026-v07@… (provisional)".</summary>
    required public string Rules { get; init; }

    /// <summary>When it was made, such as "Made on 7-11-2026".</summary>
    required public string MadeOn { get; init; }

    /// <summary>The numbers at a glance: XP, spent, left, hit points, armour points, imbue caps.</summary>
    required public IReadOnlyList<SheetFact> Facts { get; init; }

    /// <summary>Energy per day, by type.</summary>
    required public IReadOnlyList<SheetFact> Energy { get; init; }

    /// <summary>What breaks a rule or should be known, in words; empty when the plan is clean.</summary>
    required public IReadOnlyList<string> Notes { get; init; }

    /// <summary>The chosen skills as a table.</summary>
    required public SheetTable Skills { get; init; }

    /// <summary>Each chosen skill's text.</summary>
    required public IReadOnlyList<SheetEntry> SkillTexts { get; init; }

    /// <summary>The spells and techniques the skills bring.</summary>
    required public IReadOnlyList<SheetEntry> Spells { get; init; }

    /// <summary>The recipes the skills bring.</summary>
    required public IReadOnlyList<SheetEntry> Recipes { get; init; }

    /// <summary>The loresheets the skills come with.</summary>
    required public IReadOnlyList<SheetLink> Loresheets { get; init; }

    /// <summary>XP over the coming events.</summary>
    required public SheetTable XpPerEvent { get; init; }

    /// <summary>The headings and fixed words of the sheet.</summary>
    required public SheetLabels Labels { get; init; }
}

/// <summary>A label and its value.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record SheetFact
{
    /// <summary>The label.</summary>
    required public string Label { get; init; }

    /// <summary>The value, written out.</summary>
    required public string Value { get; init; }
}

/// <summary>A table: column headers and rows of cells, with the columns that hold numbers.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record SheetTable
{
    /// <summary>The column headers.</summary>
    required public IReadOnlyList<string> Headers { get; init; }

    /// <summary>The relative width of each column.</summary>
    required public IReadOnlyList<double> Widths { get; init; }

    /// <summary>The indexes of the columns that hold numbers, set right-aligned.</summary>
    required public IReadOnlyList<int> Numeric { get; init; }

    /// <summary>The rows; each has one cell per header.</summary>
    required public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; }
}

/// <summary>An item with a title, a few facts and a text: a skill, a spell, a recipe.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record SheetEntry
{
    /// <summary>The title.</summary>
    required public string Title { get; init; }

    /// <summary>Its facts, such as cost and page; facts without a value are left out by the composer.</summary>
    required public IReadOnlyList<SheetFact> Facts { get; init; }

    /// <summary>Its text; paragraphs separated by blank lines. May be empty.</summary>
    required public string Text { get; init; }
}

/// <summary>A link with a title.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record SheetLink
{
    /// <summary>What it links to.</summary>
    required public string Title { get; init; }

    /// <summary>Where.</summary>
    required public string Url { get; init; }
}

/// <summary>The sheet's headings and fixed words, in its language.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record SheetLabels
{
    /// <summary>The heading over the facts and energy.</summary>
    required public string Overview { get; init; }

    /// <summary>The heading over the energy per day.</summary>
    required public string Energy { get; init; }

    /// <summary>The heading over the notes.</summary>
    required public string Notes { get; init; }

    /// <summary>The heading over the skills.</summary>
    required public string Skills { get; init; }

    /// <summary>The heading over the spells and techniques.</summary>
    required public string Spells { get; init; }

    /// <summary>The heading over the recipes.</summary>
    required public string Recipes { get; init; }

    /// <summary>The heading over the loresheets.</summary>
    required public string Loresheets { get; init; }

    /// <summary>The heading over XP per event.</summary>
    required public string XpPerEvent { get; init; }

    /// <summary>What to say where a list is empty.</summary>
    required public string None { get; init; }

    /// <summary>The word before the page number, such as "Page".</summary>
    required public string Page { get; init; }

    /// <summary>The word between the page number and the page count, such as "of".</summary>
    required public string Of { get; init; }
}
