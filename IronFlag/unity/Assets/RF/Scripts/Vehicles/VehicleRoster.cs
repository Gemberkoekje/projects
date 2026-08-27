namespace IronFlag.Vehicles
{
    /// <summary>
    /// The four vehicles a side is built out of, in roster order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One list rather than five. The prefab builder builds one of each, the level format
    /// carries a count of each, a bunker hands out one of each and the panel in front of the
    /// player lists them in this order - and all of those used to write the same four names
    /// in the same order, or could not see the editor's copy at all because it lives in an
    /// assembly the game cannot reference.
    /// </para>
    /// <para>
    /// <see cref="VehicleKind.None"/> is deliberately not on it: it is what an unconfigured
    /// controller says, not a vehicle anybody builds, counts or drives.
    /// </para>
    /// </remarks>
    public static class VehicleRoster
    {
        /// <summary>
        /// The vehicles of the core roster, fastest and lightest first.
        /// </summary>
        /// <returns>Every <see cref="VehicleKind"/> except <see cref="VehicleKind.None"/>.</returns>
        public static readonly VehicleKind[] Kinds =
        {
            VehicleKind.Jeep,
            VehicleKind.Tank,
            VehicleKind.Asv,
            VehicleKind.Helicopter,
        };
    }
}
