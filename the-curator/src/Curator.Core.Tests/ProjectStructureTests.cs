using System.Xml.Linq;
using Curator.Core.Game;

namespace Curator.Core.Tests;

/// <summary>Guards the architecture rules that the build alone doesn't enforce.</summary>
public sealed class ProjectStructureTests
{
    [Fact]
    public void CoreNeverReferencesGodot()
    {
        var references = typeof(Prng).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, r => r.Name is not null && r.Name.StartsWith("Godot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CoreProjectHasNoPackageOrProjectReferences()
    {
        var project = XDocument.Load(Path.Combine(RepoPaths.Root, "src", "Curator.Core", "Curator.Core.csproj"));

        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Empty(project.Descendants("ProjectReference"));
    }

    [Fact]
    public void AllProjectsShareTheGodotProjectsTargetFramework()
    {
        var godot = TargetFramework(Path.Combine("game", "Curator.Godot.csproj"));

        Assert.Equal(godot, TargetFramework(Path.Combine("src", "Curator.Core", "Curator.Core.csproj")));
        Assert.Equal(godot, TargetFramework(Path.Combine("src", "Curator.Core.Tests", "Curator.Core.Tests.csproj")));
    }

    private static string TargetFramework(string relativePath)
    {
        var project = XDocument.Load(Path.Combine(RepoPaths.Root, relativePath));
        var element = project.Descendants("TargetFramework").FirstOrDefault();

        Assert.NotNull(element);
        return element.Value.Trim();
    }
}
