using Klaverjas.core.Tafels;

namespace Klaverjas.test.Tafels;

public sealed class Tafelschemas_specs
{
    private static IEnumerable<int> Aantallen() => Tafelschemas.Aantallen;

    [Test]
    public void there_is_a_plan_for_4_to_28_players_except_7()
    {
        Tafelschemas.Aantallen.Should().Equal(Enumerable.Range(4, 25).Where(n => n != 7));
    }

    [TestCaseSource(nameof(Aantallen))]
    public void every_round_seats_everyone_once_and_the_rest_sit_out(int aantal)
    {
        var schema = Tafelschemas.Voor(aantal);

        schema.Rondes.Should().HaveCount(3);
        foreach (var ronde in schema.Rondes)
        {
            var stoelen = ronde.Tafels.SelectMany(t => t.Stoelen).ToList();
            ronde.Tafels.Should().HaveCount(aantal / 4);
            stoelen.Concat(ronde.Stil).Should().BeEquivalentTo(Enumerable.Range(1, aantal));
            ronde.Stil.Should().HaveCount(aantal % 4);
        }
    }

    [TestCaseSource(nameof(Aantallen))]
    public void the_slapers_are_the_lowest_numbers_and_each_sits_out_once(int aantal)
    {
        var schema = Tafelschemas.Voor(aantal);

        schema.Rondes.SelectMany(r => r.Stil).Should().Equal(Enumerable.Range(1, schema.Slapers));
        Enumerable.Range(1, schema.Slapers).Select(schema.RondeStil).Should().Equal(Enumerable.Range(1, schema.Slapers).Select(n => ((n - 1) / schema.StilPerRonde) + 1));
    }

    [TestCaseSource(nameof(Aantallen))]
    public void nobody_gets_the_same_partner_twice(int aantal)
    {
        var maten = Tafelschemas.Voor(aantal).Rondes.SelectMany(r => r.Tafels).SelectMany(t => new[] { Paar(t.Wij1, t.Wij2), Paar(t.Zij1, t.Zij2) }).ToList();

        maten.Should().OnlyHaveUniqueItems();
    }

    [TestCaseSource(nameof(Aantallen))]
    public void with_four_tables_or_more_nobody_sits_with_the_same_people_twice(int aantal)
    {
        var schema = Tafelschemas.Voor(aantal);
        var paren = schema.Rondes.SelectMany(r => r.Tafels).SelectMany(t => Paren(t.Stoelen)).ToList();

        schema.NooitTweeKeerSamen.Should().Be(aantal >= 16);
        if (aantal >= 16)
        {
            paren.Should().OnlyHaveUniqueItems();
        }
    }

    [TestCase(4, 0, "4&2-3&1")]
    [TestCase(17, 0, "4&3-6&16 14&8-10&12 13&7-9&15 17&11-5&2")]
    [TestCase(17, 2, "12&11-9&4 1&14-17&13 15&5-16&8 7&10-2&6")]
    [TestCase(22, 1, "20&2-16&17 9&14-10&18 19&15-5&21 7&6-12&8 11&13-22&1")]
    public void the_same_number_of_players_always_gives_the_same_plan_as_the_mock(int aantal, int ronde, string tafels)
    {
        // Pinned to the approved mock's plans (same seeded search). A change here would make printed
        // plans and score sheets disagree with ones printed before.
        var ronde1 = Tafelschemas.Voor(aantal).Rondes[ronde].Tafels.Select(t => $"{t.Wij1}&{t.Wij2}-{t.Zij1}&{t.Zij2}");

        string.Join(" ", ronde1).Should().Be(tafels);
    }

    [Test]
    public void hand_1_is_dealt_by_wij_1_and_played_by_the_player_to_the_left()
    {
        var tafel = new Tafel { Wij1 = 5, Wij2 = 9, Zij1 = 2, Zij2 = 14 };

        Enumerable.Range(1, 5).Select(s => (tafel.Gever(s), tafel.Speler(s))).Should().Equal((5, 2), (2, 9), (9, 14), (14, 5), (5, 2));
    }

    private static (int, int) Paar(int a, int b) => a < b ? (a, b) : (b, a);

    private static IEnumerable<(int, int)> Paren(IReadOnlyList<int> stoelen)
    {
        for (var i = 0; i < stoelen.Count; i++)
        {
            for (var j = i + 1; j < stoelen.Count; j++)
            {
                yield return Paar(stoelen[i], stoelen[j]);
            }
        }
    }
}
