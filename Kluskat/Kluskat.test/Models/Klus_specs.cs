using static Kluskat.test.TestHelpers.Fixtures;

namespace Kluskat.test.Models;

public sealed class Klus_specs
{
    [Test]
    public void a_klus_needs_a_name_a_room_an_interval_and_a_size()
    {
        var result = Klus.Toevoegen(new Klus.Data(), Nu);

        result.IsValid.Should().BeFalse();
        result.Messages.Should().HaveCount(4);
    }

    [Test]
    public void gedaan_moves_the_klus_and_can_be_taken_back_once()
    {
        using var klok = Clock.SetTimeForCurrentContext(() => Nu);
        var vorige = Nu.AddDays(-10);
        var gedaan = NieuweKlus(Data(), vorige).Gedaan(Nu, viaBeheer: false);

        gedaan.IsValid.Should().BeTrue();
        gedaan.Value.LaatstGedaan.Should().Be(Nu);

        var teruggedraaid = gedaan.Value.Terugdraaien();
        teruggedraaid.IsValid.Should().BeTrue();
        teruggedraaid.Value.LaatstGedaan.Should().Be(vorige);
        teruggedraaid.Value.Terugdraaien().IsValid.Should().BeFalse(because: "a mis-tap is taken back exactly once");
    }

    [Test]
    public void a_date_filled_in_afterwards_cannot_move_the_klus_backwards()
    {
        using var klok = Clock.SetTimeForCurrentContext(() => Nu);
        var klus = NieuweKlus(Data(), Nu.AddDays(-2));

        klus.Gedaan(Nu.AddDays(-5), viaBeheer: true).IsValid.Should().BeFalse();
    }

    [Test]
    public void a_klus_cannot_be_done_in_the_future()
    {
        using var klok = Clock.SetTimeForCurrentContext(() => Nu);
        var klus = NieuweKlus(Data(), Nu.AddDays(-2));

        klus.Gedaan(Nu.AddDays(1), viaBeheer: true).IsValid.Should().BeFalse();
    }

    [Test]
    public void a_geschrapte_klus_refuses_everything_but_schrappen()
    {
        using var klok = Clock.SetTimeForCurrentContext(() => Nu);
        var klus = NieuweKlus(Data(), Nu.AddDays(-2)).Schrappen().Value;

        klus.Gedaan(Nu, viaBeheer: false).IsValid.Should().BeFalse();
        klus.Overslaan(OverslaanReden.GeenZin).IsValid.Should().BeFalse();
        klus.Aanpassen(Data("Anders")).IsValid.Should().BeFalse();
        klus.Schrappen().IsValid.Should().BeTrue();
    }

    [Test]
    public void an_edit_without_changes_records_no_event()
    {
        var klus = NieuweKlus(Data(), Nu);

        klus.Aanpassen(Data()).IsValid.Should().BeTrue();

        klus.Buffer.Uncommitted.Should().ContainSingle(because: "only KlusToegevoegd is buffered");
    }
}
