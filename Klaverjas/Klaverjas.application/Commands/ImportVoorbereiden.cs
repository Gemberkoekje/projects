namespace Klaverjas.application.Commands;

/// <summary>Reads an uploaded "Standen" workbook and plans the import, without writing any score.</summary>
public sealed record ImportVoorbereiden : ICommand<Guid>
{
    /// <summary>The .xlsx file.</summary>
    public IReadOnlyList<byte> Inhoud { get; init; } = [];

    /// <summary>The file's name, to show.</summary>
    public string Bestandsnaam { get; init; } = string.Empty;
}
