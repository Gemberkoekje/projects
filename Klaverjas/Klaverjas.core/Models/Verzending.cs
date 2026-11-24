using System.Globalization;

namespace Klaverjas.core.Models;

/// <summary>
/// One mailing of the standings (D2, D8): when, which week, a test or the real thing, and how it went
/// for each member. Kept so Dad sees what was sent and to whom, and nobody is mailed twice by accident.
/// </summary>
public sealed record Verzending
{
    /// <summary>The document identity: the moment, sortable (<c>yyyyMMddHHmmssfff</c>).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The latest evening in the standings mailed.</summary>
    public DateOnly Datum { get; init; }

    /// <summary>When it was sent (UTC).</summary>
    public DateTime Verstuurd { get; init; }

    /// <summary>Whether this was the test mail to Dad himself.</summary>
    public bool IsProef { get; init; }

    /// <summary>The fingerprint of the standings mailed, to tell a corrected mailing from a repeat.</summary>
    public string Vingerafdruk { get; init; } = string.Empty;

    /// <summary>The subject as sent.</summary>
    public string Onderwerp { get; init; } = string.Empty;

    /// <summary>Every addressee, with how it went.</summary>
    public IReadOnlyList<Ontvanger> Ontvangers { get; init; } = [];

    /// <summary>How many mails went out.</summary>
    public int Gelukt => Ontvangers.Count(o => o.Gelukt);

    /// <summary>The addressees whose mail failed.</summary>
    public IEnumerable<Ontvanger> Mislukt => Ontvangers.Where(o => !o.Gelukt);

    /// <summary>The document identity of a mailing at a moment.</summary>
    /// <param name="verstuurd">When it was sent (UTC).</param>
    /// <returns>The moment as <c>yyyyMMddHHmmssfff</c>.</returns>
    public static string IdVan(DateTime verstuurd) => verstuurd.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
}

/// <summary>One addressee of a mailing.</summary>
public sealed record Ontvanger
{
    /// <summary>The member (empty for the test mail to Dad).</summary>
    public Guid SpelerId { get; init; }

    /// <summary>The name, as it was then.</summary>
    public string Naam { get; init; } = string.Empty;

    /// <summary>The address it went to.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Whether the mail server took it.</summary>
    public bool Gelukt { get; init; }

    /// <summary>Why not, in the mail server's words; empty when it went out.</summary>
    public string Fout { get; init; } = string.Empty;
}
