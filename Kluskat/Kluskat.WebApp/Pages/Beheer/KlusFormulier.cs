using Qowaiv.Diagnostics.Contracts;

namespace Kluskat.WebApp.Pages.Beheer;

/// <summary>The fields of a klus as the beheer forms post them.</summary>
[Mutable("Model binding fills it in.")]
public sealed class KlusFormulier
{
    /// <summary>Where in the house.</summary>
    public string Ruimte { get; set; } = string.Empty;

    /// <summary>What to do.</summary>
    public string Naam { get; set; } = string.Empty;

    /// <summary>How many days the klus stays away after it is done.</summary>
    public int IntervalDagen { get; set; } = 14;

    /// <summary>Roughly how long it takes.</summary>
    public Grootte Grootte { get; set; } = Grootte.Klein;

    /// <summary>The bag the commands take.</summary>
    /// <returns>The data.</returns>
    public Klus.Data Data() => new Klus.Data
    {
        Ruimte = Ruimte,
        Naam = Naam,
        IntervalDagen = IntervalDagen,
        Grootte = Grootte,
    };

    /// <summary>Prefills the form from a klus.</summary>
    /// <param name="klus">The klus.</param>
    /// <returns>The form.</returns>
    public static KlusFormulier Van(KlusOverzicht klus)
    {
        ArgumentNullException.ThrowIfNull(klus);

        return new KlusFormulier
        {
            Ruimte = klus.Ruimte,
            Naam = klus.Naam,
            IntervalDagen = klus.IntervalDagen,
            Grootte = klus.Grootte,
        };
    }
}
