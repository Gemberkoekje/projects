namespace CharacterCreator.core.Skills;

/// <summary>What an <see cref="EnergyGrant"/> adds to.</summary>
public enum EnergyKind
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>Energy per day of a type (VOID's <c>manatype</c> and <c>mana_amount</c>).</summary>
    Energy,

    /// <summary>The imbue cap of a type (VOID's <c>imbue_type</c> and <c>imbue_cap</c>).</summary>
    ImbueCap,
}

/// <summary>What one purchase of a skill adds: energy per day, or imbue cap, of one type.</summary>
/// <remarks>Immutable and thread-safe.</remarks>
public sealed record EnergyGrant
{
    /// <summary>Energy or imbue cap.</summary>
    required public EnergyKind Kind { get; init; }

    /// <summary>The type as VOID names it, such as <c>Elemental</c> or <c>Willpower</c>.</summary>
    required public string Type { get; init; }

    /// <summary>How much one purchase adds.</summary>
    required public int Amount { get; init; }
}
