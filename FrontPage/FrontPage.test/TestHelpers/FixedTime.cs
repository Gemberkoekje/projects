namespace FrontPage.test.TestHelpers;

/// <summary>A clock that says whatever the test sets.</summary>
internal sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
