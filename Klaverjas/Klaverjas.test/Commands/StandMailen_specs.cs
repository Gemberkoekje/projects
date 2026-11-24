using Klaverjas.application.Commands;
using Klaverjas.application.Queries;
using static Klaverjas.test.TestHelpers.Fixtures;

namespace Klaverjas.test.Commands;

public sealed class StandMailen_specs
{
    private const string Site = "https://klaverjas.example.org";

    private readonly Speler annie = Lid("Annie de Graaf") with { Email = "annie@example.org" };
    private readonly Speler kees = Lid("Kees Bakker") with { Email = "kees@example.org" };
    private readonly Speler riet = Lid("Riet Mulder");
    private readonly Speler gerrit = Lid("Gerrit Kok") with { Email = "gerrit@example.org", DoetMee = false };
    private readonly Speler bert = Invaller("Bert van Loon") with { Email = "bert@example.org" };

    private InMemoryAvondStore avonden = new InMemoryAvondStore();
    private InMemorySpelerStore spelers = new InMemorySpelerStore();
    private InMemoryInstellingenStore instellingen = new InMemoryInstellingenStore();
    private InMemoryOnlineStandStore online = new InMemoryOnlineStandStore();
    private InMemoryVerzendingStore verzendingen = new InMemoryVerzendingStore();
    private NepMailVerzender mail = new NepMailVerzender();
    private NepPdfMaker pdf = new NepPdfMaker();

    // NUnit shares one instance across the tests: every test starts from empty stores.
    [SetUp]
    public async Task Begin()
    {
        avonden = new InMemoryAvondStore(Avond(Wk47, Speelt(annie, 4800), Speelt(kees, 4500), Speelt(riet, 4300), Speelt(bert, 5000)));
        spelers = new InMemorySpelerStore(annie, kees, riet, gerrit, bert);
        instellingen = new InMemoryInstellingenStore(Standaard with { Antwoordadres = "pa@example.org" });
        online = new InMemoryOnlineStandStore();
        verzendingen = new InMemoryVerzendingStore();
        mail = new NepMailVerzender();
        pdf = new NepPdfMaker();
        await OnlineZettenAsync();
    }

    private Task OnlineZettenAsync()
        => new StandOnlineZettenHandler(avonden, spelers, instellingen, online, NullLogger<StandOnlineZettenHandler>.Instance).HandleAsync(new StandOnlineZetten());

    private Task<Result<Verzending>> MailenAsync(bool proef = false)
        => new StandMailenHandler(online, avonden, spelers, instellingen, verzendingen, pdf, mail, NullLogger<StandMailenHandler>.Instance)
            .HandleAsync(new StandMailen { Proef = proef, SiteAdres = Site });

    private async Task<MailOverzicht> OverzichtAsync()
        => (await new MailOphalenHandler(online, spelers, instellingen, verzendingen, pdf, mail).HandleAsync(new MailOverzichtOphalen())).Value;

    [Test]
    public async Task every_playing_member_with_an_address_gets_their_own_mail_with_the_pdf()
    {
        var result = await MailenAsync();

        result.IsValid.Should().BeTrue();
        mail.Verstuurd.Select(m => m.Naar).Should().Equal("annie@example.org", "kees@example.org");
        mail.Verstuurd.Should().AllSatisfy(m =>
        {
            m.Onderwerp.Should().Be("Standen week 47");
            m.Antwoordadres.Should().Be("pa@example.org");
            m.BijlageNaam.Should().Be("Standen week 47.pdf");
            m.Bijlage.IsEmpty.Should().BeFalse();
        });
        mail.Verstuurd[1].Html.Should().Contain("Beste Kees,");
        pdf.Gemaakt.Should().Be(1);
    }

    [Test]
    public async Task the_mailing_is_logged_per_member()
    {
        mail.Weigert.Add("kees@example.org");

        var verzending = (await MailenAsync()).Value;

        verzendingen.Alle.Should().ContainSingle().Which.Should().Be(verzending);
        verzending.Should().Match<Verzending>(v => !v.IsProef && v.Datum == Wk47 && v.Gelukt == 1);
        verzending.Mislukt.Should().ContainSingle().Which.Should().Match<Ontvanger>(o => o.Naam == "Kees Bakker" && o.Fout.Contains("550"));
    }

    [Test]
    public async Task the_test_mail_goes_to_dad_only()
    {
        var verzending = (await MailenAsync(proef: true)).Value;

        mail.Verstuurd.Should().ContainSingle().Which.Should().Match<application.Mail.MailBericht>(m =>
            m.Naar == "pa@example.org" && m.NaarNaam.Length == 0 && m.Onderwerp == "Proef: Standen week 47" && m.BijlageNaam == "Standen week 47.pdf");
        verzending.IsProef.Should().BeTrue();
        (await OverzichtAsync()).Should().Match<MailOverzicht>(o => o.IsProefGestuurd && !o.IsGemaild);
    }

    [Test]
    public async Task nothing_is_mailed_when_the_standings_changed_after_going_online()
    {
        await avonden.OpslaanAsync(Avond(Wk47, Speelt(annie, 4800), Speelt(kees, 4900), Speelt(riet, 4300)));

        (await MailenAsync()).IsValid.Should().BeFalse();
        mail.Verstuurd.Should().BeEmpty();
        verzendingen.Alle.Should().BeEmpty();
    }

    [Test]
    public async Task nothing_is_mailed_before_the_standings_are_online()
    {
        online = new InMemoryOnlineStandStore();

        (await MailenAsync()).IsValid.Should().BeFalse();
        mail.Verstuurd.Should().BeEmpty();
    }

    [Test]
    public async Task nothing_is_mailed_without_dads_reply_address()
    {
        await instellingen.OpslaanAsync(Standaard);

        (await MailenAsync()).IsValid.Should().BeFalse();
        (await MailenAsync(proef: true)).IsValid.Should().BeFalse();
        mail.Verstuurd.Should().BeEmpty();
    }

    [Test]
    public async Task nothing_is_mailed_while_the_server_has_no_mail_account()
    {
        mail.IsIngesteld = false;

        (await MailenAsync()).IsValid.Should().BeFalse();
        mail.Verstuurd.Should().BeEmpty();
    }

    [Test]
    public async Task a_corrected_week_goes_out_as_verbeterd()
    {
        await MailenAsync();
        await avonden.OpslaanAsync(Avond(Wk47, Speelt(annie, 4800), Speelt(kees, 4900), Speelt(riet, 4300), Speelt(bert, 5000)));
        await OnlineZettenAsync();

        (await OverzichtAsync()).Should().Match<MailOverzicht>(o => o.IsGemaild && !o.AlGemaild && o.Verbeterd && o.Onderwerp == "Standen week 47 (verbeterd)");

        await MailenAsync();

        mail.Verstuurd.Last().Onderwerp.Should().Be("Standen week 47 (verbeterd)");
        (await OverzichtAsync()).AlGemaild.Should().BeTrue();
    }

    [Test]
    public async Task the_mail_card_lists_who_gets_it_and_who_has_no_address()
    {
        var overzicht = await OverzichtAsync();

        overzicht.Should().Match<MailOverzicht>(o => o.IsIngesteld && o.Ontvangers == 2 && !o.IsGemaild && o.Onderwerp == "Standen week 47");
        overzicht.ZonderEmail.Should().Equal("Riet Mulder");
    }

    [Test]
    public async Task the_evenings_list_marks_a_mailed_evening()
    {
        await MailenAsync(proef: true);
        var handler = new AvondenOphalenHandler(avonden, instellingen, new InMemoryVoorbereidingStore(), spelers, online, verzendingen);
        (await handler.HandleAsync(new AvondenOphalen())).Value.Gemaild.Should().BeEmpty();

        await MailenAsync();

        (await handler.HandleAsync(new AvondenOphalen())).Value.Gemaild.Should().Equal(Wk47);
    }
}
