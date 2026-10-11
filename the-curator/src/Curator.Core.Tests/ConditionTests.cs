using Curator.Core.Content;
using Curator.Core.Rules;

namespace Curator.Core.Tests;

public sealed class ConditionTests
{
    private static readonly ConditionFacts Facts = new(
        2,
        new HashSet<string>(StringComparer.Ordinal) { "elara-voss:asked-newt" },
        DecisionKind.Alternative,
        OutcomeCategory.Unhelpful,
        new HashSet<string>(StringComparer.Ordinal) { "hearth-and-kettle" });

    [Fact]
    public void TheEmptyConditionAlwaysHolds() => Assert.True(ConditionRules.Holds(Condition.Always, Facts));

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void MinTrust(int min, bool holds) => Assert.Equal(holds, ConditionRules.Holds(new Condition { MinTrust = min }, Facts));

    [Theory]
    [InlineData(2, true)]
    [InlineData(1, false)]
    public void MaxTrust(int max, bool holds) => Assert.Equal(holds, ConditionRules.Holds(new Condition { MaxTrust = max }, Facts));

    [Fact]
    public void FlagsAllAndFlagsNone()
    {
        Assert.True(ConditionRules.Holds(new Condition { FlagsAll = ["elara-voss:asked-newt"] }, Facts));
        Assert.False(ConditionRules.Holds(new Condition { FlagsAll = ["elara-voss:asked-newt", "elara-voss:gone"] }, Facts));
        Assert.True(ConditionRules.Holds(new Condition { FlagsNone = ["elara-voss:gone"] }, Facts));
        Assert.False(ConditionRules.Holds(new Condition { FlagsNone = ["elara-voss:asked-newt"] }, Facts));
    }

    [Fact]
    public void PreviousDecisionIn()
    {
        Assert.True(ConditionRules.Holds(new Condition { PreviousDecisionIn = [DecisionKind.Lent, DecisionKind.Alternative] }, Facts));
        Assert.False(ConditionRules.Holds(new Condition { PreviousDecisionIn = [DecisionKind.Declined] }, Facts));
    }

    [Fact]
    public void PreviousOutcomeIn()
    {
        Assert.True(ConditionRules.Holds(new Condition { PreviousOutcomeIn = [OutcomeCategory.Unhelpful] }, Facts));
        Assert.False(ConditionRules.Holds(new Condition { PreviousOutcomeIn = [OutcomeCategory.Good, OutcomeCategory.Harm] }, Facts));
    }

    [Fact]
    public void HasBorrowed()
    {
        Assert.True(ConditionRules.Holds(new Condition { HasBorrowed = "hearth-and-kettle" }, Facts));
        Assert.False(ConditionRules.Holds(new Condition { HasBorrowed = "lanterns" }, Facts));
    }

    [Fact]
    public void EveryFieldPresentMustHold() =>
        Assert.False(ConditionRules.Holds(new Condition { MinTrust = 1, HasBorrowed = "lanterns" }, Facts));

    [Fact]
    public void CardHistoryCountsAsBorrowed()
    {
        var session = TestWorld.NewGame();

        Assert.Contains("hearth-and-kettle", ConditionRules.BorrowedBooks(session.Content, session.State, "ada-marsh"));
    }

    [Fact]
    public void GreetingVariantsReactToWhatHappened()
    {
        var session = TestWorld.NewGame();
        session.ToDay(4, Play.Policy(("elara-voss-1", s => s.Do(new Commands.Decline()))));
        session.Ring("elara-voss-2-guarded");
        Assert.Equal("I won't take long.", session.Views.Visit().Greeting);
    }
}
