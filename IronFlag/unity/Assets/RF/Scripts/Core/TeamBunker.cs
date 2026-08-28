using System;
using System.Collections.Generic;
using UnityEngine;
using IronFlag.Vehicles;

namespace IronFlag.Core
{
    /// <summary>
    /// Where one side's vehicles live: the hall they wait in, the shaft and lift car that
    /// carry one up, the pad the helicopter finishes at, and the roster inside.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The design document's core loop starts here - "player selects a vehicle from their
    /// bunker" - and its second step is the reason this is a place rather than a spawn
    /// point: a lift for ground vehicles and a launch pad for the helicopter, "as a
    /// deliberate pacing moment, not just a menu". Both are modelled in the bunker asset,
    /// so both are found in the model rather than guessed at from a number in this file.
    /// </para>
    /// <para>
    /// Which point a vehicle leaves from is the only thing here that branches on what the
    /// vehicle is, and it branches once: everything that flies uses the pad, everything
    /// else uses the lift. <see cref="VehicleBay"/> is what actually moves
    /// a vehicle through one of them.
    /// </para>
    /// <para>
    /// A bunker with no model behind it - which is what a test builds - falls back to two
    /// measurements taken from its own transform. That keeps a bunker useful without an
    /// asset behind it, and the fallback is deliberately crude so that a real scene missing
    /// its markers is visible rather than merely slightly wrong.
    /// </para>
    /// <para>
    /// Everything underground is optional in the same way. A bunker with no hall has no
    /// bays, so every vehicle waits at the top of the shaft instead of in a room of its own,
    /// and the game is exactly the same game: the hall is scenery for one camera. That is
    /// what lets every play-mode test that assembles a bunker out of two empty markers go on
    /// working, and it is why nothing here returns a bay without being asked whether there
    /// is one.
    /// </para>
    /// <para>
    /// Registered in the editor as well as in play, which is what <c>ExecuteAlways</c> is
    /// here for: the command-line still puts a vehicle out of a bunker without ever starting
    /// the game. A bunker has no update of its own, so there is nothing else it sets running.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [AddComponentMenu("IronFlag/Team Bunker")]
    public sealed class TeamBunker : MonoBehaviour
    {
        /// <summary>Name of the object in the bunker model that ground vehicles rise on.</summary>
        public const string LiftNodeName = "LiftPlatform";

        /// <summary>Name of the object in the bunker model the helicopter takes off from.</summary>
        public const string HelipadNodeName = "Helipad";

        /// <summary>Name prefix of a bay deck in the hall model, plus its roster index.</summary>
        public const string BayNodePrefix = "Bay";

        /// <summary>Name prefix of a bay's lamp in the hall model, plus its roster index.</summary>
        public const string LampNodePrefix = "Lamp";

        /// <summary>Name of the cap along the top of the hall's cutaway face.</summary>
        public const string SkylineNodeName = "Skyline";

        [SerializeField]
        [Tooltip("Which side this bunker belongs to.")]
        private Team team = Team.None;

        [SerializeField]
        [Tooltip("Lift platform in the bunker model. Ground vehicles rise on its origin.")]
        private Transform lift;

        [SerializeField]
        [Tooltip("Helipad in the bunker model. The helicopter lifts off from its origin.")]
        private Transform helipad;

        [SerializeField]
        [Tooltip("Fallback metres in front of the bunker, used only when the model has no lift.")]
        private float doorwayDistance = 6.0f;

        [SerializeField]
        [Tooltip("Fallback metres above the bunker, used only when the model has no helipad.")]
        private float roofHeight = 4.0f;

        [SerializeField]
        [Tooltip("The underground hall, or none when this bunker was built without one.")]
        private GameObject hall;

        [SerializeField]
        [Tooltip("Top of the hall's cutaway face; the select camera frames against it.")]
        private Transform skyline;

        [SerializeField]
        [Tooltip("The bay deck each roster slot waits on, in roster order.")]
        private Transform[] bays = Array.Empty<Transform>();

        [SerializeField]
        [Tooltip("The lamp above each bay, in roster order.")]
        private Renderer[] lamps = Array.Empty<Renderer>();

        [SerializeField]
        [Tooltip("The point light in each bay, in roster order.")]
        private Light[] glow = Array.Empty<Light>();

        [SerializeField]
        [Tooltip("The car that rides the shaft, or none when there is no hall.")]
        private BunkerLift car;

        /// <summary>Which bay is lit as the one being chosen, or -1 for none.</summary>
        private int chosen = -1;

        /// <summary>Scratch block for writing one lamp's emission without instancing it.</summary>
        private static MaterialPropertyBlock lampInk;

        /// <summary>Every bunker currently in the scene, in the order they woke up.</summary>
        private static readonly List<TeamBunker> Live = new List<TeamBunker>();

        /// <summary>Which side this bunker belongs to.</summary>
        public Team Team
        {
            get => team;
            set => team = value;
        }

        /// <summary>Heading a vehicle faces when it leaves, clockwise from world +Z.</summary>
        public float FacingYawDegrees => transform.eulerAngles.y;

        /// <summary>Where a ground vehicle stands when it has finished rising.</summary>
        public Vector3 LiftPoint
            => lift == null ? transform.position + (transform.forward * doorwayDistance) : lift.position;

        /// <summary>Where the helicopter sits before it takes off.</summary>
        public Vector3 HelipadPoint
            => helipad == null ? transform.position + (Vector3.up * roofHeight) : helipad.position;

        /// <summary>Whether this bunker found both of its deploy points in a model.</summary>
        public bool IsModelled => lift != null && helipad != null;

        /// <summary>Whether there is an underground hall to look at and park vehicles in.</summary>
        public bool HasHall => hall != null && bays.Length > 0;

        /// <summary>The underground hall, or <c>null</c> when this bunker has none.</summary>
        public GameObject Hall => hall;

        /// <summary>The car that rides the shaft, or <c>null</c> when there is no hall.</summary>
        public BunkerLift Car => car;

        /// <summary>How many bays the hall has; zero when there is no hall.</summary>
        public int BayCount => bays.Length;

        /// <summary>Height of the top of the hall's cutaway face, in world space.</summary>
        /// <remarks>
        /// What the select camera pins the top of its picture to. Without a hall it is the
        /// ground the bunker stands on, which is the honest answer for a bunker that has
        /// nothing underneath it to look at.
        /// </remarks>
        public float SkylineHeight => skyline == null ? transform.position.y : skyline.position.y;

        /// <summary>
        /// Returns where one roster slot's vehicle waits.
        /// </summary>
        /// <param name="slot">Roster index.</param>
        /// <returns>
        /// The middle of that bay's deck in world space, or <see cref="LiftPoint"/> when
        /// this bunker has no hall or no bay for that slot.
        /// </returns>
        /// <remarks>
        /// The origin of a bay deck is on the surface a vehicle stands on, the same way the
        /// lift and the pad are, so which bay holds what is an art decision.
        /// </remarks>
        public Vector3 BayFor(int slot)
        {
            Transform deck = BayNode(slot);
            return deck == null ? LiftPoint : deck.position;
        }

        /// <summary>
        /// Returns the bay deck one roster slot waits on.
        /// </summary>
        /// <param name="slot">Roster index.</param>
        /// <returns>The deck, or <c>null</c> when there is no bay for that slot.</returns>
        public Transform BayNode(int slot)
            => slot < 0 || slot >= bays.Length ? null : bays[slot];

        /// <summary>
        /// Returns a point in the lift shaft at a given height.
        /// </summary>
        /// <param name="height">Height in world space.</param>
        /// <returns>A point on the shaft's axis, which is the lift point's own column.</returns>
        public Vector3 ShaftPoint(float height)
        {
            Vector3 top = LiftPoint;
            return new Vector3(top.x, height, top.z);
        }

        /// <summary>
        /// Shows or hides the underground hall.
        /// </summary>
        /// <param name="visible">Whether the hall should be drawn.</param>
        /// <remarks>
        /// Only the player choosing from a bunker ever sees inside it, so only then is it
        /// drawn. The lift car is deliberately <em>not</em> part of this: it is the deck a
        /// vehicle stands on at the top of the shaft, and a collar with nothing in it is
        /// what the battlefield camera would otherwise be shown.
        /// </remarks>
        public void ShowHall(bool visible)
        {
            if (hall != null && hall.activeSelf != visible)
            {
                hall.SetActive(visible);
            }
        }

        /// <summary>
        /// Lights one bay as the one being chosen, and dims the rest.
        /// </summary>
        /// <param name="slot">Roster index to light, or -1 to dim every bay.</param>
        /// <remarks>
        /// This is where the selection lives now. A highlighted row of text says which of
        /// four names is picked; a lit room says it about the thing itself, which is the
        /// whole reason the vehicles are visible down there.
        /// </remarks>
        public void ChooseBay(int slot)
        {
            chosen = slot;

            for (int bay = 0; bay < lamps.Length; bay++)
            {
                Paint(bay, bay == slot);
            }
        }

        /// <summary>Which bay is currently lit as chosen, or -1 when none is.</summary>
        public int ChosenBay => chosen;

        /// <summary>
        /// Returns the bunker one side deploys from.
        /// </summary>
        /// <param name="side">Side to look up.</param>
        /// <returns>That side's bunker, or <c>null</c> when the scene has none.</returns>
        /// <remarks>
        /// Null is a normal answer, not a failure: a vehicle assembled in a test has no
        /// bunker to go back to, and redeploys where it started instead.
        /// </remarks>
        public static TeamBunker For(Team side)
        {
            foreach (TeamBunker bunker in Live)
            {
                if (bunker != null && bunker.team == side)
                {
                    return bunker;
                }
            }

            return null;
        }

        /// <summary>
        /// Sets which side this bunker belongs to and where its two deploy points are.
        /// </summary>
        /// <param name="side">Side this bunker belongs to.</param>
        /// <param name="liftPlatform">Lift platform from the model, or null for the fallback.</param>
        /// <param name="pad">Helipad from the model, or null for the fallback.</param>
        /// <remarks>Called by the sandbox scene builder, which finds both by name.</remarks>
        public void Configure(Team side, Transform liftPlatform, Transform pad)
        {
            team = side;
            lift = liftPlatform;
            helipad = pad;
        }

        /// <summary>
        /// Points this bunker at the base underneath it.
        /// </summary>
        /// <param name="underground">The hall, or null for a bunker with nothing below.</param>
        /// <param name="top">The cap along the top of the hall's cutaway face.</param>
        /// <param name="decks">The bay decks, in roster order.</param>
        /// <param name="lights">The lamp renderers, in roster order.</param>
        /// <param name="points">The point lights, in roster order.</param>
        /// <param name="liftCar">The car that rides the shaft.</param>
        /// <remarks>Called by <see cref="IronFlag.Levels.LevelBuilder"/>, which finds them all by name.</remarks>
        public void ConfigureBase(
            GameObject underground,
            Transform top,
            Transform[] decks,
            Renderer[] lights,
            Light[] points,
            BunkerLift liftCar)
        {
            hall = underground;
            skyline = top;
            bays = decks == null ? Array.Empty<Transform>() : decks;
            lamps = lights == null ? Array.Empty<Renderer>() : lights;
            glow = points == null ? Array.Empty<Light>() : points;
            car = liftCar;
            ChooseBay(-1);
        }

        /// <summary>
        /// Returns where one vehicle appears when it is deployed.
        /// </summary>
        /// <param name="kind">Vehicle being deployed.</param>
        /// <returns>The deploy position in world space.</returns>
        /// <remarks>
        /// The helicopter leaves from the roof and everything else from the lift, which is
        /// the whole of the difference between a launch pad and a lift as far as any code
        /// is concerned. The pacing beat that plays out between here and being drivable
        /// belongs to <see cref="VehicleBay"/>.
        /// </remarks>
        public Vector3 DeployPointFor(VehicleKind kind)
            => kind == VehicleKind.Helicopter ? HelipadPoint : LiftPoint;

        /// <summary>
        /// Sets one bay's lamp and light to the chosen or the resting brightness.
        /// </summary>
        /// <param name="slot">Roster index of the bay.</param>
        /// <param name="lit">Whether this is the bay being chosen.</param>
        /// <remarks>
        /// Through a <see cref="MaterialPropertyBlock"/> rather than by touching
        /// <c>Renderer.material</c>, which would clone the shared asset per bay and leak one
        /// material per bunker per scene load. One block, reused, is also why this is a
        /// static field: eight lamps do not need eight of them.
        /// </remarks>
        private void Paint(int slot, bool lit)
        {
            if (slot < lamps.Length && lamps[slot] != null)
            {
                if (lampInk == null)
                {
                    lampInk = new MaterialPropertyBlock();
                }

                lamps[slot].GetPropertyBlock(lampInk);
                lampInk.SetColor(BunkerView.EmissionProperty, BunkerView.LampEmission(lit));
                lamps[slot].SetPropertyBlock(lampInk);
            }

            if (slot < glow.Length && glow[slot] != null)
            {
                glow[slot].intensity = BunkerView.LampIntensity(lit);
                glow[slot].color = BunkerView.LampColour(lit, team);
            }
        }

        private void OnEnable() => Live.Add(this);

        private void OnDisable() => Live.Remove(this);
    }
}
