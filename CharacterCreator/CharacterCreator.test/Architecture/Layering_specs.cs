using System.Reflection;

namespace CharacterCreator.test.Architecture;

/// <summary>
/// Guards the shape of the solution (DESIGN.md §6): references only point down, the rules engine in
/// core stays pure, and the browser's client sees nothing but core.
/// </summary>
public sealed class Layering_specs
{
    private static readonly Assembly Core = Load("CharacterCreator.core");
    private static readonly Assembly Application = Load("CharacterCreator.application");
    private static readonly Assembly Infrastructure = Load("CharacterCreator.infrastructure");
    private static readonly Assembly Client = Load("CharacterCreator.WebApp.Client");

    [Test]
    public void core_references_no_other_layer()
        => OwnReferences(Core).Should().BeEmpty("core is the leaf");

    [Test]
    public void the_application_references_only_core()
        => OwnReferences(Application).Should().BeSubsetOf(["CharacterCreator.core"]);

    [Test]
    public void infrastructure_never_references_the_host()
        => OwnReferences(Infrastructure).Should().NotContain(name => name.StartsWith("CharacterCreator.WebApp", StringComparison.Ordinal));

    [Test]
    public void the_browser_client_references_only_core()
        => OwnReferences(Client).Should().BeSubsetOf(["CharacterCreator.core"], "the client is downloaded by every visitor; it carries the rules engine and nothing of the server");

    [Test]
    public void only_infrastructure_touches_storage()
    {
        foreach (var assembly in new[] { Core, Application, Client })
        {
            References(assembly).Should().NotContain(name => IsStorage(name), $"{assembly.GetName().Name} must stay storage-free; that is what the ports are for");
        }
    }

    [Test]
    public void core_touches_no_io_framework()
        => References(Core).Should().NotContain(name => IsIo(name), "the rules engine runs in the browser too; it receives everything it needs");

    private static Assembly Load(string name) => Assembly.Load(new AssemblyName(name));

    private static string[] References(Assembly assembly)
        => [.. assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty)];

    private static string[] OwnReferences(Assembly assembly)
        => [.. References(assembly).Where(name => name.StartsWith("CharacterCreator.", StringComparison.Ordinal))];

    private static bool IsStorage(string name)
        => name.StartsWith("Marten", StringComparison.Ordinal)
            || name.StartsWith("Npgsql", StringComparison.Ordinal)
            || name.StartsWith("JasperFx", StringComparison.Ordinal)
            || name.StartsWith("Weasel", StringComparison.Ordinal);

    private static bool IsIo(string name)
        => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.Extensions", StringComparison.Ordinal)
            || name.StartsWith("System.Net", StringComparison.Ordinal)
            || name.StartsWith("System.IO.FileSystem", StringComparison.Ordinal);
}
