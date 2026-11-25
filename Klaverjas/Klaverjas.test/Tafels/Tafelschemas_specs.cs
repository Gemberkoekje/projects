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

    // Dad's own plans from his workbook's score-sheet tabs, as "Wij 1 Zij 1 Wij 2 Zij 2" per table: the sheet read
    // row by row. A change here would make printed plans and score sheets disagree with his.
    [TestCase(16, 1, "1 9 5 13, 2 10 6 14, 3 11 7 15, 4 12 8 16")]
    [TestCase(17, 1, "1 9 5 13, 3 10 6 14, 17 11 7 15, 4 12 8 16")]
    [TestCase(20, 1, "14 17 12 7, 1 18 11 16, 2 5 15 20, 6 9 4 19, 10 13 3 8")]
    [TestCase(24, 2, "16 3 19 24, 14 4 21 11, 12 7 13 23, 9 6 18 15, 8 1 17 22, 2 10 5 20")]
    [TestCase(12, 1, "1 5 12 9, 7 10 4 3, 2 8 6 11")]
    public void the_plans_are_dads_own(int aantal, int ronde, string tafels)
    {
        Tekst(Tafelschemas.Voor(aantal), ronde).Should().Be(tafels);
    }

    // Slips in his workbook, repaired with the one seat that was missing in that round.
    [TestCase(8, 1, "1 6 8 5, 7 3 2 4")]
    [TestCase(22, 2, "22 13 11 20, 10 15 4 17, 1 14 8 19, 21 18 2 12, 3 9 7 16")]
    [TestCase(27, 0, "25 26 27 4, 5 6 7 8, 9 10 11 12, 13 14 15 16, 17 18 19 20, 21 22 23 24")]
    public void the_slips_in_his_workbook_are_repaired(int aantal, int ronde, string tafels)
    {
        Tekst(Tafelschemas.Voor(aantal), ronde).Should().Be(tafels);
    }

    [Test]
    public void a_number_without_a_tab_follows_dads_rule_the_extras_take_the_slapers_places()
    {
        // 19 = his 16-player plan with 17, 18 and 19 in the places of round 3's slapers 7, 8 and 9.
        Tekst(Tafelschemas.Voor(19), 2).Should().Be("1 18 11 14, 2 5 12 15, 3 6 19 16, 4 17 10 13");
    }

    [Test]
    public void the_first_on_the_sheet_writes_the_second_deals_and_the_third_plays()
    {
        var tafel = new Tafel { Wij1 = 5, Zij1 = 2, Wij2 = 9, Zij2 = 14 };

        Enumerable.Range(1, 5).Select(s => (tafel.Gever(s), tafel.Speler(s))).Should().Equal((2, 9), (9, 14), (14, 5), (5, 2), (2, 9));
    }

    private static string Tekst(Tafelschema schema, int ronde)
        => string.Join(", ", schema.Rondes[ronde].Tafels.Select(t => $"{t.Wij1} {t.Zij1} {t.Wij2} {t.Zij2}"));

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
