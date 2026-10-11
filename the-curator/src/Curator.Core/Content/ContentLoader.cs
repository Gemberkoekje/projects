using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Curator.Core.Content;

/// <summary>Loads content strictly and validates it (BUILD_BRIEF §4.5, §8.3).</summary>
public static class ContentLoader
{
    /// <summary>The root files every content set has.</summary>
    public static readonly IReadOnlyList<string> RootFiles =
    [
        "balance.json", "tags.json", "questions.json", "ingredients.json", "schedule.json",
        "outcomes_generic.json", "newspaper.json", "letters.json",
    ];

    /// <summary>Loads and validates content.</summary>
    /// <param name="source">Where the files are.</param>
    /// <returns>The content, with any warnings in <see cref="ContentSet.Warnings"/>.</returns>
    /// <exception cref="ContentLoadException">The content has errors.</exception>
    public static ContentSet Load(IContentSource source)
    {
        var (parts, problems, version) = Analyze(source);
        if (parts is null || problems.Any(p => p.Severity == ProblemSeverity.Error))
        {
            throw new ContentLoadException(Sorted(problems));
        }

        return new ContentSet(parts, version, Sorted(problems));
    }

    /// <summary>Every problem in the content, errors first, without throwing.</summary>
    /// <param name="source">Where the files are.</param>
    /// <returns>The problems; empty when the content is clean.</returns>
    public static IReadOnlyList<ContentProblem> Check(IContentSource source) => Sorted(Analyze(source).Problems);

    private static IReadOnlyList<ContentProblem> Sorted(List<ContentProblem> problems) =>
        problems.OrderByDescending(p => p.Severity)
            .ThenBy(p => p.File, StringComparer.Ordinal)
            .ThenBy(p => p.JsonPath, StringComparer.Ordinal)
            .ToList();

    // The nullable result is the one place content can be absent: a file that failed to parse.
    private static (ContentParts? Parts, List<ContentProblem> Problems, string Version) Analyze(IContentSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var problems = new List<ContentProblem>();
        var texts = new SortedDictionary<string, string>(StringComparer.Ordinal);

        var balance = Parse<Balance>(source, "balance.json", problems, texts);
        var tags = Parse<TagsFile>(source, "tags.json", problems, texts);
        var questions = Parse<QuestionsFile>(source, "questions.json", problems, texts);
        var ingredients = Parse<IngredientsFile>(source, "ingredients.json", problems, texts);
        var schedule = Parse<ScheduleFile>(source, "schedule.json", problems, texts);
        var generic = Parse<OutcomesGenericFile>(source, "outcomes_generic.json", problems, texts);
        var newspaper = Parse<NewspaperFile>(source, "newspaper.json", problems, texts);
        var letters = Parse<LettersFile>(source, "letters.json", problems, texts);

        var fileById = new Dictionary<string, string>(StringComparer.Ordinal);
        var books = ParseFolder<Book>(source, "books", problems, texts, fileById, b => b.Id);
        var patrons = ParseFolder<Patron>(source, "patrons", problems, texts, fileById, p => p.Id);

        var version = Hash(texts);
        if (balance is null || tags is null || questions is null || ingredients is null || schedule is null ||
            generic is null || newspaper is null || letters is null)
        {
            return (null, problems, version);
        }

        var parts = new ContentParts
        {
            Balance = balance,
            Tags = tags,
            Questions = questions,
            Ingredients = ingredients,
            Schedule = schedule,
            OutcomesGeneric = generic,
            Newspaper = newspaper,
            Letters = letters,
            Books = books,
            Patrons = patrons,
            FileById = fileById,
        };
        problems.AddRange(ContentValidator.Validate(parts));
        return (parts, problems, version);
    }

    private static List<T> ParseFolder<T>(
        IContentSource source,
        string folder,
        List<ContentProblem> problems,
        SortedDictionary<string, string> texts,
        Dictionary<string, string> fileById,
        Func<T, string> id)
        where T : class
    {
        var items = new List<T>();
        foreach (var path in source.ListJsonFiles(folder))
        {
            var item = Parse<T>(source, path, problems, texts);
            if (item is null)
            {
                continue;
            }

            items.Add(item);
            fileById.TryAdd(id(item), path);
        }

        return items;
    }

    private static T? Parse<T>(IContentSource source, string path, List<ContentProblem> problems, SortedDictionary<string, string> texts)
        where T : class
    {
        if (!source.Exists(path))
        {
            problems.Add(new ContentProblem(ProblemSeverity.Error, path, "$", "file is missing"));
            return null;
        }

        var text = source.ReadText(path);
        texts[path] = text;
        try
        {
            var value = JsonSerializer.Deserialize<T>(text, ContentJson.Options);
            if (value is null)
            {
                problems.Add(new ContentProblem(ProblemSeverity.Error, path, "$", "file is null"));
            }

            return value;
        }
        catch (JsonException ex)
        {
            problems.Add(new ContentProblem(ProblemSeverity.Error, path, ex.Path ?? "$", ex.Message));
            return null;
        }
    }

    private static string Hash(SortedDictionary<string, string> texts)
    {
        var builder = new StringBuilder();
        foreach (var (path, text) in texts)
        {
            builder.Append(path).Append('\n').Append(text.Replace("\r\n", "\n", StringComparison.Ordinal)).Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexStringLower(hash)[..16];
    }
}
