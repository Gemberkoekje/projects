namespace CharacterCreator.core.Rules;

/// <summary>
/// The XP a character earns at an event, from the total it had at the start of that event. VOID's
/// <c>App\Checkout\XpAward</c> (Vaardigheden 2026, p. 9); the shared cases keep the two equal.
/// </summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed class XpTable
{
    private readonly (decimal From, decimal Award)[] steps;

    /// <summary>Initializes a new instance of the <see cref="XpTable"/> class.</summary>
    /// <param name="steps">From which total each award applies; any order.</param>
    /// <param name="bookOfExperiences">What a book of experiences adds on top, at most once per event.</param>
    public XpTable(IEnumerable<(decimal From, decimal Award)> steps, decimal bookOfExperiences)
    {
        ArgumentNullException.ThrowIfNull(steps);

        this.steps = [.. steps.OrderByDescending(step => step.From)];
        if (this.steps.Length == 0)
        {
            throw new ArgumentException("An XP table needs at least one step.", nameof(steps));
        }

        BookOfExperiences = bookOfExperiences;
    }

    /// <summary>Vortex's table: below 20 XP 1.5, from 20 1.25, from 30 1, from 40 0.75, from 50 0.5.</summary>
    public static XpTable Vortex { get; } = new ([(50m, 0.5m), (40m, 0.75m), (30m, 1m), (20m, 1.25m), (0m, 1.5m)], 0.25m);

    /// <summary>What a book of experiences adds on top of an award, at most once per event.</summary>
    public decimal BookOfExperiences { get; }

    /// <summary>The award for an event started with <paramref name="xp"/>.</summary>
    /// <param name="xp">The total at the start of the event.</param>
    /// <returns>The XP the event adds.</returns>
    public decimal AwardFor(decimal xp)
    {
        foreach (var (from, award) in steps)
        {
            if (xp >= from)
            {
                return award;
            }
        }

        return steps[^1].Award;
    }

    /// <summary>The total after <paramref name="events"/> events, each awarding from the total before it.</summary>
    /// <param name="xp">The total now.</param>
    /// <param name="events">How many events.</param>
    /// <param name="withBook">Whether every event also brings a book of experiences.</param>
    /// <returns>The total afterwards.</returns>
    public decimal After(decimal xp, int events, bool withBook = false)
    {
        for (var i = 0; i < events; i++)
        {
            xp += AwardFor(xp) + (withBook ? BookOfExperiences : 0m);
        }

        return xp;
    }
}
