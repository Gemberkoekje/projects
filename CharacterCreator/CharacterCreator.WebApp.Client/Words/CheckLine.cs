using CharacterCreator.core.Rules;

namespace CharacterCreator.WebApp.Client.Words;

/// <summary>Which line of "Klopt je personage?" a finding belongs to (UX-REVIEW.md P6).</summary>
public enum CheckKind
{
    /// <summary>Not a line.</summary>
    None = 0,

    /// <summary>The XP: not more than there is, and for a new character not too much left over.</summary>
    Budget,

    /// <summary>Every skill's prerequisites.</summary>
    Prerequisites,

    /// <summary>Every other rule: maxima, extra skills at creation, retired skills, teachers.</summary>
    Rules,
}

/// <summary>One line of the checklist: ✓ with what is right, or ⚠ with the first thing that isn't.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record CheckLine
{
    /// <summary>Which line.</summary>
    required public CheckKind Kind { get; init; }

    /// <summary>The line's words.</summary>
    required public string Text { get; init; }

    /// <summary>The findings behind a ⚠, first the one the words name; empty for ✓.</summary>
    required public IReadOnlyList<Finding> Findings { get; init; }

    /// <summary>Whether the line is ✓.</summary>
    public bool IsOk => Findings.Count == 0;

    /// <summary>How many more there are than the one named.</summary>
    public int More => Math.Max(Findings.Count - 1, 0);
}
