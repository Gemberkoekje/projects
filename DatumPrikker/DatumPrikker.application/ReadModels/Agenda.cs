namespace DatumPrikker.application.ReadModels;

/// <summary>A poll's picked date as an agenda file (D22).</summary>
public sealed record Agenda
{
    /// <summary>The poll's title, to name the file after.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The iCalendar text (see <see cref="AgendaFile"/>).</summary>
    public string Text { get; init; } = string.Empty;
}
