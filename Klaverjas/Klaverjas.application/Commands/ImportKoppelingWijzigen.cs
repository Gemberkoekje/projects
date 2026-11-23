namespace Klaverjas.application.Commands;

/// <summary>Couples a name on the points sheet to another name on the pits sheet, or to none.</summary>
public sealed record ImportKoppelingWijzigen : ICommand
{
    /// <summary>The plan.</summary>
    public Guid Id { get; init; }

    /// <summary>The name on the points sheet.</summary>
    public string Naam { get; init; } = string.Empty;

    /// <summary>The name on the pits sheet; empty for none.</summary>
    public string PitNaam { get; init; } = string.Empty;
}
