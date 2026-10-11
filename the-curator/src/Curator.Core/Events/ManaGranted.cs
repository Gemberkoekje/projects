namespace Curator.Core.Events;

/// <summary>The morning's mana: the pool is set to Total.</summary>
/// <param name="Total">The new mana pool.</param>
/// <param name="Base">The base allowance (or the schedule's fixed grant).</param>
/// <param name="AttentiveBonus">Bonus for yesterday's investigated visits.</param>
/// <param name="GoodBonus">Bonus for good outcomes surfaced since the last grant.</param>
/// <param name="Rollover">Unspent mana carried over.</param>
/// <param name="Fixed">Whether the schedule fixed the grant (day 1).</param>
public sealed record ManaGranted(int Total, int Base, int AttentiveBonus, int GoodBonus, int Rollover, bool Fixed) : GameEvent;
