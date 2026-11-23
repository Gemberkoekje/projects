namespace Klaverjas.application.ReadModels;

/// <summary>A file to download.</summary>
public sealed record Export
{
    /// <summary>The name to save it as.</summary>
    public string Bestandsnaam { get; init; } = string.Empty;

    /// <summary>The .xlsx file.</summary>
    public IReadOnlyList<byte> Inhoud { get; init; } = [];
}
