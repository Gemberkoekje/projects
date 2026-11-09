namespace DatumPrikker.core.Models;

/// <summary>The tally of a poll: per date how many said Ja, Misschien and Nee, and who said what.</summary>
public sealed record PollResults
{
    /// <summary>The poll.</summary>
    public Poll Poll { get; init; } = new Poll();

    /// <summary>One tally per date option, in date order.</summary>
    public IReadOnlyList<OptionTally> Options { get; init; } = [];

    /// <summary>The same tallies, best first: most Ja, then most Misschien, then earliest.</summary>
    public IReadOnlyList<OptionTally> Ranked { get; init; } = [];

    /// <summary>Every answer, by name.</summary>
    public IReadOnlyList<Answer> Answers { get; init; } = [];

    /// <summary>The date options that share first place with at least one Ja; empty when nobody said Ja.</summary>
    public IReadOnlyList<OptionTally> Best
        => Ranked.Count == 0 || Ranked[0].Yes == 0
            ? []
            : [.. Ranked.TakeWhile(tally => tally.Yes == Ranked[0].Yes && tally.Maybe == Ranked[0].Maybe)];

    /// <summary>Tallies a poll's answers.</summary>
    /// <param name="poll">The poll.</param>
    /// <param name="answers">Its answers.</param>
    /// <returns>The results.</returns>
    public static PollResults Tally(Poll poll, IEnumerable<Answer> answers)
    {
        ArgumentNullException.ThrowIfNull(poll);
        ArgumentNullException.ThrowIfNull(answers);

        var byName = answers
            .Where(answer => answer.PollId == poll.Id)
            .OrderBy(answer => answer.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(answer => answer.Email, StringComparer.Ordinal)
            .ToArray();

        var tallies = poll.Options
            .Select(option => new OptionTally
            {
                Option = option,
                Yes = byName.Count(answer => answer.ChoiceFor(option.Id) == Availability.Yes),
                Maybe = byName.Count(answer => answer.ChoiceFor(option.Id) == Availability.Maybe),
                No = byName.Count(answer => answer.ChoiceFor(option.Id) == Availability.No),
            })
            .ToArray();

        return new PollResults
        {
            Poll = poll,
            Options = tallies,
            Ranked = [.. tallies
                .OrderByDescending(tally => tally.Yes)
                .ThenByDescending(tally => tally.Maybe)
                .ThenBy(tally => tally.Option.Date)
                .ThenBy(tally => tally.Option.Start)],
            Answers = byName,
        };
    }
}
