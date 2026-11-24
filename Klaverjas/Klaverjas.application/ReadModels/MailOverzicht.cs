namespace Klaverjas.application.ReadModels;

/// <summary>The mail card on Dad's Stand screen: who would get it, who has no address, and what was sent.</summary>
public sealed record MailOverzicht
{
    /// <summary>Whether the server can send mail at all.</summary>
    public bool IsIngesteld { get; init; }

    /// <summary>Where replies (and the test mail) go; empty until Dad fills it in.</summary>
    public string Antwoordadres { get; init; } = string.Empty;

    /// <summary>How many members would get the mail.</summary>
    public int Ontvangers { get; init; }

    /// <summary>The playing members without an email address (Dad prints the PDF for them).</summary>
    public IReadOnlyList<string> ZonderEmail { get; init; } = [];

    /// <summary>The latest real mailing of the week online; empty when none.</summary>
    public Verzending LaatsteVerzending { get; init; } = new Verzending();

    /// <summary>The latest test mail of the week online; empty when none.</summary>
    public Verzending LaatsteProef { get; init; } = new Verzending();

    /// <summary>Whether exactly the standings online were mailed already.</summary>
    public bool AlGemaild { get; init; }

    /// <summary>Whether this week went out before with other numbers: the next mail says "(verbeterd)".</summary>
    public bool Verbeterd { get; init; }

    /// <summary>The subject the next mail gets.</summary>
    public string Onderwerp { get; init; } = string.Empty;

    /// <summary>Whether this week has been mailed to the members.</summary>
    public bool IsGemaild => LaatsteVerzending.Id.Length > 0;

    /// <summary>Whether a test mail went out for this week.</summary>
    public bool IsProefGestuurd => LaatsteProef.Id.Length > 0;
}

/// <summary>"Zo ziet de e-mail eruit": the mail as the number 1 gets it.</summary>
public sealed record MailVoorbeeld
{
    /// <summary>The subject.</summary>
    public string Onderwerp { get; init; } = string.Empty;

    /// <summary>The sender's name.</summary>
    public string Van { get; init; } = string.Empty;

    /// <summary>The address it goes to, or a note that there is none.</summary>
    public string Aan { get; init; } = string.Empty;

    /// <summary>Where replies go.</summary>
    public string Antwoordadres { get; init; } = string.Empty;

    /// <summary>The attachment's file name.</summary>
    public string BijlageNaam { get; init; } = string.Empty;

    /// <summary>The mail itself, as HTML.</summary>
    public string Html { get; init; } = string.Empty;
}

/// <summary>The standings online as a PDF, like Dad's month sheet.</summary>
public sealed record StandPdf
{
    /// <summary>The file.</summary>
    public ReadOnlyMemory<byte> Inhoud { get; init; }

    /// <summary>Its name, such as "Standen week 47.pdf".</summary>
    public string Bestandsnaam { get; init; } = string.Empty;
}
