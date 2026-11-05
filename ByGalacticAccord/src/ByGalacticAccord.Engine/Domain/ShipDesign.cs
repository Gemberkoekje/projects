using System;
using System.Collections.Generic;
using System.Linq;

namespace ByGalacticAccord.Engine.Domain;

/// <summary>The five standard designs (design §6.2): a standard combination of parts each. Anything else is a custom build.</summary>
public enum Design
{
    LightFreighter,
    BulkHauler,
    FastCourier,
    RefrigeratedTransport,
    ArmouredFreighter,
}

/// <summary>A ship's stats, each computed from its parts (design §6.1), so each can explain itself (<see cref="ShipStats.Terms"/>).</summary>
public enum ShipStat
{
    Cargo,
    Mass,
    Power,
    Speed,
    Toughness,
    Reliability,
    Preservation,
    RunningCost,
    Value,
}

/// <summary>What one part adds to a stat: its contribution, or (for toughness) its multiplier. A null part is the yard's assembly.</summary>
public readonly record struct StatTerm(FittedPart? Part, decimal Value);

/// <summary>
/// The parts a ship is built from (design §6.1): a frame, a drive and a module or none. A build orders the kinds and the
/// grade it wants; a built ship has the grades that were fitted.
/// </summary>
public sealed record ShipParts(FittedPart Frame, FittedPart Drive, FittedPart? Module)
{
    /// <summary>The parts in slot order: frame, drive, then the module if there is one.</summary>
    public IReadOnlyList<FittedPart> All => Module is { } m ? new[] { Frame, Drive, m } : new[] { Frame, Drive };

    /// <summary>The part in a slot, if any.</summary>
    public FittedPart? In(PartSlot slot) => slot switch
    {
        PartSlot.Frame => Frame,
        PartSlot.Drive => Drive,
        _ => Module,
    };

    /// <summary>The same parts with the one in <paramref name="slot"/> replaced.</summary>
    public ShipParts With(PartSlot slot, FittedPart part) => slot switch
    {
        PartSlot.Frame => this with { Frame = part },
        PartSlot.Drive => this with { Drive = part },
        _ => this with { Module = part },
    };

    /// <summary>The standard design these kinds make, whatever the grades; null for a custom build.</summary>
    public Design? Design => Designs.Match(this);

    /// <summary>The same kinds, every part at <paramref name="grade"/>.</summary>
    public ShipParts AtGrade(Grade grade) => new(Frame with { Grade = grade }, Drive with { Grade = grade }, Module is { } m ? m with { Grade = grade } : null);

    public static ShipParts Of(PartKind frame, PartKind drive, PartKind? module, Grade grade = Grade.A) =>
        new(new FittedPart(frame, grade), new FittedPart(drive, grade), module is { } m ? new FittedPart(m, grade) : null);

    public override string ToString() => string.Join(" + ", All);
}

/// <summary>
/// A ship's stats from its parts (design §6.1, §15.4): cargo is the frame's (by grade) plus or minus the module's; mass the
/// frame's plus the module's; speed is the drive's power (by grade) over the mass; toughness the frame's, times armour's
/// (by grade); reliability the drive's; preservation the refrigeration unit's (by grade). Running costs follow the parts'
/// prices (design §6.5); the value is what the parts are worth at their grades plus the yard's assembly. Incidents come
/// more often to an unreliable drive and hurt a frail hull more (design §16.7): their modifiers are one over reliability
/// and one over toughness.
/// </summary>
public sealed record ShipStats(
    decimal CargoCapacity,
    decimal Mass,
    decimal Power,
    decimal Speed,
    decimal Toughness,
    decimal Reliability,
    decimal Preservation,
    bool RunsHot,
    Credits RunningCostPerTick,
    Credits Value)
{
    /// <summary>What a ship costs to run an hour, as a share of its parts' base prices: a Light Freighter's ₵6,000 of parts cost ₵0.18.</summary>
    public const decimal RunningShare = 0.00003m;

    /// <summary>What a yard's assembly adds to a ship's value over its parts (design §15.6: a Light Freighter's ₵6,000 of parts make an ₵8,000 ship).</summary>
    public static readonly Credits AssemblyValue = Credits.Of(2_000m);

    /// <summary>How much more often an incident comes than to a standard drive: one over reliability.</summary>
    public decimal IncidentChance => Reliability <= 0m ? 1m : Math.Round(1m / Reliability, 4);

    /// <summary>How much worse an incident's damage is than to a standard hull: one over toughness.</summary>
    public decimal IncidentSeverity => Toughness <= 0m ? 1m : Math.Round(1m / Toughness, 4);

    public static ShipStats From(ShipParts parts)
    {
        decimal cargo = Sum(Terms(parts, ShipStat.Cargo));
        decimal mass = Sum(Terms(parts, ShipStat.Mass));
        decimal power = Sum(Terms(parts, ShipStat.Power));
        decimal toughness = Terms(parts, ShipStat.Toughness).Aggregate(1m, (product, term) => product * term.Value);
        decimal preservation = Sum(Terms(parts, ShipStat.Preservation));
        return new ShipStats(
            Math.Round(cargo, 2),
            Math.Round(mass, 3),
            Math.Round(power, 3),
            mass <= 0m ? 0m : Math.Round(power / mass, 3),
            Math.Round(toughness, 3),
            parts.Drive.Info.Reliability,
            Math.Round(preservation, 3),
            parts.All.Any(p => p.Info.RunsHot),
            Credits.Of(Math.Round(Sum(Terms(parts, ShipStat.RunningCost)), 4)),
            Credits.Of(Math.Round(Sum(Terms(parts, ShipStat.Value)), 2)));
    }

    /// <summary>
    /// What each part contributes to a stat (design §6.1: every stat explains itself). Cargo, mass, power, preservation,
    /// running cost and value add up their terms; toughness multiplies them (the frame's, then armour's); speed is power over
    /// mass and reliability the drive's, so their terms are the drive's and the mass's parts.
    /// </summary>
    public static IReadOnlyList<StatTerm> Terms(ShipParts parts, ShipStat stat)
    {
        var terms = new List<StatTerm>();
        foreach (FittedPart part in parts.All)
        {
            PartInfo info = part.Info;
            decimal scale = ShipPart.StatScale(part.Grade);
            decimal value = stat switch
            {
                ShipStat.Cargo => info.Cargo * (info.Main == MainStat.Cargo ? scale : 1m),
                ShipStat.Mass => info.Mass,
                ShipStat.Power or ShipStat.Speed => info.Power * scale,
                ShipStat.Toughness => info.Toughness * (info.Main == MainStat.Toughness ? scale : 1m),
                ShipStat.Reliability => info.Reliability,
                ShipStat.Preservation => info.Preservation * scale,
                ShipStat.RunningCost => info.BasePrice.Amount * RunningShare,
                ShipStat.Value => info.BasePrice.Amount * ShipPart.ValueShare(part.Grade),
                _ => 0m,
            };

            if (value != 0m)
            {
                terms.Add(new StatTerm(part, value));
            }
        }

        if (stat == ShipStat.Value)
        {
            terms.Add(new StatTerm(null, AssemblyValue.Amount));
        }

        return terms;
    }

    private static decimal Sum(IEnumerable<StatTerm> terms) => terms.Sum(t => t.Value);
}

/// <summary>The standard designs' parts (design §6.2) and their stats (design §15.6, which follow from §15.4).</summary>
public static class Designs
{
    private static readonly Dictionary<Design, ShipParts> Table = new()
    {
        [Design.LightFreighter] = ShipParts.Of(PartKind.LightFrame, PartKind.StandardDrive, null),
        [Design.BulkHauler] = ShipParts.Of(PartKind.BulkFrame, PartKind.StandardDrive, null),
        [Design.FastCourier] = ShipParts.Of(PartKind.CourierFrame, PartKind.FastDrive, null),
        [Design.RefrigeratedTransport] = ShipParts.Of(PartKind.LightFrame, PartKind.StandardDrive, PartKind.RefrigerationUnit),
        [Design.ArmouredFreighter] = ShipParts.Of(PartKind.LightFrame, PartKind.StandardDrive, PartKind.ArmourPlating),
    };

    public static IReadOnlyList<Design> All { get; } = Enum.GetValues<Design>();

    /// <summary>A design's parts, every one at <paramref name="grade"/>.</summary>
    public static ShipParts Parts(Design design, Grade grade = Grade.A) => Table[design].AtGrade(grade);

    /// <summary>A design's stats as designed, at grade A.</summary>
    public static ShipStats Stats(Design design) => ShipStats.From(Table[design]);

    /// <summary>The design a set of part kinds makes, whatever their grades; null if none does.</summary>
    public static Design? Match(ShipParts parts)
    {
        foreach ((Design design, ShipParts standard) in Table)
        {
            if (standard.Frame.Kind == parts.Frame.Kind && standard.Drive.Kind == parts.Drive.Kind && standard.Module?.Kind == parts.Module?.Kind)
            {
                return design;
            }
        }

        return null;
    }
}
