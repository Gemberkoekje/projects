namespace Curator.Core.Content;

/// <summary>The parsed content files, before they are assembled into a <see cref="ContentSet"/>.</summary>
public sealed record ContentParts
{
    public required Balance Balance { get; init; }

    public required TagsFile Tags { get; init; }

    public required QuestionsFile Questions { get; init; }

    public required IngredientsFile Ingredients { get; init; }

    public required ScheduleFile Schedule { get; init; }

    public required OutcomesGenericFile OutcomesGeneric { get; init; }

    public required NewspaperFile Newspaper { get; init; }

    public required LettersFile Letters { get; init; }

    public required IReadOnlyList<Book> Books { get; init; }

    public required IReadOnlyList<Patron> Patrons { get; init; }

    /// <summary>The file each book or patron was read from, by id.</summary>
    public IReadOnlyDictionary<string, string> FileById { get; init; } = new Dictionary<string, string>();
}
