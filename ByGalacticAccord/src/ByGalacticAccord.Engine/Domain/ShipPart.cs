using System;
using System.Collections.Generic;
using System.Linq;

namespace ByGalacticAccord.Engine.Domain;

/// <summary>Where a part goes in a ship (design §6.1): every ship has a frame and a drive, and a module or none.</summary>
public enum PartSlot
{
    Frame,
    Drive,
    Module,
}

/// <summary>The Slice 1 part kinds (design §6.1, §15.4).</summary>
public enum PartKind
{
    LightFrame,
    BulkFrame,
    CourierFrame,
    StandardDrive,
    FastDrive,
    RefrigerationUnit,
    ArmourPlating,
    CargoPod,
}

/// <summary>
/// A part's grade (design §6.1): A as designed, B a step down, C barely to spec. Grade scales the part's main stat
/// (<see cref="ShipPart.StatScale"/>) and its value (<see cref="ShipPart.ValueShare"/>).
/// </summary>
public enum Grade
{
    A,
    B,
    C,
}

/// <summary>Which of a part's stats its grade scales (design §6.1: "Grade scales the part's main stat").</summary>
public enum MainStat
{
    Cargo,
    Power,
    Preservation,
    Toughness,
}

/// <summary>
/// One part kind's stats as designed, at grade A (design §15.4). A frame gives cargo, mass and toughness; a drive power
/// and reliability, and runs hot when pushed if it is standard; a module changes cargo and mass and adds one specialty:
/// refrigeration (perishables decay at a fifth of the rate, <see cref="Preservation"/> 0.8), armour (toughness ×1.6) or
/// more cargo.
/// </summary>
public sealed record PartInfo(
    PartKind Kind,
    PartSlot Slot,
    decimal Cargo,
    decimal Mass,
    decimal Toughness,
    decimal Power,
    decimal Reliability,
    decimal Preservation,
    bool RunsHot,
    Credits BasePrice,
    MainStat Main);

/// <summary>A part fitted in a ship, or wanted for one: its kind and grade.</summary>
public readonly record struct FittedPart(PartKind Kind, Grade Grade)
{
    public PartInfo Info => ShipPart.Info(Kind);

    public PartSlot Slot => Info.Slot;

    /// <summary>The good this part is when it is traded, carried or stored.</summary>
    public GoodType Good => ShipPart.GoodOf(Kind, Grade);

    public override string ToString() => $"{Kind} {Grade}";
}

/// <summary>
/// The part table (design §15.4) and the mapping between part goods and their kind and grade. First-pass values from the
/// design; grade steps and value shares are tuning (design A.2).
/// </summary>
public static class ShipPart
{
    /// <summary>What grade B and C do to a part's main stat (design §15.4: 0.96 and 0.92).</summary>
    public static decimal StatScale(Grade grade) => grade switch
    {
        Grade.A => 1m,
        Grade.B => 0.96m,
        _ => 0.92m,
    };

    /// <summary>
    /// What a part of a grade is worth against grade A: B 85%, C 70%. The design leaves the figure open (A.2); it decides how
    /// much a substitute stings, and is the discount a lenient build's substitution is priced down by.
    /// </summary>
    public static decimal ValueShare(Grade grade) => grade switch
    {
        Grade.A => 1m,
        Grade.B => 0.85m,
        _ => 0.70m,
    };

    private static readonly Dictionary<PartKind, PartInfo> Table = new()
    {
        [PartKind.LightFrame] = new(PartKind.LightFrame, PartSlot.Frame, Cargo: 120m, Mass: 1.0m, Toughness: 1.0m, Power: 0m, Reliability: 0m, Preservation: 0m, RunsHot: false, Credits.Of(3_000m), MainStat.Cargo),
        [PartKind.BulkFrame] = new(PartKind.BulkFrame, PartSlot.Frame, Cargo: 500m, Mass: 1.7m, Toughness: 0.85m, Power: 0m, Reliability: 0m, Preservation: 0m, RunsHot: false, Credits.Of(12_000m), MainStat.Cargo),
        [PartKind.CourierFrame] = new(PartKind.CourierFrame, PartSlot.Frame, Cargo: 45m, Mass: 0.78m, Toughness: 0.7m, Power: 0m, Reliability: 0m, Preservation: 0m, RunsHot: false, Credits.Of(3_500m), MainStat.Cargo),
        [PartKind.StandardDrive] = new(PartKind.StandardDrive, PartSlot.Drive, Cargo: 0m, Mass: 0m, Toughness: 0m, Power: 1.0m, Reliability: 1.0m, Preservation: 0m, RunsHot: true, Credits.Of(3_000m), MainStat.Power),
        [PartKind.FastDrive] = new(PartKind.FastDrive, PartSlot.Drive, Cargo: 0m, Mass: 0m, Toughness: 0m, Power: 1.4m, Reliability: 0.9m, Preservation: 0m, RunsHot: false, Credits.Of(6_000m), MainStat.Power),
        [PartKind.RefrigerationUnit] = new(PartKind.RefrigerationUnit, PartSlot.Module, Cargo: -10m, Mass: 0.05m, Toughness: 0m, Power: 0m, Reliability: 0m, Preservation: 0.8m, RunsHot: false, Credits.Of(5_000m), MainStat.Preservation),
        [PartKind.ArmourPlating] = new(PartKind.ArmourPlating, PartSlot.Module, Cargo: -15m, Mass: 0.1m, Toughness: 1.6m, Power: 0m, Reliability: 0m, Preservation: 0m, RunsHot: false, Credits.Of(8_000m), MainStat.Toughness),
        [PartKind.CargoPod] = new(PartKind.CargoPod, PartSlot.Module, Cargo: 40m, Mass: 0.15m, Toughness: 0m, Power: 0m, Reliability: 0m, Preservation: 0m, RunsHot: false, Credits.Of(2_500m), MainStat.Cargo),
    };

    public static PartInfo Info(PartKind kind) => Table[kind];

    /// <summary>Every part kind, frames, then drives, then modules.</summary>
    public static IReadOnlyList<PartKind> Kinds { get; } = Enum.GetValues<PartKind>();

    /// <summary>The kinds that go in a slot.</summary>
    public static IReadOnlyList<PartKind> KindsFor(PartSlot slot) => Kinds.Where(k => Info(k).Slot == slot).ToArray();

    /// <summary>Every part good: each kind at grade A, B and C.</summary>
    public static IReadOnlyList<GoodType> Goods { get; } = Enum.GetValues<GoodType>().Where(g => g >= GoodType.LightFrameA).ToArray();

    /// <summary>The good a part of this kind and grade is.</summary>
    public static GoodType GoodOf(PartKind kind, Grade grade) => GoodType.LightFrameA + ((int)kind * 3) + (int)grade;

    /// <summary>Whether a good is a ship part.</summary>
    public static bool IsPart(GoodType good) => good >= GoodType.LightFrameA;

    /// <summary>The kind and grade of a part good.</summary>
    public static (PartKind Kind, Grade Grade) Of(GoodType good)
    {
        if (!IsPart(good))
        {
            throw new ArgumentException($"{good} is not a ship part.", nameof(good));
        }

        int index = good - GoodType.LightFrameA;
        return ((PartKind)(index / 3), (Grade)(index % 3));
    }

    /// <summary>The part a good is, or null for an ordinary good.</summary>
    public static FittedPart? PartOf(GoodType good) => IsPart(good) ? new FittedPart(Of(good).Kind, Of(good).Grade) : null;

    /// <summary>How many grade steps <paramref name="fitted"/> falls short of <paramref name="wanted"/>; zero or less is to spec.</summary>
    public static int StepsShort(Grade wanted, Grade fitted) => (int)fitted - (int)wanted;
}
