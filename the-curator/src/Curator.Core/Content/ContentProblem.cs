namespace Curator.Core.Content;

/// <summary>A problem found while loading or validating content, located by file and JSON path.</summary>
/// <param name="Severity">Errors stop the game loading; warnings don't.</param>
/// <param name="File">The content file, e.g. "books/lanterns.json".</param>
/// <param name="JsonPath">Where in the file, e.g. "$.pages[2].text".</param>
/// <param name="Message">What's wrong.</param>
public sealed record ContentProblem(ProblemSeverity Severity, string File, string JsonPath, string Message)
{
    /// <inheritdoc />
    public override string ToString() =>
        $"{(Severity == ProblemSeverity.Error ? "error" : "warning")} {File} {JsonPath}: {Message}";
}
