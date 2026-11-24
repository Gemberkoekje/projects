namespace Klaverjas.application.Mail;

/// <summary>One mail to send.</summary>
public sealed record MailBericht
{
    /// <summary>The address.</summary>
    public string Naar { get; init; } = string.Empty;

    /// <summary>The addressee's name.</summary>
    public string NaarNaam { get; init; } = string.Empty;

    /// <summary>The sender's name (the address is the server's mail account).</summary>
    public string AfzenderNaam { get; init; } = string.Empty;

    /// <summary>Where replies go.</summary>
    public string Antwoordadres { get; init; } = string.Empty;

    /// <summary>The subject.</summary>
    public string Onderwerp { get; init; } = string.Empty;

    /// <summary>The mail as HTML.</summary>
    public string Html { get; init; } = string.Empty;

    /// <summary>The same mail as plain text, for mail programs without HTML.</summary>
    public string Tekst { get; init; } = string.Empty;

    /// <summary>The attached PDF.</summary>
    public ReadOnlyMemory<byte> Bijlage { get; init; }

    /// <summary>The attachment's file name.</summary>
    public string BijlageNaam { get; init; } = string.Empty;
}

/// <summary>How one mail went.</summary>
public sealed record MailUitslag
{
    /// <summary>A mail the server took.</summary>
    public static readonly MailUitslag Verstuurd = new MailUitslag { Gelukt = true };

    /// <summary>Whether the mail server took it.</summary>
    public bool Gelukt { get; init; }

    /// <summary>Why not; empty when it went out.</summary>
    public string Fout { get; init; } = string.Empty;
}
