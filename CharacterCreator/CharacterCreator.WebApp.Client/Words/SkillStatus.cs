namespace CharacterCreator.WebApp.Client.Words;

/// <summary>What a picker row's status pill stands for; each kind has its own icon and colour.</summary>
public enum StatusKind
{
    /// <summary>No pill: the skill can simply be bought.</summary>
    None = 0,

    /// <summary>✓ The plan has it.</summary>
    Chosen,

    /// <summary>⚠ It can be bought once something is fixed, such as a missing prerequisite.</summary>
    Warning,

    /// <summary>✗ A rule VOID enforces stands in the way, such as not enough XP.</summary>
    Blocked,

    /// <summary>ⓘ Something to know, such as needing a teacher.</summary>
    Info,
}

/// <summary>A picker row's status pill (UX-REVIEW.md P3): a few words, and the full story for its tooltip.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record SkillStatus
{
    /// <summary>No pill.</summary>
    public static SkillStatus None { get; } = new () { Kind = StatusKind.None, Text = string.Empty, Full = string.Empty };

    /// <summary>What it stands for.</summary>
    required public StatusKind Kind { get; init; }

    /// <summary>The pill's words, such as "vereist Harnas II".</summary>
    required public string Text { get; init; }

    /// <summary>Every reason in words, for the tooltip.</summary>
    required public string Full { get; init; }

    /// <summary>The pill's icon.</summary>
    public string Icon => Kind switch
    {
        StatusKind.Chosen => "✓",
        StatusKind.Warning => "⚠︎",
        StatusKind.Blocked => "✗",
        StatusKind.Info => "ⓘ",
        _ => string.Empty,
    };
}
