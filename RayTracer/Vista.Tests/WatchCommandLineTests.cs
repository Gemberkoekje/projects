using Vista.Cli;

namespace Vista.Tests;

/// <summary>The watch CLI (vista-plan §10 M1.8): its command line, the options file's syntax, and how the two merge.</summary>
[TestClass]
public sealed class WatchCommandLineTests
{
    private string _dir = null!;

    [TestInitialize]
    public void CreateDirectory()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vista-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void DeleteDirectory() => Directory.Delete(_dir, recursive: true);

    [TestMethod]
    public void WithoutAnOptionsFile_TheCommandLineMustMakeAPreview()
    {
        Assert.IsFalse(WatchCommandLine.TryParse(["--watch", "--recipe", "r.json", "--out", "p.png"], out _, out IReadOnlyList<string> errors));
        CollectionAssert.Contains(errors.ToArray(), "--seed is required.");
        Assert.IsTrue(WatchCommandLine.TryParse(["--watch", "--recipe", "r.json", "--seed", "4", "--out", "p.png", "--denoise"], out WatchRequest? request, out _));
        Assert.AreEqual(string.Empty, request!.OptionsPath);
        Assert.IsFalse(request.DryRun);
        Assert.IsTrue(request.Flags.Contains("--denoise"));
        Assert.IsFalse(WatchCommandLine.TryParse(["--preview", "--recipe", "r.json"], out _, out _));
        Assert.IsFalse(WatchCommandLine.TryParse(["--watch", "--recipe", "r.json", "--seed", "4", "--out", "p.png", "--bogus"], out _, out _));
    }

    [TestMethod]
    public void AnOptionsFile_MaySupplyTheRest_ButMustExist()
    {
        string options = Path.Combine(_dir, "o.txt");
        Assert.IsFalse(WatchCommandLine.TryParse(["--watch", "--options", options], out _, out IReadOnlyList<string> errors));
        StringAssert.Contains(errors.Single(), "No options file");
        File.WriteAllText(options, "--recipe r.json --seed 3 --out p.png");
        Assert.IsTrue(WatchCommandLine.TryParse(["--watch", "--options", options], out WatchRequest? request, out _));
        Assert.AreEqual(options, request!.OptionsPath);

        var problems = new List<string>();
        PreviewRequest? merged = WatchCommandLine.Merge(request, File.ReadAllText(options), problems, out _, out _);
        Assert.AreEqual(0, problems.Count, string.Join(" ", problems));
        Assert.AreEqual(3ul, merged!.Seed);
        Assert.AreEqual("r.json", merged.RecipePath);
    }

    [TestMethod]
    public void Merge_FileValuesWin_AndFlagsAdd()
    {
        Assert.IsTrue(WatchCommandLine.TryParse(["--watch", "--recipe", "r.json", "--seed", "4", "--out", "p.png", "--spp", "16"], out WatchRequest? request, out _));
        var problems = new List<string>();
        PreviewRequest? merged = WatchCommandLine.Merge(request!, "--spp 4 --denoise --denoise-device cuda\n", problems, out Dictionary<string, string> values, out HashSet<string> flags);
        Assert.AreEqual(0, problems.Count, string.Join(" ", problems));
        Assert.AreEqual(4, merged!.SamplesPerPixel);
        Assert.AreEqual(4ul, merged.Seed);
        Assert.AreEqual(Output.DenoiseDevice.Cuda, merged.Denoise!.Device);
        Assert.AreEqual("4", values["--spp"]);
        Assert.IsTrue(flags.Contains("--denoise"));
        Assert.AreEqual("16", request!.Values["--spp"], "the command line is kept as it was");

        Assert.IsNull(WatchCommandLine.Merge(request, "--dry-run", problems = [], out _, out _));
        StringAssert.Contains(problems.Single(), "--dry-run");
        Assert.IsNull(WatchCommandLine.Merge(request, "--seed 1 --seed 2", problems = [], out _, out _));
        StringAssert.Contains(problems.Single(), "more than once");
    }

    [TestMethod]
    public void Tokenize_CommentsQuotesAndLines()
    {
        var problems = new List<string>();
        CollectionAssert.AreEqual(
            new[] { "--seed", "7", "--out", "my renders/p.png", "--time", "dusk", "--camera", "a#b.json" },
            WatchCommandLine.Tokenize("# a comment\n--seed 7   # the seed\n--out \"my renders/p.png\"\r\n\t--time dusk\n--camera a#b.json\n", problems).ToArray());
        Assert.AreEqual(0, problems.Count);
        CollectionAssert.AreEqual(Array.Empty<string>(), WatchCommandLine.Tokenize("  \n# only a comment", problems).ToArray());

        CollectionAssert.AreEqual(new[] { "--out", "a", "--spp", "4" }, WatchCommandLine.Tokenize("--out \"a\n--spp 4", problems).ToArray());
        StringAssert.Contains(problems.Single(), "past the end of its line");
        problems.Clear();
        WatchCommandLine.Tokenize("--out \"a", problems);
        StringAssert.Contains(problems.Single(), "not closed");
    }
}
