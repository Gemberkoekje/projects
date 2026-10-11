namespace Curator.Core.Content;

/// <summary>All game content, loaded and validated; immutable at runtime (BUILD_BRIEF §4.5).</summary>
public sealed class ContentSet
{
    /// <summary>The slot id that asks for the next unused one-off visitor.</summary>
    public const string FillerSlot = "filler";

    private readonly Dictionary<string, Book> books;
    private readonly Dictionary<string, PageRef> pages;
    private readonly Dictionary<string, Patron> patrons;
    private readonly Dictionary<string, VisitRef> visits;
    private readonly Dictionary<string, Ingredient> ingredients;
    private readonly Dictionary<int, ScheduleDay> days;

    /// <summary>Assembles a content set. Use <see cref="ContentLoader"/> rather than calling this directly.</summary>
    /// <param name="parts">The parsed files.</param>
    /// <param name="version">A hash of every content file.</param>
    /// <param name="warnings">Validation warnings.</param>
    public ContentSet(ContentParts parts, string version, IReadOnlyList<ContentProblem> warnings)
    {
        ArgumentNullException.ThrowIfNull(parts);
        Balance = parts.Balance;
        Tags = parts.Tags;
        Questions = parts.Questions;
        Schedule = parts.Schedule;
        OutcomesGeneric = parts.OutcomesGeneric;
        Newspaper = parts.Newspaper;
        Letters = parts.Letters;
        Books = parts.Books.OrderBy(b => b.Id, StringComparer.Ordinal).ToList();
        Patrons = parts.Patrons.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        Fillers = Patrons.Where(p => p.Role == PatronRole.Filler)
            .OrderBy(p => p.FillerOrder)
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .ToList();
        IngredientList = parts.Ingredients.Ingredients;
        Version = version;
        Warnings = warnings;

        books = Books.ToDictionary(b => b.Id, StringComparer.Ordinal);
        pages = Books.SelectMany(b => b.Pages.Select(p => new PageRef(b, p)))
            .ToDictionary(r => r.Page.Id, StringComparer.Ordinal);
        patrons = Patrons.ToDictionary(p => p.Id, StringComparer.Ordinal);
        visits = Patrons.SelectMany(p => p.Visits.Select(v => new VisitRef(p, v)))
            .ToDictionary(r => r.Visit.Id, StringComparer.Ordinal);
        ingredients = IngredientList.ToDictionary(i => i.Id, StringComparer.Ordinal);
        days = Schedule.Days.ToDictionary(d => d.Day);
    }

    public Balance Balance { get; }

    public TagsFile Tags { get; }

    public QuestionsFile Questions { get; }

    public ScheduleFile Schedule { get; }

    public OutcomesGenericFile OutcomesGeneric { get; }

    public NewspaperFile Newspaper { get; }

    public LettersFile Letters { get; }

    /// <summary>Every book, ordered by id.</summary>
    public IReadOnlyList<Book> Books { get; }

    /// <summary>Every patron, ordered by id.</summary>
    public IReadOnlyList<Patron> Patrons { get; }

    /// <summary>One-off visitors in the order they are used.</summary>
    public IReadOnlyList<Patron> Fillers { get; }

    public IReadOnlyList<Ingredient> IngredientList { get; }

    /// <summary>A hash of every content file; saved games record it.</summary>
    public string Version { get; }

    /// <summary>Validation warnings (errors stop loading).</summary>
    public IReadOnlyList<ContentProblem> Warnings { get; }

    /// <summary>The number of days in the prototype.</summary>
    public int WeekDays => Balance.Week.Days;

    /// <summary>A book by id.</summary>
    /// <param name="id">The book id.</param>
    /// <returns>The book.</returns>
    public Book Book(string id) =>
        books.TryGetValue(id, out var book) ? book : throw new KeyNotFoundException($"No book '{id}'.");

    /// <summary>Whether a book exists.</summary>
    /// <param name="id">The book id.</param>
    /// <returns>True when it does.</returns>
    public bool HasBook(string id) => books.ContainsKey(id);

    /// <summary>A page and its book, by page id.</summary>
    /// <param name="pageId">The page id.</param>
    /// <returns>The page and book.</returns>
    public PageRef Page(string pageId) =>
        pages.TryGetValue(pageId, out var page) ? page : throw new KeyNotFoundException($"No page '{pageId}'.");

    /// <summary>Whether a page exists.</summary>
    /// <param name="pageId">The page id.</param>
    /// <returns>True when it does.</returns>
    public bool HasPage(string pageId) => pages.ContainsKey(pageId);

    /// <summary>A patron by id.</summary>
    /// <param name="id">The patron id.</param>
    /// <returns>The patron.</returns>
    public Patron Patron(string id) =>
        patrons.TryGetValue(id, out var patron) ? patron : throw new KeyNotFoundException($"No patron '{id}'.");

    /// <summary>Whether a patron exists.</summary>
    /// <param name="id">The patron id.</param>
    /// <returns>True when they do.</returns>
    public bool HasPatron(string id) => patrons.ContainsKey(id);

    /// <summary>A visit and its patron, by visit id.</summary>
    /// <param name="visitId">The visit id.</param>
    /// <returns>The visit and patron.</returns>
    public VisitRef Visit(string visitId) =>
        visits.TryGetValue(visitId, out var visit) ? visit : throw new KeyNotFoundException($"No visit '{visitId}'.");

    /// <summary>An ingredient by id.</summary>
    /// <param name="id">The ingredient id.</param>
    /// <returns>The ingredient.</returns>
    public Ingredient Ingredient(string id) =>
        ingredients.TryGetValue(id, out var ingredient) ? ingredient : throw new KeyNotFoundException($"No ingredient '{id}'.");

    /// <summary>The schedule for a day.</summary>
    /// <param name="day">The day, 1 to <see cref="WeekDays"/>.</param>
    /// <returns>Its slots and options.</returns>
    public ScheduleDay Day(int day) =>
        days.TryGetValue(day, out var scheduleDay) ? scheduleDay : throw new KeyNotFoundException($"No schedule for day {day}.");

    /// <summary>The fee for lending a book: its own fee, else the rarity fee.</summary>
    /// <param name="book">The book.</param>
    /// <returns>The fee.</returns>
    public int FeeFor(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return book.Fee ?? Balance.Money.FeeByRarity[book.Rarity];
    }
}
