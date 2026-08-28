namespace IronFlag.Audio
{
    /// <summary>
    /// One piece of music: the menu bed, a match theme, or the cue a match ends on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Named the same way <see cref="SfxKind"/> is - <see cref="MatchTank"/> is
    /// <c>RF_Music_MatchTank.wav</c> - and rendered from <c>audio/sounds/music.scd</c>.
    /// </para>
    /// <para>
    /// There are four match themes rather than one bed because the design document's M8
    /// line is "per-vehicle music/SFX hook": what is playing tells you what you are driving.
    /// They share a key and a motif, so changing vehicle is a change of character rather
    /// than a change of track. Which one plays when two seats are driving different vehicles
    /// is <see cref="MatchMusic"/>'s problem, and it has one pair of speakers to solve it
    /// with.
    /// </para>
    /// </remarks>
    public enum MusicKind
    {
        /// <summary>No music, which is what the level editor plays.</summary>
        None = 0,

        /// <summary>The main menu's bed.</summary>
        MenuTheme = 1,

        /// <summary>The theme for driving the jeep.</summary>
        MatchJeep = 2,

        /// <summary>The theme for driving the tank.</summary>
        MatchTank = 3,

        /// <summary>The theme for driving the ASV.</summary>
        MatchAsv = 4,

        /// <summary>The theme for flying the helicopter.</summary>
        MatchHelicopter = 5,

        /// <summary>What plays over a won match.</summary>
        Victory = 6,

        /// <summary>What plays over a lost match.</summary>
        Defeat = 7,
    }
}
