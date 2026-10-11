namespace Curator.Core.Content;

/// <summary>What comes of a visit, and how and when the curator hears of it (CONTENT_GUIDE §6).</summary>
public sealed record Outcome
{
    /// <summary>A silent outcome with no effects: the default for declines and walk-outs.</summary>
    public static readonly Outcome Silent = new() { Channel = OutcomeChannel.None };

    /// <summary>How it surfaces; <see cref="OutcomeChannel.None"/> means never. Always written, so silence is a choice.</summary>
    public required OutcomeChannel Channel { get; init; }

    /// <summary>Days after resolution until it surfaces; the category default when absent.</summary>
    /// <remarks>Nullable because "absent" (use the balance) differs from an explicit 0.</remarks>
    public int? DelayDays { get; init; }

    /// <summary>Newspaper only, at most 8 words.</summary>
    public string Headline { get; init; } = "";

    /// <summary>Letter only: the sender.</summary>
    public string From { get; init; } = "";

    public string Text { get; init; } = "";

    /// <summary>Trust change for this patron when it surfaces.</summary>
    public int Trust { get; init; }

    /// <summary>Reputation change when it surfaces; public channels have a default when absent.</summary>
    /// <remarks>Nullable because "absent" (use the public-channel default) differs from an explicit 0.</remarks>
    public int? Reputation { get; init; }

    /// <summary>Flags set when the outcome is resolved, at lending.</summary>
    public IReadOnlyList<string> SetFlags { get; init; } = [];

    /// <summary>False keeps the book away for good.</summary>
    public bool BookReturns { get; init; } = true;

    /// <summary>Replaces the loan's due date.</summary>
    /// <remarks>Nullable because "absent" keeps the visit's loan days.</remarks>
    public int? ReturnInDays { get; init; }

    public OnReturn OnReturn { get; init; } = OnReturn.Nothing;

    /// <summary>Overrides only: the category this outcome counts as.</summary>
    public OutcomeCategory Category { get; init; }
}
