using System;

namespace IronFlag.Destruction
{
    /// <summary>
    /// The destructible things on the map, one per row of the asset spec.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Most of these are scenery a level file scatters. <see cref="FlagTower"/> is not: it is
    /// placed as an objective rather than as a prop, so it never appears in a level's
    /// structure list and is deliberately absent from
    /// <see cref="StructureTuning.Roster"/>. It is in this enum because it is shot at like
    /// everything else here and needs the same numbers.
    /// </para>
    /// <para>
    /// The tower was not destructible until the rules changed: an intact tower now hides
    /// whether it holds the flag, breaking one is the only way to find out, and a jeep may
    /// only take a flag off a tower that has been broken open. Shooting the objective is
    /// therefore the way to the objective rather than a way to delete it - see
    /// <see cref="IronFlag.Objective.FlagTower"/>. The bunker is still the one structure on
    /// the map that cannot be touched.
    /// </para>
    /// </remarks>
    [Serializable]
    public enum StructureKind
    {
        /// <summary>Not a destructible, which is what an unconfigured component reads.</summary>
        None = 0,

        /// <summary>A tree: the cheapest cover in the game, and the first thing anybody shoots.</summary>
        Tree = 1,

        /// <summary>The smaller of the two buildings.</summary>
        BuildingA = 2,

        /// <summary>The larger of the two buildings.</summary>
        BuildingB = 3,

        /// <summary>A bridge, which is a route rather than cover.</summary>
        Bridge = 4,

        /// <summary>A fuel depot.</summary>
        DepotFuel = 5,

        /// <summary>An ammunition depot.</summary>
        DepotAmmo = 6,

        /// <summary>
        /// A flag tower, real or decoy: the one destructible that is also the objective.
        /// </summary>
        FlagTower = 7,

        /// <summary>
        /// An automated turret: the one destructible that shoots back.
        /// </summary>
        /// <remarks>
        /// Also the one that belongs to a side. Everything else on this list is furniture
        /// both players can knock down, and a turret that fired on whoever was nearest
        /// would be a hazard rather than a defence - see
        /// <see cref="IronFlag.Destruction.AutoTurret"/>, and
        /// <see cref="IronFlag.Levels.LevelValidation"/>, which refuses a turret with no
        /// side and refuses a side on anything else.
        /// </remarks>
        Turret = 8,
    }
}
