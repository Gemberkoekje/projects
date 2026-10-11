using Curator.Core.Content;

namespace Curator.Core.Tests;

/// <summary>BUILD_BRIEF §8.3 on the real content in game/content.</summary>
public sealed class RealContentTests
{
    [Fact]
    public void TheRealContentHasNoErrors()
    {
        var problems = ContentLoader.Check(new DirectoryContentSource(RepoPaths.GameContent));

        var errors = problems.Where(p => p.Severity == ProblemSeverity.Error).Select(p => p.ToString()).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public void TheRealContentIsTheWholeCatalogue()
    {
        var content = ContentLoader.Load(new DirectoryContentSource(RepoPaths.GameContent));

        Assert.Equal(15, content.Books.Count);
        Assert.Equal(15, content.Patrons.Count);
        Assert.Equal(10, content.Fillers.Count);
        Assert.Equal(3 + 1, content.Patrons.Count(p => p.Role == PatronRole.Story));
        Assert.True(content.Books.Count(b => b.Pages.Any(p => p.Danger >= 2)) >= ContentValidator.MinBooksWithOutliers);
        Assert.All(content.Books, b => Assert.InRange(b.Pages.Count, 3, 10));
    }

    [Fact]
    public void TheContentReportIsWritten()
    {
        var content = ContentLoader.Load(new DirectoryContentSource(RepoPaths.GameContent));

        var report = ContentReport.Render(content);
        var outDir = Path.Combine(RepoPaths.Root, "out");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "content-report.md"), report);

        Assert.All(content.Patrons.SelectMany(p => p.Visits), v => Assert.Contains($"`{v.Id}`", report, StringComparison.Ordinal));
    }

    [Fact]
    public void TheRealContentHasNoWarnings()
    {
        var problems = ContentLoader.Check(new DirectoryContentSource(RepoPaths.GameContent));

        var warnings = problems.Where(p => p.Severity == ProblemSeverity.Warning).Select(p => p.ToString()).ToList();
        Assert.True(warnings.Count == 0, string.Join(Environment.NewLine, warnings));
    }
}
