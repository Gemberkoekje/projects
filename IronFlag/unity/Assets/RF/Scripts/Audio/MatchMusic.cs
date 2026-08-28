using System.Collections.Generic;
using UnityEngine;
using IronFlag.Core;
using IronFlag.Objective;
using IronFlag.Players;
using IronFlag.Vehicles;

namespace IronFlag.Audio
{
    /// <summary>
    /// What the match sounds like: which theme is playing, and the cue it ends on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The policy half of the music. <see cref="MusicPlayer"/> knows how to fade between two
    /// clips and nothing else; this knows what a match is and picks. Split apart because the
    /// main menu needs the first without any of the second.
    /// </para>
    /// <para>
    /// <strong>The theme follows the first seat that is out.</strong> Four match themes and
    /// one pair of speakers is the same shape of problem as four cameras and one
    /// <see cref="AudioListener"/>, and it gets the same answer: seat one already holds the
    /// ear, so seat one also holds the soundtrack, and the second seat is heard rather than
    /// listened to. When seat one is back at its bunker choosing a vehicle, seat two's ride
    /// picks the theme; when neither is out, whatever was playing keeps playing, because
    /// being blown up should not also change the music.
    /// </para>
    /// <para>
    /// <strong>The end cue is about the room, not the side.</strong> Two players sharing a
    /// screen cannot be played a fanfare and a lament at once, and one of them did just win.
    /// So a result with any local player on the winning side is a victory - which is always
    /// true of a two-player match, and correctly false of the one-player game, where the only
    /// human can genuinely lose.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Match Music")]
    public sealed class MatchMusic : MonoBehaviour
    {
        /// <summary>The theme a match opens on before anybody has deployed.</summary>
        /// <remarks>
        /// The jeep's. Every side starts with eight of them and the flag can only be carried
        /// by one, so it is the ride the game is about - see <see cref="FlagRules"/>.
        /// </remarks>
        public const MusicKind Opening = MusicKind.MatchJeep;

        private LocalMultiplayer session;
        private Match match;
        private bool ended;
        private bool won;

        /// <summary>
        /// Returns the music a match should be playing right now.
        /// </summary>
        /// <returns>The theme, or the end cue once the match is over.</returns>
        /// <remarks>
        /// Read every frame and handed straight to <see cref="MusicPlayer.Play"/>, which
        /// ignores being told what it is already playing. That is what keeps this a rule
        /// about the current state rather than a state machine with its own transitions to
        /// get wrong.
        /// </remarks>
        public MusicKind Wanted()
        {
            if (match != null && match.IsOver)
            {
                return Settled() ? MusicKind.Victory : MusicKind.Defeat;
            }

            VehicleKind driving = Driving();
            if (driving != VehicleKind.None)
            {
                return AudioRoster.ThemeOf(driving);
            }

            // Nobody is out, so hold the theme somebody was last driving to - being blown
            // up should not also change the music. Only a theme, though: the menu bed and the
            // two end cues belong to a moment rather than to a vehicle, and holding one of
            // those is how a rematch ends up playing the last match's fanfare.
            MusicPlayer player = MusicPlayer.Current;
            MusicKind holding = player == null ? MusicKind.None : player.Playing;
            return AudioRoster.IsMatchTheme(holding) ? holding : Opening;
        }

        /// <summary>
        /// Returns whether the side that won is a side somebody here was playing, working it
        /// out once and remembering the answer.
        /// </summary>
        /// <returns><c>true</c> when a seated player won.</returns>
        /// <remarks>
        /// Cached because the result cannot change and the question is not cheap:
        /// <see cref="PlayerVehicleDriver.Team"/> reads the paint off a roster of four
        /// vehicles, and this is asked from <c>Update</c>. A match is over for as long as the
        /// players leave it up.
        /// </remarks>
        private bool Settled()
        {
            if (!ended)
            {
                ended = true;
                won = match != null && WonHere(match.Winner);
            }

            return won;
        }

        /// <summary>
        /// Returns what the seat that owns the soundtrack is driving.
        /// </summary>
        /// <returns>
        /// The first seated player's vehicle, falling back through the other seats, or
        /// <see cref="VehicleKind.None"/> when nobody is on the field.
        /// </returns>
        private VehicleKind Driving()
        {
            if (session == null)
            {
                return VehicleKind.None;
            }

            IReadOnlyList<PlayerVehicleDriver> players = session.Players;
            foreach (PlayerVehicleDriver player in players)
            {
                VehicleController vehicle = player == null ? null : player.ActiveVehicle;
                if (vehicle != null && vehicle.Kind != VehicleKind.None)
                {
                    return vehicle.Kind;
                }
            }

            return VehicleKind.None;
        }

        /// <summary>
        /// Returns whether anybody at this machine is on the winning side.
        /// </summary>
        /// <param name="winner">The side that won.</param>
        /// <returns><c>true</c> when a seated player won.</returns>
        /// <remarks>
        /// A match with no seats at all - which is what a still photograph and most of the
        /// test suite is - counts as won. There is nobody there to be disappointed, and a
        /// victory cue is the better default for a scene being photographed.
        /// </remarks>
        private bool WonHere(Team winner)
        {
            if (session == null || winner == Team.None)
            {
                return true;
            }

            IReadOnlyList<PlayerVehicleDriver> players = session.Players;
            if (players.Count == 0)
            {
                return true;
            }

            foreach (PlayerVehicleDriver player in players)
            {
                if (player != null && player.Team == winner)
                {
                    return true;
                }
            }

            return false;
        }

        private void Awake()
        {
            session = GetComponent<LocalMultiplayer>();
            match = GetComponent<Match>();
        }

        private void Update()
        {
            MusicPlayer player = MusicPlayer.Current;
            if (player == null)
            {
                return;
            }

            // A match that is not over has not been won by anybody, whatever this thought a
            // moment ago. Nothing in a match calls Match.Restart today, but a rematch button
            // is the obvious thing to add next to the pause panel, and a latched result would
            // survive it - leaving the second match playing the first one's fanfare.
            if (match != null && !match.IsOver)
            {
                ended = false;
                won = false;
            }

            // The sting first, once, on the frame the match ends: the fanfare underneath it
            // takes most of a second to fade up, and the sound of having won should land on
            // the moment rather than after it. Settled() is what latches the result, so the
            // sting and the cue can never disagree about who won.
            bool announce = !ended && match != null && match.IsOver;
            if (announce)
            {
                Sfx.Play(Settled() ? SfxKind.MatchWon : SfxKind.MatchLost);
            }

            player.Play(Wanted());
        }
    }
}
