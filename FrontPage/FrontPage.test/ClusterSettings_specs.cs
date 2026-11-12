using FrontPage.WebApp;
using Microsoft.Extensions.Configuration;

namespace ClusterSettings_specs;

public class Reading_the_configuration
{
    [Test]
    public void gives_each_app_its_workloads()
    {
        var settings = ClusterSettings.Read(Configuration(new Dictionary<string, string?>()
        {
            ["Apps:ebi:0"] = "ebi-cs-api/ebi-cs-api",
            ["Apps:ebi:1"] = "ebi-cs-api/ebi-cs-frontend",
            ["Apps:kluskat:0"] = "kluskat/kluskat",
        }));

        settings.Apps.Should().BeEquivalentTo(new[]
        {
            Facts.App("ebi", "ebi-cs-api/ebi-cs-api", "ebi-cs-api/ebi-cs-frontend"),
            Facts.App("kluskat", "kluskat/kluskat"),
        });
    }

    [Test]
    public void reads_the_system_namespaces()
    {
        var settings = ClusterSettings.Read(Configuration(new Dictionary<string, string?>()
        {
            ["Apps:kluskat:0"] = "kluskat/kluskat",
            ["Cluster:SystemNamespaces:0"] = "kube-system",
            ["Cluster:SystemNamespaces:1"] = "traefik",
        }));

        settings.SystemNamespaces.Should().BeEquivalentTo("kube-system", "traefik");
    }

    [Test]
    public void fails_loudly_without_apps()
    {
        var read = () => ClusterSettings.Read(Configuration([]));

        read.Should().Throw<InvalidOperationException>().WithMessage("*Apps*");
    }

    [Test]
    public void fails_loudly_on_a_workload_that_is_not_namespace_slash_name()
    {
        var read = () => ClusterSettings.Read(Configuration(new Dictionary<string, string?>() { ["Apps:kluskat:0"] = "kluskat" }));

        read.Should().Throw<FormatException>();
    }

    [TestCase("45", 45)]
    [TestCase("0", 30)]
    [TestCase("", 30)]
    public void refreshes_every_configured_number_of_seconds_or_30(string configured, int expected)
    {
        var configuration = Configuration(configured.Length == 0 ? [] : new Dictionary<string, string?> { ["Cluster:RefreshSeconds"] = configured });

        ClusterSettings.RefreshInterval(configuration).Should().Be(TimeSpan.FromSeconds(expected));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
