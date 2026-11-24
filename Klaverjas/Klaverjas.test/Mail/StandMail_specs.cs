using Klaverjas.application.Mail;
using static Klaverjas.test.TestHelpers.Fixtures;

namespace Klaverjas.test.Mail;

public sealed class StandMail_specs
{
    private const string Site = "https://klaverjas.example.org";

    private readonly Speler annie = Lid("Annie de Graaf");
    private readonly Speler kees = Lid("Kees Bakker");

    private OnlineStand Online(string bericht = "")
    {
        var stand = StandBerekening.Bereken([annie, kees], [Avond(Wk47, Speelt(annie, 4800, pitten: 2), Speelt(kees, 4500, pitten: 5))], Standaard, 2026);
        return new OnlineStand { Stand = StandWeergave.Van(stand, Standaard), Bericht = bericht, OnlineSinds = new DateTime(2026, 11, 16, 21, 00, 00, DateTimeKind.Utc) };
    }

    [Test]
    public void the_mail_greets_the_member_by_first_name_and_says_where_they_stand()
    {
        var (html, tekst) = StandMail.Inhoud(Online(), Standaard, kees, Site);

        html.Should().Contain("Beste Kees,");
        html.Should().Contain("Je staat <b>2e</b> met <b>4500</b> punten en <b>5</b> pitten.");
        tekst.Should().Contain("Je staat 2e met 4500 punten en 5 pitten.");
    }

    [Test]
    public void someone_not_in_the_standings_gets_no_place_line()
    {
        var (html, _) = StandMail.Inhoud(Online(), Standaard, Lid("Nieuw Lid"), Site);

        html.Should().Contain("Beste Nieuw,").And.NotContain("Je staat");
    }

    [Test]
    public void dads_note_is_shown_as_text_with_its_line_breaks()
    {
        var (html, tekst) = StandMail.Inhoud(Online("Tot maandag!\n<b>Half acht</b>"), Standaard, annie, Site);

        html.Should().Contain("Tot maandag!<br>&lt;b&gt;Half acht&lt;/b&gt;");
        tekst.Should().Contain("Tot maandag!\n<b>Half acht</b>");
    }

    [Test]
    public void the_mail_has_the_winners_the_tables_and_the_link()
    {
        var (html, _) = StandMail.Inhoud(Online(), Standaard, annie, Site);

        html.Should().Contain("Winnaars van maandag 16 november:");
        html.Should().Contain("Punten 2026").And.Contain("Pitten 2026").And.Contain("Week totalen");
        html.Should().Contain("<a href=\"https://klaverjas.example.org\">klaverjas.example.org</a>");
        html.Should().Contain("Groeten,<br>Wedstrijdleiding klaverjassen");
    }

    [Test]
    public void names_are_encoded()
    {
        var stand = StandBerekening.Bereken([annie with { Naam = "Annie <script>" }], [Avond(Wk47, Speelt(annie, 4800))], Standaard, 2026);
        var online = new OnlineStand { Stand = StandWeergave.Van(stand, Standaard), OnlineSinds = DateTime.UtcNow };

        StandMail.Inhoud(online, Standaard, annie, Site).Html.Should().Contain("Annie &lt;script&gt;").And.NotContain("<script>");
    }

    [TestCase(false, "Standen week 47")]
    [TestCase(true, "Standen week 47 (verbeterd)")]
    public void the_subject_carries_the_week(bool verbeterd, string onderwerp)
    {
        StandMail.Onderwerp(Standaard, Online(), verbeterd).Should().Be(onderwerp);
    }

    [TestCase("Standen week 47", "Standen week 47.pdf")]
    [TestCase("Stand: week 47/48", "Stand week 4748.pdf")]
    [TestCase("", "Stand.pdf")]
    public void the_attachment_is_named_after_the_subject(string onderwerp, string naam)
    {
        StandMail.BijlageNaam(onderwerp).Should().Be(naam);
    }
}
