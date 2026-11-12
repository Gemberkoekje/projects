namespace GitVersion_specs;

public class From_a_Flux_revision
{
    [Test]
    public void takes_the_sha1_commit_after_the_branch()
    {
        var version = GitVersion.FromFluxRevision("main@sha1:6969711fe18fc30117c18c8f371b490dec7d224c", Facts.Now);

        version.Should().BeEquivalentTo(new { Commit = "6969711fe18fc30117c18c8f371b490dec7d224c", FetchedAt = Facts.Now, IsKnown = true });
    }

    [Test]
    public void takes_a_sha256_commit()
    {
        var commit = new string('b', 64);

        GitVersion.FromFluxRevision("main@sha256:" + commit, Facts.Now).Commit.Should().Be(commit);
    }

    [TestCase("")]
    [TestCase("main")]
    [TestCase("main@sha1:not-a-hash")]
    [TestCase("main@sha1:6969711")]
    public void is_unknown_without_a_full_commit_hash(string revision)
    {
        GitVersion.FromFluxRevision(revision, Facts.Now).Should().BeSameAs(GitVersion.Unknown);
    }
}

public class WorkloadRef_from_the_configuration
{
    [Test]
    public void is_namespace_slash_name()
    {
        WorkloadRef.Parse("ebi-cs-api/ebi-cs-frontend").Should().BeEquivalentTo(new { Namespace = "ebi-cs-api", Name = "ebi-cs-frontend" });
    }

    [TestCase("")]
    [TestCase("kluskat")]
    [TestCase("kluskat/")]
    [TestCase("/kluskat")]
    [TestCase("a/b/c")]
    public void fails_loudly_on_anything_else(string value)
    {
        var parse = () => WorkloadRef.Parse(value);

        parse.Should().Throw<FormatException>();
    }
}
