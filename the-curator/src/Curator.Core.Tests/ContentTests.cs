using System.Text.Json.Nodes;
using Curator.Core.Content;

namespace Curator.Core.Tests;

public sealed class ContentTests
{
    [Fact]
    public void TestContentLoadsWithoutErrors()
    {
        var problems = TestWorld.Source().Check();

        Assert.DoesNotContain(problems, p => p.Severity == ProblemSeverity.Error);
        Assert.Equal(7, TestWorld.Content.WeekDays);
    }

    [Fact]
    public void UnknownPropertiesAreErrors()
    {
        var source = TestWorld.Source();
        source.Edit("books/lanterns.json", n => n["colour"] = "red");

        var problem = Assert.Single(source.Check(), p => p.File == "books/lanterns.json");
        Assert.Equal(ProblemSeverity.Error, problem.Severity);
        Assert.Equal("$.colour", problem.JsonPath);
    }

    [Fact]
    public void MissingRequiredPropertiesAreErrors()
    {
        var source = TestWorld.Source();
        source.Edit("books/lanterns.json", n => n.AsObject().Remove("title"));

        Assert.Contains(source.Check(), p => p.Severity == ProblemSeverity.Error && p.File == "books/lanterns.json");
    }

    [Fact]
    public void ErrorsReportTheJsonPath()
    {
        var source = TestWorld.Source();
        source.Edit("books/lanterns.json", n => n["pages"]![1]!["tags"] = new JsonArray("not-a-tag"));

        var problem = Assert.Single(source.Check(), p => p.Severity == ProblemSeverity.Error);
        Assert.Equal("$.pages[1].tags[0]", problem.JsonPath);
    }

    [Fact]
    public void MissingFilesAreErrors()
    {
        var source = TestWorld.Source();
        source.Remove("letters.json");

        Assert.Contains(source.Check(), p => p.File == "letters.json" && p.Message.Contains("missing", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadThrowsWithEveryError()
    {
        var source = TestWorld.Source();
        source.Edit("books/lanterns.json", n => n["spineTag"] = "nonsense");

        var ex = Assert.Throws<ContentLoadException>(source.Load);
        Assert.Contains(ex.Problems, p => p.JsonPath == "$.spineTag");
    }

    [Fact]
    public void FileNamesMustMatchIds()
    {
        var source = TestWorld.Source();
        source.Set("books/lamps.json", source.ReadText("books/lanterns.json"));
        source.Remove("books/lanterns.json");

        Assert.Contains(source.Check(), p => p.File == "books/lamps.json" && p.Message.Contains("doesn't match", StringComparison.Ordinal));
    }

    [Fact]
    public void ReferencesMustResolve()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/wren-hale.json", n => n["visits"]![0]!["request"]!["bookId"] = "no-such-book");
        source.Edit("books/lanterns.json", n => n["pages"]![0]!["ingredients"] = new JsonArray("unobtainium", "salt"));
        source.Edit("schedule.json", n => n["days"]![2]!["slots"] = new JsonArray("nobody"));

        var errors = source.Check().Where(p => p.Severity == ProblemSeverity.Error).Select(p => p.Message).ToList();
        Assert.Contains(errors, m => m.Contains("no-such-book", StringComparison.Ordinal));
        Assert.Contains(errors, m => m.Contains("unobtainium", StringComparison.Ordinal));
        Assert.Contains(errors, m => m.Contains("nobody", StringComparison.Ordinal));
    }

    [Fact]
    public void UnlockTargetsMustResolve()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/wren-hale.json", n => n["visits"]![0]!["questions"]![1]!["unlockedBy"] = "identifiedPage:lanterns-99");

        Assert.Contains(source.Check(), p => p.Severity == ProblemSeverity.Error && p.Message.Contains("lanterns-99", StringComparison.Ordinal));
    }

    [Fact]
    public void MarkupMustBeBalancedAndNonEmpty()
    {
        var source = TestWorld.Source();
        source.Edit("books/lanterns.json", n => n["pages"]![0]!["text"] = "Hold the cloth {~and it dries");
        source.Edit("books/lanterns.json", n => n["pages"]![1]!["text"] = "Cup the hands {??} and breathe.");

        var errors = source.Check().Where(p => p.Message.StartsWith("markup", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void BooksNeedThreeToTenPagesAndASafePage()
    {
        var source = TestWorld.Source();
        source.Edit("books/advanced-botanical.json", n =>
        {
            var pages = n["pages"]!.AsArray();
            pages.RemoveAt(0);
            pages.RemoveAt(0);
        });

        var errors = source.Check().Where(p => p.File == "books/advanced-botanical.json").Select(p => p.Message).ToList();
        Assert.Contains(errors, m => m.Contains("3-10", StringComparison.Ordinal));
        Assert.Contains(errors, m => m.Contains("danger 0", StringComparison.Ordinal));
    }

    [Fact]
    public void FourBooksNeedADangerousOutlier()
    {
        var source = TestWorld.Source();
        source.Edit("books/lanterns.json", n => n["pages"]![3]!["danger"] = 1);

        Assert.Contains(source.Check(), p => p.Message.Contains("at least 4", StringComparison.Ordinal));
    }

    [Fact]
    public void StoryVisitsNeedGoodUnhelpfulAndHarm()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/wren-hale.json", n => n["visits"]![1]!["outcomes"]!.AsObject().Remove("harm"));

        Assert.Contains(source.Check(), p => p.Severity == ProblemSeverity.Error && p.JsonPath == "$.visits[1].outcomes");
    }

    [Fact]
    public void GenericOutcomesMustCoverEveryCategoryAndCause()
    {
        var source = TestWorld.Source();
        source.Edit("outcomes_generic.json", n => n["mixedAccident"] = new JsonArray());

        Assert.Contains(source.Check(), p => p.File == "outcomes_generic.json" && p.JsonPath == "$.mixedAccident");
    }

    [Fact]
    public void FlagsMustNameAPatron()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/wren-hale.json", n => n["visits"]![0]!["questions"]![0]!["setFlags"] = new JsonArray("asked-paces"));

        Assert.Contains(source.Check(), p => p.Severity == ProblemSeverity.Error && p.Message.Contains("asked-paces", StringComparison.Ordinal));
    }

    [Fact]
    public void FlagsReadButNeverSetAreWarnings()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/wren-hale.json", n => n["visits"]![1]!["when"] = JsonNode.Parse("""{ "flagsAll": ["wren-hale:never-set"] }"""));

        var problem = Assert.Single(source.Check(), p => p.Message.Contains("never-set", StringComparison.Ordinal));
        Assert.Equal(ProblemSeverity.Warning, problem.Severity);
    }

    [Fact]
    public void StoryVisitsMustFitTheSchedule()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/wren-hale.json", n => n["visits"]![1]!["earliestDay"] = 6);

        Assert.Contains(source.Check(), p => p.Severity == ProblemSeverity.Error && p.Message.Contains("last slot", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadThoughtsUnlocksMustMatchTheQuestions()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/wren-hale.json", n => n["visits"]![0]!["readThoughts"]!["unlocks"] = new JsonArray());

        Assert.Contains(source.Check(), p => p.Severity == ProblemSeverity.Error && p.Message.Contains("wren-hale-1-paces", StringComparison.Ordinal));
    }

    [Fact]
    public void ForcedVisitsMustNameABook()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/wren-hale.json", n => n["visits"]![2]!["forced"] = true);

        Assert.Contains(source.Check(), p => p.Severity == ProblemSeverity.Error && p.JsonPath == "$.visits[2].forced");
    }

    [Fact]
    public void RepeatedPropertiesAreErrors()
    {
        var source = TestWorld.Source();
        source.Set("books/lanterns.json", source.ReadText("books/lanterns.json").Replace("\"title\":", "\"title\": \"Twice\", \"title\":", StringComparison.Ordinal));

        Assert.Contains(source.Check(), p => p.Severity == ProblemSeverity.Error && p.File == "books/lanterns.json");
    }

    [Fact]
    public void EnumNamesMustBeExact()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/wren-hale.json", n => n["visits"]![0]!["temptations"]![0]!["use"] = "ACCIDENT");

        Assert.Contains(source.Check(), p => p.Severity == ProblemSeverity.Error && p.Message.Contains("ACCIDENT", StringComparison.Ordinal));
    }

    [Fact]
    public void AnOutcomeMustSayItsChannel()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/wren-hale.json", n => n["visits"]![0]!["outcomes"]!["good"]!.AsObject().Remove("channel"));

        Assert.Contains(source.Check(), p => p.Severity == ProblemSeverity.Error && p.File == "patrons/wren-hale.json");
    }

    [Fact]
    public void RemovePageNeedsABookThatHasThePage()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/osric-penn.json", n => n["visits"]![0]!["outcomes"]!["good"]!["onReturn"] = JsonNode.Parse("""{ "removePage": "lanterns-4" }"""));
        source.Edit("patrons/wren-hale.json", n => n["visits"]![0]!["outcomes"]!["good"]!["onReturn"] = JsonNode.Parse("""{ "removePage": "common-wards-7" }"""));

        var errors = source.Check().Where(p => p.JsonPath.EndsWith("onReturn.removePage", StringComparison.Ordinal)).ToList();
        Assert.Contains(errors, p => p.File == "patrons/osric-penn.json" && p.Message.Contains("known book", StringComparison.Ordinal));
        Assert.Contains(errors, p => p.File == "patrons/wren-hale.json" && p.Message.Contains("isn't in 'lanterns'", StringComparison.Ordinal));
    }

    [Fact]
    public void AStoryPatronWithoutASlotIsAnError()
    {
        var source = TestWorld.Source();
        source.Edit("schedule.json", n =>
        {
            n["days"]![1]!["slots"] = new JsonArray("elara-voss", "filler");
            n["days"]![3]!["slots"] = new JsonArray("elara-voss", "filler");
        });

        Assert.Contains(source.Check(), p => p.Severity == ProblemSeverity.Error && p.Message.Contains("'wren-hale' has no slot", StringComparison.Ordinal));
    }

    [Fact]
    public void UnreachableTrustAndSpacingAreWarnings()
    {
        var source = TestWorld.Source();
        source.Edit("patrons/wren-hale.json", n =>
        {
            n["visits"]![1]!["when"] = JsonNode.Parse("""{ "minTrust": 3, "previousDecisionIn": ["alternative"] }""");
            n["visits"]![2]!["minDaysAfterPrevious"] = 3;
        });

        var warnings = source.Check().Where(p => p.Severity == ProblemSeverity.Warning).Select(p => p.JsonPath).ToList();
        Assert.Contains("$.visits[1].when.minTrust", warnings);
        Assert.Contains("$.visits[2].minDaysAfterPrevious", warnings);
    }

    [Fact]
    public void TheVersionChangesWithTheContent()
    {
        var source = TestWorld.Source();
        var before = source.Load().Version;
        source.Edit("books/lanterns.json", n => n["summary"] = "Different.");

        Assert.NotEqual(before, source.Load().Version);
    }

    [Fact]
    public void GreetingsReadAsStringsOrVariants()
    {
        var elara = TestWorld.Content.Patron("elara-voss");

        Assert.Single(elara.Visits[0].Greeting);
        Assert.Equal(2, elara.Visits.Single(v => v.Id == "elara-voss-2-returning").Greeting.Count);
    }

    [Fact]
    public void EnumKeyedBalanceMapsRead()
    {
        var balance = TestWorld.Content.Balance;

        Assert.Equal(7, balance.Money.FeeByRarity[Rarity.Rare]);
        Assert.Equal(0, balance.Outcomes.DefaultDelayDays[OutcomeCategory.WalkedOut]);
    }
}
