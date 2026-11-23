namespace Klaverjas.application.Ports;

/// <summary>Reads Dad's "Standen" workbook (D4).</summary>
public interface IStandenLezer
{
    /// <summary>Reads the `Stand` and `pit` sheets.</summary>
    /// <param name="inhoud">The .xlsx file.</param>
    /// <returns>What the workbook holds, or why it could not be read, in Dutch.</returns>
    Result<StandenBestand> Lees(byte[] inhoud);
}
