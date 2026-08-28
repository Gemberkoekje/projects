namespace IronFlag.Audio
{
    /// <summary>
    /// One noise the game can make: a shot, a hit, an objective changing hands, a button.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every value here names a clip on disk. The rule is mechanical - a value called
    /// <see cref="WeaponCannon"/> is <c>RF_Sfx_WeaponCannon.wav</c>, rendered from
    /// <c>audio/sounds/weapons.scd</c> - so the enum and the folder cannot drift apart
    /// without a test noticing (see <c>AudioRosterTests</c>). Adding a sound means adding a
    /// recipe and a value with the matching name, and nothing else.
    /// </para>
    /// <para>
    /// The engine loops sit in this enum rather than in one of their own because they are
    /// clips like any other; what makes them a loop is <see cref="EngineAudio"/> playing
    /// them on a source with <c>loop</c> set, which is a property of the source and never
    /// of the file - see <c>AudioImportSettings</c>.
    /// </para>
    /// </remarks>
    public enum SfxKind
    {
        /// <summary>No sound at all, which is what a thing with nothing to say plays.</summary>
        None = 0,

        /// <summary>The jeep's grenade launcher going off.</summary>
        WeaponGrenade = 1,

        /// <summary>The tank's main gun.</summary>
        WeaponCannon = 2,

        /// <summary>The helicopter's rockets leaving the rail.</summary>
        WeaponRocket = 3,

        /// <summary>The ASV's chaingun.</summary>
        WeaponChaingun = 4,

        /// <summary>An emplaced turret's autocannon.</summary>
        WeaponAutocannon = 5,

        /// <summary>Something detonating: a round landing, a hull going up.</summary>
        Explosion = 6,

        /// <summary>A round striking armour without finishing it.</summary>
        Impact = 7,

        /// <summary>A structure taking enough damage to crack.</summary>
        StructureDamaged = 8,

        /// <summary>A structure collapsing.</summary>
        StructureDestroyed = 9,

        /// <summary>A flag being lifted off its tower or off the ground.</summary>
        FlagPickup = 10,

        /// <summary>A carried flag hitting the dirt.</summary>
        FlagDropped = 11,

        /// <summary>A dropped flag going back to its own tower.</summary>
        FlagReturned = 12,

        /// <summary>A flag being delivered, which is how a match is won.</summary>
        FlagCaptured = 13,

        /// <summary>The sting for the side that won.</summary>
        MatchWon = 14,

        /// <summary>The sting for the side that lost.</summary>
        MatchLost = 15,

        /// <summary>A button being pressed.</summary>
        UiClick = 16,

        /// <summary>A choice being made: a different tool, a different vehicle, a different map.</summary>
        UiSelect = 17,

        /// <summary>Leaving a panel for the one behind it.</summary>
        UiBack = 18,

        /// <summary>An action the game refused.</summary>
        UiDenied = 19,

        /// <summary>The jeep's engine, looping.</summary>
        EngineJeep = 20,

        /// <summary>The tank's engine, looping.</summary>
        EngineTank = 21,

        /// <summary>The ASV's engine, looping.</summary>
        EngineAsv = 22,

        /// <summary>The helicopter's rotors, looping.</summary>
        EngineHelicopter = 23,
    }
}
