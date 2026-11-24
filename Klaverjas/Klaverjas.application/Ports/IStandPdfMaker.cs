namespace Klaverjas.application.Ports;

/// <summary>Makes the PDF of the standings, like Dad's month sheet (D2): to attach to the mail and to print.</summary>
public interface IStandPdfMaker
{
    /// <summary>Makes the PDF of what is online.</summary>
    /// <param name="online">The standings online.</param>
    /// <param name="instellingen">The club's name and the colours.</param>
    /// <returns>The PDF file.</returns>
    ReadOnlyMemory<byte> Maak(OnlineStand online, Instellingen instellingen);
}
