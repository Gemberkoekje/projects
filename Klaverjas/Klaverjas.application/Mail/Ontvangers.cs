namespace Klaverjas.application.Mail;

/// <summary>Who gets the standings mail, and what was mailed before.</summary>
internal static class Ontvangers
{
    /// <summary>The members who get the mail: playing members with an email address, by name.</summary>
    /// <param name="spelers">Every player.</param>
    /// <returns>The addressees.</returns>
    public static IReadOnlyList<Speler> Leden(IEnumerable<Speler> spelers)
        => [.. spelers.Where(s => s.IsLid && s.DoetMee && Emailadres.IsGeldig(s.Email)).OrderBy(s => s.Naam, StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>The playing members without (a usable) email address: Dad prints the PDF for them.</summary>
    /// <param name="spelers">Every player.</param>
    /// <returns>Their names, sorted.</returns>
    public static IReadOnlyList<string> ZonderEmail(IEnumerable<Speler> spelers)
        => [.. spelers.Where(s => s.IsLid && s.DoetMee && !Emailadres.IsGeldig(s.Email)).Select(s => s.Naam).Order(StringComparer.CurrentCultureIgnoreCase)];

    /// <summary>The member whose mail the preview and the test mail show: the number 1, as in the mock.</summary>
    /// <param name="online">The standings online.</param>
    /// <param name="spelers">Every player.</param>
    /// <returns>The member, or a stand-in with the name from the standings.</returns>
    public static Speler Voorbeeld(OnlineStand online, IEnumerable<Speler> spelers)
    {
        var eerste = online.Stand.Punten.FirstOrDefault();
        if (eerste is null)
        {
            return new Speler { Naam = "speler" };
        }

        return spelers.FirstOrDefault(s => s.Id == eerste.SpelerId) ?? new Speler { Id = eerste.SpelerId, Naam = eerste.Naam };
    }

    /// <summary>The real mailings (not tests) of the week online, newest first.</summary>
    /// <param name="verzendingen">Every mailing.</param>
    /// <param name="online">The standings online.</param>
    /// <returns>The mailings of that week.</returns>
    public static IReadOnlyList<Verzending> VanDezeWeek(IEnumerable<Verzending> verzendingen, OnlineStand online)
        => [.. verzendingen.Where(v => !v.IsProef && v.Datum == online.Stand.LaatsteAvond).OrderByDescending(v => v.Verstuurd)];

    /// <summary>Whether the week online was mailed before with other numbers: the next mail says "(verbeterd)".</summary>
    /// <param name="verzendingen">Every mailing.</param>
    /// <param name="online">The standings online.</param>
    /// <returns><see langword="true"/> after a correction.</returns>
    public static bool IsVerbeterd(IEnumerable<Verzending> verzendingen, OnlineStand online)
        => VanDezeWeek(verzendingen, online).Any(v => v.Vingerafdruk != online.Vingerafdruk);
}
