namespace IronFlag.Vehicles
{
    /// <summary>
    /// What each vehicle is called on screen.
    /// </summary>
    /// <remarks>
    /// Spelled out rather than derived from the enum for the one reason the enum cannot
    /// cover: the ASV is an acronym in the asset spec and a word in C#, and
    /// <c>VehicleKind.Asv.ToString()</c> would put "Asv" on the HUD. Deliberately separate
    /// from the editor's asset naming, which produces file names rather than labels.
    /// </remarks>
    public static class VehicleNames
    {
        /// <summary>
        /// Returns the name a vehicle goes by on screen.
        /// </summary>
        /// <param name="kind">Vehicle to name.</param>
        /// <returns>Its display name, or an empty string for no vehicle at all.</returns>
        public static string For(VehicleKind kind)
        {
            switch (kind)
            {
                case VehicleKind.Jeep:
                    return "Jeep";
                case VehicleKind.Tank:
                    return "Tank";
                case VehicleKind.Asv:
                    return "ASV";
                case VehicleKind.Helicopter:
                    return "Helicopter";
                default:
                    return string.Empty;
            }
        }
    }
}
