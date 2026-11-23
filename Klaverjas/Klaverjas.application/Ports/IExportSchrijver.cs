namespace Klaverjas.application.Ports;

/// <summary>Writes every score to a file Dad can open without the app (D17: the data is never trapped).</summary>
public interface IExportSchrijver
{
    /// <summary>Writes the export.</summary>
    /// <param name="gegevens">Everything to write.</param>
    /// <returns>The .xlsx file.</returns>
    byte[] Schrijf(ExportGegevens gegevens);
}

/// <summary>Everything the export holds.</summary>
public sealed record ExportGegevens
{
    /// <summary>The settings (club name, absence score).</summary>
    public Instellingen Instellingen { get; init; } = new Instellingen();

    /// <summary>Every player.</summary>
    public IReadOnlyList<Speler> Spelers { get; init; } = [];

    /// <summary>Every evening, of every season.</summary>
    public IReadOnlyList<Avond> Avonden { get; init; } = [];

    /// <summary>The latest season's standings.</summary>
    public Stand Stand { get; init; } = Stand.Leeg(0);
}
