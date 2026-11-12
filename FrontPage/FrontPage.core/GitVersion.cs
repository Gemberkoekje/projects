namespace FrontPage.core;

/// <summary>The gembernodes commit Flux has fetched, and when it fetched it.</summary>
public sealed record GitVersion
{
    /// <summary>Stands in for a version that could not be read.</summary>
    public static GitVersion Unknown { get; } = new GitVersion { Commit = string.Empty, FetchedAt = DateTimeOffset.MinValue };

    /// <summary>The full commit hash, or empty when unknown.</summary>
    public required string Commit { get; init; }

    /// <summary>When Flux fetched that commit.</summary>
    public required DateTimeOffset FetchedAt { get; init; }

    /// <summary>Whether a commit is known.</summary>
    public bool IsKnown => Commit.Length > 0;

    /// <summary>
    /// Reads a Flux source revision, such as <c>main@sha1:6969711fe18fc30117c18c8f371b490dec7d224c</c>.
    /// </summary>
    /// <param name="revision">The revision as Flux reports it.</param>
    /// <param name="fetchedAt">When Flux fetched it.</param>
    /// <returns>The version, or <see cref="Unknown"/> when the revision holds no commit hash.</returns>
    public static GitVersion FromFluxRevision(string revision, DateTimeOffset fetchedAt)
    {
        var commit = revision[(revision.LastIndexOf(':') + 1)..];
        return commit.Length is 40 or 64 && commit.All(char.IsAsciiHexDigitLower)
            ? new GitVersion { Commit = commit, FetchedAt = fetchedAt }
            : Unknown;
    }
}
