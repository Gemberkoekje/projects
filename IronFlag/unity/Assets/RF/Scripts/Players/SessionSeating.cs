using System.Collections.Generic;
using UnityEngine;
using IronFlag.Core;
using IronFlag.Levels;
using IronFlag.UI;
using IronFlag.Vehicles;

namespace IronFlag.Players
{
    /// <summary>
    /// Empties the seats the map that is loading has no side for, before anybody sits down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole of one-player mode. The scene is built with two seats because the
    /// scene is built once, in the editor, and the map is not chosen until the menu - so the
    /// question "how many people are playing" cannot be answered when the scene is saved. It
    /// is answered here instead, off the map itself: a side with no bunker is a side nobody
    /// can play, so its seat is taken out and the player that is left gets the whole screen.
    /// See <see cref="LevelDefinition.IsSolo"/> for why a bunker is the whole of the
    /// question.
    /// </para>
    /// <para>
    /// <strong>Nothing else in the game had to learn about this.</strong> The flag, the
    /// decoy towers, the capture and the win are the same objective a match already has,
    /// pointed at a side with nobody behind it; <see cref="IronFlag.Combat.AutoTurret"/> has
    /// no concept of a human and defends brown's towers against whoever drives at them; and
    /// <see cref="SplitScreenLayout.ViewportFor"/> already had a full-screen answer for one
    /// player. The mode is a seat that stays empty, not a second game.
    /// </para>
    /// <para>
    /// <strong>Why it destroys rather than disables.</strong> A disabled seat is still four
    /// vehicles parked on the enemy's ground, still on
    /// <see cref="IronFlag.Objective.TeamReserve"/>'s books, and still something a turret
    /// can decide to shoot at the moment anything re-enables it. There is no state here
    /// worth keeping: the seat is not coming back inside a match, and the way to get it back
    /// is to load a two-sided map.
    /// </para>
    /// <para>
    /// Runs between the level loading and the session dealing devices out - see the
    /// execution orders on <see cref="LevelLoader"/> and <see cref="LocalMultiplayer"/> -
    /// which is the one window where the map is known and nobody has been given a controller
    /// or half a screen yet.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Session Seating")]
    [RequireComponent(typeof(LocalMultiplayer))]
    [DefaultExecutionOrder(-150)]
    public sealed class SessionSeating : MonoBehaviour
    {
        /// <summary>
        /// Takes the unplayed sides out of the session.
        /// </summary>
        /// <param name="session">The session to thin out.</param>
        /// <param name="level">The map being played.</param>
        /// <returns>How many seats were emptied.</returns>
        /// <remarks>
        /// <para>
        /// Public and static so a test can ask what a level does to a session without a
        /// scene load, and so the one rule - keep the seats whose side the map plays - is
        /// written once.
        /// </para>
        /// <para>
        /// A level nobody could read, or one that plays no sides at all, leaves every seat
        /// alone. That is the safe failure: two players on a broken map is the game as it
        /// was, while an empty session is a black screen with no way out of it.
        /// </para>
        /// </remarks>
        public static int Seat(LocalMultiplayer session, LevelDefinition level)
        {
            if (session == null || level == null)
            {
                return 0;
            }

            var kept = new List<PlayerVehicleDriver>();
            var emptied = new List<PlayerVehicleDriver>();

            foreach (PlayerVehicleDriver player in session.Players)
            {
                if (player == null)
                {
                    continue;
                }

                if (level.IsPlayed(player.Team))
                {
                    kept.Add(player);
                }
                else
                {
                    emptied.Add(player);
                }
            }

            if (kept.Count == 0)
            {
                return 0;
            }

            foreach (PlayerVehicleDriver player in emptied)
            {
                Retire(player);
            }

            session.Seat(kept);
            KeepAnEar(kept);
            return emptied.Count;
        }

        /// <summary>
        /// Takes one seat out of the game: its player, its roster, its camera and its HUD.
        /// </summary>
        /// <param name="player">The seat to empty.</param>
        /// <remarks>
        /// Everything is switched off before it is destroyed, because <c>Destroy</c> only
        /// takes effect at the end of the frame and this runs during <c>Awake</c>. Without
        /// the switch-off the vehicles would still register with the bunkers, the HUD would
        /// still build itself a panel, and the camera would still draw a frame of a game
        /// nobody is playing.
        /// </remarks>
        private static void Retire(PlayerVehicleDriver player)
        {
            foreach (VehicleController vehicle in player.Roster)
            {
                Discard(vehicle == null ? null : vehicle.gameObject);
            }

            foreach (PlayerHud hud in FindObjectsByType<PlayerHud>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (hud != null && hud.Player == player)
                {
                    Discard(hud.gameObject);
                }
            }

            TopDownCameraRig rig = player.CameraRig;
            Discard(rig == null ? null : rig.gameObject);
            Discard(player.gameObject);
        }

        /// <summary>
        /// Switches something off and then destroys it.
        /// </summary>
        /// <param name="thing">The object, or <c>null</c> for nothing to do.</param>
        private static void Discard(GameObject thing)
        {
            if (thing == null)
            {
                return;
            }

            thing.SetActive(false);

            if (Application.isPlaying)
            {
                Destroy(thing);
            }
            else
            {
                DestroyImmediate(thing);
            }
        }

        /// <summary>
        /// Makes sure somebody is still listening after a seat has been taken out.
        /// </summary>
        /// <param name="kept">The seats that are still being played.</param>
        /// <remarks>
        /// <para>
        /// The scene builder gives the audio listener to the first seat, because there is
        /// only ever one pair of speakers. On a map that plays the second side and not the
        /// first - which nothing generates, but a hand-written level file is free to be -
        /// that listener has just been destroyed, and a game with none is a game with no
        /// sound and a warning per frame. Cheaper to hand the ear to whoever is left than to
        /// make the scene builder guess which seat will survive a map it has never seen.
        /// </para>
        /// <para>
        /// The surviving cameras are asked rather than the scene, deliberately. A listener
        /// destroyed a moment ago is still in the scene until the end of the frame and would
        /// answer this question yes, right up until the frame it stops existing.
        /// </para>
        /// </remarks>
        private static void KeepAnEar(List<PlayerVehicleDriver> kept)
        {
            Camera first = null;

            foreach (PlayerVehicleDriver player in kept)
            {
                TopDownCameraRig rig = player.CameraRig;
                Camera view = rig == null ? null : rig.View;
                if (view == null)
                {
                    continue;
                }

                if (view.GetComponent<AudioListener>() != null)
                {
                    return;
                }

                first = first == null ? view : first;
            }

            if (first != null)
            {
                first.gameObject.AddComponent<AudioListener>();
            }
        }

        /// <summary>
        /// Reads the map that has just loaded and seats the players it has sides for.
        /// </summary>
        private void Awake()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            int emptied = Seat(GetComponent<LocalMultiplayer>(), LevelLoader.Current);
            if (emptied > 0)
            {
                Debug.Log(
                    $"IronFlag: one-player map - {emptied} seat(s) left empty, and the player "
                    + "who is here has the whole screen.");
            }
        }
    }
}
