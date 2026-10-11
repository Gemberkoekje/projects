using Curator.Core.Commands;
using Curator.Core.Content;
using Curator.Core.Game;

namespace Curator.Core.Tests;

/// <summary>The small test-content world in TestContent/ and ways to start games in it.</summary>
internal static class TestWorld
{
    private static readonly Lazy<ContentSet> Loaded = new(() => ContentLoader.Load(new DirectoryContentSource(ContentRoot)));

    public static string ContentRoot => Path.Combine(AppContext.BaseDirectory, "TestContent");

    public static ContentSet Content => Loaded.Value;

    public static InMemoryContentSource Source() => InMemoryContentSource.FromDirectory(ContentRoot);

    public static GameSession NewGame(long seed = 1) => NewGame(Content, seed, debug: false);

    public static GameSession NewDebugGame(long seed = 1) => NewGame(Content, seed, debug: true);

    public static GameSession NewGame(ContentSet content, long seed, bool debug)
    {
        var session = new GameSession(content, debug);
        session.Do(new NewGame(seed));
        return session;
    }
}
