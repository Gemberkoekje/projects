using Klaverjas.application.Commands;
using static Klaverjas.test.TestHelpers.Fixtures;

namespace Klaverjas.test.Commands;

public sealed class Wachtwoorden_specs
{
    private static InstellingenOpslaan Formulier(Instellingen huidig, string spelers, string nieuw = "", string herhaald = "")
        => new InstellingenOpslaan
        {
            Club = huidig.Club,
            PuntenAfwezig = huidig.PuntenAfwezig,
            Speeldag = huidig.Speeldag,
            EersteAvond = huidig.EersteAvond,
            Kleuren = huidig.Kleuren,
            AfzenderNaam = huidig.AfzenderNaam,
            Antwoordadres = huidig.Antwoordadres,
            Onderwerp = huidig.Onderwerp,
            Spelerswachtwoord = spelers,
            NieuwWachtwoord = nieuw,
            NieuwWachtwoordHerhaald = herhaald,
        };

    private static InstellingenOpslaanHandler Opslaan(InMemoryInstellingenStore store)
        => new InstellingenOpslaanHandler(store, NullLogger<InstellingenOpslaanHandler>.Instance);

    [Test]
    public async Task dads_new_password_is_stored_only_as_a_hash()
    {
        var store = new InMemoryInstellingenStore();

        (await Opslaan(store).HandleAsync(Formulier(Standaard, "klaverjas", "Harten-Aas", "Harten-Aas"))).IsValid.Should().BeTrue();

        store.Huidig.BeheerWachtwoord.Should().NotContain("Harten-Aas");
        Wachtwoord.Klopt("Harten-Aas", store.Huidig.BeheerWachtwoord).Should().BeTrue();
    }

    [Test]
    public async Task empty_boxes_keep_dads_password()
    {
        var hash = Wachtwoord.Maak("Harten-Aas");
        var store = new InMemoryInstellingenStore(Standaard with { BeheerWachtwoord = hash });

        (await Opslaan(store).HandleAsync(Formulier(store.Huidig, "klaverjas"))).IsValid.Should().BeTrue();

        store.Huidig.BeheerWachtwoord.Should().Be(hash);
    }

    [TestCase("Harten-Aas", "Harten-Aaz")]
    [TestCase("kort", "kort")]
    [TestCase("", "Harten-Aas")]
    public async Task a_new_password_must_be_long_enough_and_typed_twice_the_same(string nieuw, string herhaald)
    {
        var store = new InMemoryInstellingenStore();

        (await Opslaan(store).HandleAsync(Formulier(Standaard, "klaverjas", nieuw, herhaald))).IsValid.Should().BeFalse();

        store.Huidig.Should().Be(Standaard);
    }

    [Test]
    public async Task dads_password_cannot_be_the_players_password()
    {
        var store = new InMemoryInstellingenStore();

        (await Opslaan(store).HandleAsync(Formulier(Standaard, "klaverjas", "Klaverjas", "Klaverjas"))).IsValid.Should().BeFalse();
    }

    [Test]
    public async Task the_players_password_cannot_be_dads_own()
    {
        var store = new InMemoryInstellingenStore(Standaard with { BeheerWachtwoord = Wachtwoord.Maak("Harten-Aas") });

        (await Opslaan(store).HandleAsync(Formulier(store.Huidig, "Harten-Aas"))).IsValid.Should().BeFalse();

        store.Huidig.Spelerswachtwoord.Should().BeEmpty();
    }

    [Test]
    public async Task the_players_password_is_kept_without_spaces_at_the_ends()
    {
        var store = new InMemoryInstellingenStore();

        await Opslaan(store).HandleAsync(Formulier(Standaard, "  klaverjas  "));

        store.Huidig.Spelerswachtwoord.Should().Be("klaverjas");
    }

    [Test]
    public async Task clearing_the_players_password_closes_their_page()
    {
        var store = new InMemoryInstellingenStore(Standaard with { Spelerswachtwoord = "klaverjas" });

        (await Opslaan(store).HandleAsync(Formulier(store.Huidig, string.Empty))).IsValid.Should().BeTrue();

        store.Huidig.SpelerspaginaOpen.Should().BeFalse();
    }
}
