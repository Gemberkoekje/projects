namespace Curator.Core.Content;

/// <summary>Thrown when content has errors; lists every problem found.</summary>
public sealed class ContentLoadException : Exception
{
    /// <summary>Creates the exception from the problems found.</summary>
    /// <param name="problems">All problems, errors and warnings.</param>
    public ContentLoadException(IReadOnlyList<ContentProblem> problems)
        : base(Describe(problems))
    {
        Problems = problems;
    }

    /// <summary>Creates an exception with a message only.</summary>
    public ContentLoadException()
        : this([])
    {
    }

    /// <summary>Creates an exception with a message only.</summary>
    /// <param name="message">The message.</param>
    public ContentLoadException(string message)
        : base(message)
    {
        Problems = [];
    }

    /// <summary>Creates an exception with a message and cause.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public ContentLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
        Problems = [];
    }

    /// <summary>Every problem found, errors first.</summary>
    public IReadOnlyList<ContentProblem> Problems { get; }

    private static string Describe(IReadOnlyList<ContentProblem> problems)
    {
        var errors = problems.Where(p => p.Severity == ProblemSeverity.Error).ToList();
        return $"Content has {errors.Count} error(s):{Environment.NewLine}{string.Join(Environment.NewLine, errors)}";
    }
}
