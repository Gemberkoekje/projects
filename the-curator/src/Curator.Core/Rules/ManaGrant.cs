namespace Curator.Core.Rules;

/// <summary>A morning's mana, broken down.</summary>
/// <param name="Total">The new pool.</param>
/// <param name="Base">The base allowance, or the schedule's fixed grant.</param>
/// <param name="AttentiveBonus">The part of the bonus from investigated visits.</param>
/// <param name="GoodBonus">The part of the bonus from good outcomes.</param>
/// <param name="Rollover">Mana carried over.</param>
/// <param name="Fixed">Whether the schedule fixed the grant.</param>
public sealed record ManaGrant(int Total, int Base, int AttentiveBonus, int GoodBonus, int Rollover, bool Fixed);
