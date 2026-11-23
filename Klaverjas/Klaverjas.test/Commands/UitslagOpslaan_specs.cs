using Klaverjas.application.Commands;
using static Klaverjas.test.TestHelpers.Fixtures;

namespace Klaverjas.test.Commands;

public sealed class UitslagOpslaan_specs
{
    private readonly Speler annie = Lid("Annie de Graaf");
    private readonly Speler kees = Lid("Kees Bakker");
    private readonly Speler bert = Invaller("Bert van Loon");

    private static UitslagInvoer Regel(Speler speler, Status status, string punten, string pitten = "", int ronde = 0)
        => new UitslagInvoer { SpelerId = speler.Id, Status = status, Punten = punten, Pitten = pitten, Ronde = ronde };

    private Result<List<Uitslag>> Controleer(params UitslagInvoer[] regels)
        => UitslagOpslaanHandler.Controleer(new UitslagOpslaan { Datum = Wk47, Regels = regels }, new[] { annie, kees, bert }.ToDictionary(s => s.Id));

    [Test]
    public void missing_points_name_every_player_concerned()
    {
        var result = Controleer(Regel(annie, Status.Speelt, string.Empty), Regel(kees, Status.Slaper, " ", ronde: 2));

        result.Messages.Should().ContainSingle().Which.Message.Should().Contain("Annie de Graaf, Kees Bakker");
    }

    [TestCase("4o00")]
    [TestCase("-12")]
    [TestCase("4.200")]
    [TestCase("100000")]
    public void points_must_be_a_plain_number(string punten)
    {
        Controleer(Regel(annie, Status.Speelt, punten)).IsValid.Should().BeFalse();
    }

    [Test]
    public void empty_pits_count_as_zero()
    {
        var result = Controleer(Regel(annie, Status.Speelt, "4512", "  "));

        result.Value.Should().ContainSingle().Which.Pitten.Should().Be(0);
    }

    [Test]
    public void an_absent_member_needs_no_numbers()
    {
        var result = Controleer(Regel(annie, Status.Afwezig, string.Empty));

        result.Value.Should().ContainSingle().Which.Status.Should().Be(Status.Afwezig);
    }

    [Test]
    public void a_substitute_cannot_be_absent()
    {
        Controleer(Regel(bert, Status.Afwezig, string.Empty)).IsValid.Should().BeFalse();
    }

    [Test]
    public void a_slaper_keeps_the_round_and_a_player_has_none()
    {
        var result = Controleer(Regel(annie, Status.Slaper, "4826", ronde: 2), Regel(kees, Status.Speelt, "4100", ronde: 3));

        result.Value.Select(u => (u.Status, u.Ronde, u.Punten)).Should().Equal((Status.Slaper, 2, 4826), (Status.Speelt, 0, 4100));
    }

    [Test]
    public async Task an_evening_without_play_cannot_be_saved()
    {
        var instellingen = new InMemoryInstellingenStore(Standaard with { GeenSpeelavonden = [new GeenSpeelavond { Datum = Wk47, Reden = "Feest" }] });
        var avonden = new InMemoryAvondStore();
        var handler = new UitslagOpslaanHandler(avonden, new InMemorySpelerStore(annie), instellingen, NullLogger<UitslagOpslaanHandler>.Instance);

        var result = await handler.HandleAsync(new UitslagOpslaan { Datum = Wk47, Regels = [Regel(annie, Status.Speelt, "4000")] });

        result.IsValid.Should().BeFalse();
        avonden.Alle.Should().BeEmpty();
    }

    [Test]
    public async Task a_checked_evening_is_stored()
    {
        var avonden = new InMemoryAvondStore();
        var handler = new UitslagOpslaanHandler(avonden, new InMemorySpelerStore(annie, kees), new InMemoryInstellingenStore(), NullLogger<UitslagOpslaanHandler>.Instance);

        var result = await handler.HandleAsync(new UitslagOpslaan { Datum = Wk47, Regels = [Regel(annie, Status.Speelt, "4918", "1"), Regel(kees, Status.Afwezig, string.Empty)] });

        result.IsValid.Should().BeTrue();
        avonden.Alle.Should().ContainSingle().Which.Uitslagen.Should().HaveCount(2);
    }
}
