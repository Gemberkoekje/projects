using AdventureEngine.Application.Agents;

namespace AdventureEngine.Tests;

public class ModerationResultParserTests
{
    [Fact]
    public void TryParse_SafePayload_ReturnsSafeResult()
    {
        var parsed = ModerationResultParser.TryParse("""{"result":"SAFE"}""", out var result);

        Assert.True(parsed);
        Assert.True(result.IsSafe);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void TryParse_UnsafePayloadWithReason_ReturnsUnsafeResult()
    {
        var parsed = ModerationResultParser.TryParse("""{"result":"UNSAFE","reason":"Graphic sexual content."}""", out var result);

        Assert.True(parsed);
        Assert.False(result.IsSafe);
        Assert.Equal("Graphic sexual content.", result.Reason);
    }

    [Fact]
    public void TryParse_EmptyOrGarbagePayload_FailsAndDefaultsToSafeResult()
    {
        var parsedEmpty = ModerationResultParser.TryParse(string.Empty, out var emptyResult);
        var parsedGarbage = ModerationResultParser.TryParse("definitely not json", out var garbageResult);

        Assert.False(parsedEmpty);
        Assert.False(parsedGarbage);
        Assert.True(emptyResult.IsSafe);
        Assert.True(garbageResult.IsSafe);
        Assert.Null(emptyResult.Reason);
        Assert.Null(garbageResult.Reason);
    }
}
