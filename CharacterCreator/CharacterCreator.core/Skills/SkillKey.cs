namespace CharacterCreator.core.Skills;

/// <summary>
/// Identifies a skill in a rule book: the content's stable key, such as <c>levenskracht-b</c>
/// (DESIGN.md §4.1). Plans store keys, never VOID ids, because ids differ between VOID instances.
/// </summary>
/// <remarks>Immutable and thread-safe. The default value is <see cref="Empty"/>.</remarks>
public readonly record struct SkillKey
{
    private readonly string value;

    /// <summary>Initializes a new instance of the <see cref="SkillKey"/> class.</summary>
    /// <param name="value">The key; surrounding white space is dropped.</param>
    /// <exception cref="ArgumentException">The key is empty or white space.</exception>
    public SkillKey(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value.Trim();
    }

    /// <summary>No skill.</summary>
    public static SkillKey Empty => default;

    /// <summary>Whether this is <see cref="Empty"/>.</summary>
    public bool IsEmpty => value is null;

    /// <summary>The key's text; empty for <see cref="Empty"/>.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => value ?? string.Empty;
}
