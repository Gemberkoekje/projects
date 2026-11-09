namespace DatumPrikker.test.Models;

public sealed class PollResults_specs
{
    [Test]
    public void dates_rank_by_ja_then_misschien_then_earliest()
    {
        var poll = Fixtures.Poll(Fixtures.Evening(12), Fixtures.Evening(13), Fixtures.Evening(14));
        var answers = new[]
        {
            Fixtures.Answer("Arie", "arie@example.com", poll, Availability.Yes, Availability.Yes, Availability.Maybe),
            Fixtures.Answer("Bea", "bea@example.com", poll, Availability.No, Availability.Yes, Availability.Yes),
            Fixtures.Answer("Carla", "carla@example.com", poll, Availability.Maybe, Availability.Maybe, Availability.Yes),
        };

        var results = PollResults.Tally(poll, answers);

        results.Ranked.Select(tally => tally.Option.Date.Day).Should().Equal(13, 14, 12);
        results.Options.Select(tally => tally.Option.Date.Day).Should().Equal(12, 13, 14);
        results.Ranked[0].Should().BeEquivalentTo(new { Yes = 2, Maybe = 1, No = 0 });
        results.Best.Should().HaveCount(2, "13 and 14 both have two ja and one misschien");
    }

    [Test]
    public void without_any_ja_there_is_no_best_date()
    {
        var poll = Fixtures.Poll();

        PollResults.Tally(poll, [Fixtures.Answer("Arie", "arie@example.com", poll, Availability.Maybe)]).Best.Should().BeEmpty();
        PollResults.Tally(poll, []).Best.Should().BeEmpty();
    }

    [Test]
    public void answers_to_other_polls_are_ignored_and_the_rest_sorted_by_name()
    {
        var poll = Fixtures.Poll();
        var other = Fixtures.Poll();

        Answer[] given =
        [
            Fixtures.Answer("bea", "bea@example.com", poll, Availability.Yes),
            Fixtures.Answer("Arie", "arie@example.com", poll, Availability.Yes),
            Fixtures.Answer("Zoë", "zoe@example.com", other, Availability.Yes),
        ];

        var results = PollResults.Tally(poll, given);

        results.Answers.Select(answer => answer.Name).Should().Equal("Arie", "bea");
    }
}
