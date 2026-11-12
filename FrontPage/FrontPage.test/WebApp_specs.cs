using FrontPage.WebApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WebApp_specs;

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class The_page : IDisposable
{
    private readonly FrontPageFactory factory = new FrontPageFactory();

    [Test]
    public async Task is_served_at_the_root_with_the_security_headers()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        response.Headers.GetValues("Content-Security-Policy").Should().Equal(SecurityHeaders.ContentSecurityPolicy);
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.CacheControl!.NoCache.Should().BeTrue();
    }

    [Test]
    public async Task has_no_inline_script_or_style_the_policy_would_block()
    {
        var html = await Html();

        Regex.IsMatch(html, @"<script(?![^>]*\bsrc=)[^>]*>", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)).Should().BeFalse();
        Regex.IsMatch(html, @"<style\b|\sstyle=""|\son[a-z]+=""", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)).Should().BeFalse();
    }

    [Test]
    public async Task shows_a_status_for_exactly_the_configured_apps()
    {
        var html = await Html();
        var configured = factory.Services.GetRequiredService<IConfiguration>().GetSection("Apps").GetChildren().Select(a => a.Key);

        var onPage = Regex.Matches(html, @"data-app=""([^""]+)""", RegexOptions.None, TimeSpan.FromSeconds(1)).Select(m => m.Groups[1].Value);

        onPage.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(configured);
    }

    [Test]
    public async Task fonts_are_cached_for_good()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/fonts/inter-latin-400-normal.woff2", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.ToString().Should().Contain("immutable");
    }

    [Test]
    public async Task an_unknown_path_is_not_found()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/nope", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task healthz_answers_ok()
    {
        using var client = factory.CreateClient();

        (await client.GetStringAsync(new Uri("/healthz", UriKind.Relative))).Should().Be("ok");
    }

    public void Dispose() => factory.Dispose();

    private async Task<string> Html()
    {
        using var client = factory.CreateClient();
        return await client.GetStringAsync(new Uri("/", UriKind.Relative));
    }
}

[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class The_cluster_endpoint : IDisposable
{
    private readonly FrontPageFactory factory = new FrontPageFactory();

    [Test]
    public async Task returns_the_latest_summary_as_camel_case_json()
    {
        using var client = factory.CreateClient();
        factory.Reader.Next = Facts.Cluster() with
        {
            Nodes = [Facts.Node("gembernode-01", cpuUsed: 1, memoryUsed: 6)],
            RunningPodNamespaces = ["kluskat", "zeef"],
            Workloads = [Facts.Workload("kluskat/kluskat", 1), Facts.Workload("dungeontable/dungeontable", 0)],
            Version = GitVersion.FromFluxRevision("main@sha1:6969711fe18fc30117c18c8f371b490dec7d224c", Facts.Now),
        };
        await factory.Services.GetRequiredService<ClusterStatus>().RefreshAsync(CancellationToken.None);

        using var json = JsonDocument.Parse(await client.GetStringAsync(new Uri("/api/cluster", UriKind.Relative)));
        var root = json.RootElement;

        root.GetProperty("available").GetBoolean().Should().BeTrue();
        root.GetProperty("podsRunning").GetInt32().Should().Be(2);
        root.GetProperty("nodes")[0].GetProperty("cpuPercent").GetInt32().Should().Be(25);
        root.GetProperty("nodes")[0].GetProperty("memoryPercent").GetInt32().Should().Be(75);
        root.GetProperty("apps").GetProperty("kluskat").GetString().Should().Be("up");
        root.GetProperty("apps").GetProperty("dungeontable").GetString().Should().Be("down");
        root.GetProperty("version").GetProperty("commit").GetString().Should().Be("6969711fe18fc30117c18c8f371b490dec7d224c");
        root.GetProperty("version").GetProperty("isKnown").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task says_unavailable_when_the_cluster_has_never_answered()
    {
        factory.Reader.Next = FakeClusterFactsReader.Failure();
        using var client = factory.CreateClient();

        using var json = JsonDocument.Parse(await client.GetStringAsync(new Uri("/api/cluster", UriKind.Relative)));

        json.RootElement.GetProperty("available").GetBoolean().Should().BeFalse();
    }

    [Test]
    public async Task never_calls_the_cluster_per_request()
    {
        using var client = factory.CreateClient();
        var reads = factory.Reader.Reads;

        for (var i = 0; i < 5; i++)
        {
            await client.GetStringAsync(new Uri("/api/cluster", UriKind.Relative));
        }

        factory.Reader.Reads.Should().Be(reads);
    }

    public void Dispose() => factory.Dispose();
}
