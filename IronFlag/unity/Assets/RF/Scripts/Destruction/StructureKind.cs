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
        /// One of the two that belong to a side, the other being <see cref="Door"/>. The
        /// rest of this list is furniture both players can knock down, and a turret that
        /// fired on whoever was nearest would be a hazard rather than a defence - see
        /// <see cref="IronFlag.Destruction.AutoTurret"/>, and
        /// <see cref="IronFlag.Levels.LevelValidation"/>, which refuses a side on anything
        /// that cannot have one and refuses to leave one off anything that must.
        /// </remarks>
        Turret = 8,

        /// <summary>
        /// A short length of concrete barrier: the one destructible meant to be placed in
        /// rows.
        /// </summary>
        /// <remarks>
        /// Everything else on this list is a thing; a wall is a <em>unit of a thing</em>,
        /// five metres of it, and a fortified corner is four of them and a turret. That is
        /// the only way this format can express a wall at all - a
        /// <see cref="IronFlag.Levels.LevelStructure"/> is a point and a heading, so a
        /// barrier spanning two chosen ends would need a placement primitive the level file
        /// does not have. Segments cost nothing to add and read as construction rather than
        /// as tiling, because the piers stand at the joins - see
        /// <c>blender/assets/prop_wall.py</c>.
        /// </remarks>
        Wall = 9,

        /// <summary>
        /// A gate: a <see cref="Wall"/> segment that sinks into the ground for one side.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The second destructible that belongs to a side, and the reason
        /// <see cref="StructureTuning.BelongsToASide"/> is a method rather than a
        /// comparison. A vehicle of the owning side coming near drops the leaf into the
        /// floor and drives through; to everybody else it is a wall - see
        /// <see cref="IronFlag.Destruction.AutoDoor"/>.
        /// </para>
        /// <para>
        /// Built to a wall's dimensions down to the pier spacing, because a gate is only
        /// worth having as part of a run: on its own it is five metres of barrier with a
        /// hole in it that anybody can drive round. It is also deliberately the
        /// <em>softest</em> part of any run it is in, which is the oldest rule in
        /// fortification - the gate is where the wall is attacked.
        /// </para>
        /// </remarks>
        Door = 10,
    }
}
