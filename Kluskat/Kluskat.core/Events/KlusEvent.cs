namespace Kluskat.core.Events;

/// <summary>
/// Base of every domain event on a <see cref="Klus"/>. There is one user, so unlike werkplaats there
/// is no "who": only when.
/// </summary>
/// <remarks>
/// The stream is kept forever and is what phase 2 (XP, achievements, the weekly streak) is computed
/// from, so an event is only ever added, never changed; <c>KlusEvent_schema_guard_specs</c> holds
/// that line.
/// </remarks>
public abstract record KlusEvent
{
    /// <summary>When the event happened (UTC).</summary>
    public DateTime When { get; init; } = Clock.UtcNow();
}

/// <summary>A klus joined the list.</summary>
public sealed record KlusToegevoegd : KlusEvent
{
    /// <summary>Where in the house, e.g. "Keuken".</summary>
    public string Ruimte { get; init; } = string.Empty;

    /// <summary>What to do, e.g. "Stofzuigen".</summary>
    public string Naam { get; init; } = string.Empty;

    /// <summary>How many days the klus stays away after it is done.</summary>
    public int IntervalDagen { get; init; }

    /// <summary>Roughly how long it takes.</summary>
    public Grootte Grootte { get; init; }

    /// <summary>
    /// When it was last done (UTC). For a new klus this is chosen, not observed: the start-up seed
    /// staggers it so the list trickles in instead of arriving all at once.
    /// </summary>
    public DateTime LaatstGedaan { get; init; }
}

/// <summary>The descriptive fields of a klus changed on the beheer page.</summary>
public sealed record KlusAangepast : KlusEvent
{
    /// <summary>Where in the house.</summary>
    public string Ruimte { get; init; } = string.Empty;

    /// <summary>What to do.</summary>
    public string Naam { get; init; } = string.Empty;

    /// <summary>How many days the klus stays away after it is done.</summary>
    public int IntervalDagen { get; init; }

    /// <summary>Roughly how long it takes.</summary>
    public Grootte Grootte { get; init; }
}

/// <summary>The klus was done.</summary>
public sealed record KlusGedaan : KlusEvent
{
    /// <summary>When it was done (UTC). Equals <see cref="KlusEvent.When"/> unless entered on beheer.</summary>
    public DateTime GedaanOp { get; init; }

    /// <summary>
    /// Whether this was filled in afterwards on the beheer page rather than ticked off on the klus
    /// screen. Phase 2 hands out XP only for the latter.
    /// </summary>
    public bool ViaBeheer { get; init; }
}

/// <summary>A mis-tapped "Gedaan" was taken back.</summary>
public sealed record KlusGedaanTeruggedraaid : KlusEvent
{
    /// <summary>The last-done moment the klus returns to (UTC).</summary>
    public DateTime LaatstGedaan { get; init; }
}

/// <summary>The klus was on screen and the user asked for another one.</summary>
public sealed record KlusOvergeslagen : KlusEvent
{
    /// <summary>Which of the two skip buttons.</summary>
    public OverslaanReden Reden { get; init; }
}

/// <summary>The klus left the list for good.</summary>
public sealed record KlusGeschrapt : KlusEvent;
