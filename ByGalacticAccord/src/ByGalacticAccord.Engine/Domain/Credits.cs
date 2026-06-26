using System;
using System.Globalization;

namespace ByGalacticAccord.Engine.Domain;

/// <summary>
/// The universal currency. A decimal-backed value type so the credit-conservation
/// invariant (§5.6) is exact: no float drift is allowed to "conjure or destroy" credits.
/// </summary>
public readonly record struct Credits(decimal Amount) : IComparable<Credits>
{
    public static readonly Credits Zero = new(0m);

    public static Credits Of(decimal amount) => new(amount);

    public bool IsNegative => Amount < 0m;

    public static Credits operator +(Credits a, Credits b) => new(a.Amount + b.Amount);

    public static Credits operator -(Credits a, Credits b) => new(a.Amount - b.Amount);

    public static Credits operator *(Credits a, decimal factor) => new(a.Amount * factor);

    public static Credits operator *(decimal factor, Credits a) => new(a.Amount * factor);

    public static bool operator >(Credits a, Credits b) => a.Amount > b.Amount;

    public static bool operator <(Credits a, Credits b) => a.Amount < b.Amount;

    public static bool operator >=(Credits a, Credits b) => a.Amount >= b.Amount;

    public static bool operator <=(Credits a, Credits b) => a.Amount <= b.Amount;

    public int CompareTo(Credits other) => Amount.CompareTo(other.Amount);

    public override string ToString() => "₡" + Amount.ToString("N0", CultureInfo.InvariantCulture);
}
