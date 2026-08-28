using System.Collections.Generic;
using UnityEngine;
using IronFlag.Audio;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Objective;
using IronFlag.Supply;
using IronFlag.Vehicles;

namespace IronFlag.Players
{
    /// <summary>
    /// One local player: the bunker they choose from, the vehicle those controls are
    /// driving, and the camera that follows it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The design document's core loop lives here. A player is in one of two places: in
    /// their bunker looking at a roster, or out on the field driving one of it. There is no
    /// third state and no way to be in both - which is the real change M4 makes, because M3
    /// had all four vehicles parked on the start line and let the pilot teleport between
    /// them. Now three of them are inside the bunker, out of sight and out of range, and
    /// getting a different one means coming home or dying.
    /// </para>
    /// <para>
    /// The roster is four <em>kinds</em> rather than four vehicles, and how many of each a
    /// side actually has is <see cref="TeamReserve"/>, which the level file stocks. A row
    /// whose reserve is empty stays on the panel and stays selectable and simply cannot be
    /// sent out - see <see cref="HasOneLeft"/> - because the panel is where a player finds
    /// out why.
    /// </para>
    /// <para>
    /// The driver owns none of its own input plumbing. <see cref="LocalMultiplayer"/> works
    /// out which devices this player holds and hands over a <see cref="PlayerControls"/>;
    /// what happens here is the translation from that into a <see cref="VehicleInput"/>,
    /// which is the only thing a vehicle ever sees.
    /// </para>
    /// <para>
    /// Aiming is where the two control schemes genuinely differ rather than just reading
    /// different hardware. A pointer names a place on the map, so it is raycast onto the
    /// vehicle's ground plane; a stick names a direction on the screen, so it is turned by
    /// the camera's fixed heading. The scheme says which of the two the aim value is, which
    /// is why nothing here inspects a device.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Player Vehicle Driver")]
    public sealed class PlayerVehicleDriver : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Camera rig that follows whichever vehicle this player is driving.")]
        private TopDownCameraRig cameraRig;

        [SerializeField]
        [Tooltip("The four vehicles waiting in this player's bunker, in roster order.")]
        private List<VehicleController> roster = new List<VehicleController>();

        [SerializeField]
        [Tooltip("Which roster entry is highlighted when the match starts.")]
        private int startIndex;

        [SerializeField]
        [Tooltip("Seconds the deploy button must be held in the field to leave it.")]
        private float recallHoldSeconds = 1.0f;

        [SerializeField]
        [Tooltip("Seconds the camera holds on the wreck before cutting to the bunker.")]
        private float wreckHoldSeconds = 1.4f;

        private readonly List<VehicleBay> bays = new List<VehicleBay>();
        private PlayerControls controls;
        private int selected;
        private int activeIndex = -1;
        private float holding;
        private float wreckHold;
        private bool recallArmed;

        /// <summary>The vehicles in this player's bunker, in roster order.</summary>
        public IReadOnlyList<VehicleController> Roster => roster;

        /// <summary>The camera rig showing this player their vehicle.</summary>
        public TopDownCameraRig CameraRig => cameraRig;

        /// <summary>The controls this player is holding, or null when they have no device.</summary>
        public PlayerControls Controls => controls;

        /// <summary>The vehicle currently being driven, or null when nothing is out.</summary>
        public VehicleController ActiveVehicle
            => activeIndex >= 0 && activeIndex < roster.Count ? roster[activeIndex] : null;

        /// <summary>
        /// Whether this player is in their bunker rather than out on the field.
        /// </summary>
        /// <remarks>
        /// True for the whole of the ride out as well, which is deliberate: a vehicle on the
        /// lift is not one anybody is driving, and a pilot who could steer it would drive it
        /// off the platform.
        /// </remarks>
        public bool AtTheBunker => ActiveVehicle == null;

        /// <summary>
        /// Whether the camera is still holding on a wreck, before the cut to the bunker.
        /// </summary>
        /// <remarks>
        /// True for a window after <see cref="AtTheBunker"/> already is, which is exactly the
        /// gap <see cref="OnReturned"/> holds the camera open for. Nothing that shows a menu
        /// or accepts a roster choice should run while this is true, or the choice it makes
        /// (and the camera cut that follows it) throws away the moment the hold exists for.
        /// </remarks>
        public bool IsHoldingOnWreck => wreckHold > 0.0f;

        /// <summary>Which roster entry the bunker panel is highlighting.</summary>
        public int Selected => selected;

        /// <summary>The highlighted vehicle, or null when the roster is empty.</summary>
        public VehicleController SelectedVehicle
            => selected >= 0 && selected < roster.Count ? roster[selected] : null;

        /// <summary>Whether a vehicle is riding out of the bunker right now.</summary>
        public bool IsDeploying
        {
            get
            {
                VehicleBay bay = BayFor(selected);
                return bay != null && bay.IsDeploying;
            }
        }

        /// <summary>Whether the highlighted vehicle could be sent out right now.</summary>
        public bool CanDeploy
        {
            get
            {
                VehicleBay bay = BayFor(selected);
                return AtTheBunker && bay != null && bay.IsReady && !bay.IsDeploying
                    && HasOneLeft(selected);
            }
        }

        /// <summary>
        /// How many of one roster entry this side still has, counting the one in the bay.
        /// </summary>
        /// <param name="index">Roster index.</param>
        /// <returns>
        /// How many are left, or <see cref="int.MaxValue"/> in a scene that keeps no
        /// reserve - which is what a test rig and a bare sandbox are.
        /// </returns>
        /// <remarks>
        /// The vehicle standing in the bay is included, because it has not been lost yet:
        /// this is the number of times this side can still lose one, which is the number a
        /// pilot deciding what to take out actually wants.
        /// </remarks>
        public int RemainingOf(int index)
        {
            VehicleController vehicle
                = index >= 0 && index < roster.Count ? roster[index] : null;

            return vehicle == null
                ? 0
                : TeamReserve.LeftFor(Team, vehicle.Kind);
        }

        /// <summary>
        /// Whether one roster entry is a vehicle this side has any of left.
        /// </summary>
        /// <param name="index">Roster index.</param>
        /// <returns><c>false</c> only when the reserve for that vehicle is empty.</returns>
        /// <remarks>
        /// The whole of what a finite roster does to the bunker screen. A vehicle nobody has
        /// any of stays on the panel and stays selectable, for the same reason one being
        /// repaired does: the answer to "why can I not take the tank" has to be somewhere a
        /// player can read it, and a row that vanished would take the answer with it.
        /// </remarks>
        public bool HasOneLeft(int index) => RemainingOf(index) > 0;

        /// <summary>Seconds the deploy button has to be held to leave the field.</summary>
        public float RecallHoldSeconds => recallHoldSeconds;

        /// <summary>
        /// Whether the deploy button has been let go of since this vehicle came out.
        /// </summary>
        /// <remarks>
        /// One button sends a vehicle out and takes one off the field, and the ride out
        /// lasts longer than the hold does. Without this, holding the button a moment too
        /// long at the bunker would blow up the vehicle it had just chosen.
        /// </remarks>
        public bool CanLeaveTheField => recallArmed;

        /// <summary>How far through leaving the field the player's held button is, in 0..1.</summary>
        public float RecallProgress
            => recallHoldSeconds <= 0.0f ? 0.0f : Mathf.Clamp01(holding / recallHoldSeconds);

        /// <summary>
        /// Whether the vehicle being driven is standing on its own side's bunker.
        /// </summary>
        /// <remarks>
        /// The difference between parking a vehicle and blowing it up. Being home is asked
        /// of the supply network rather than of the bunker's geometry, so "close enough to
        /// swap vehicles" and "close enough to refuel" are the same place by construction -
        /// a player who can see their fuel gauge climbing knows they can also change their
        /// mind.
        /// </remarks>
        public bool IsHome
        {
            get
            {
                VehicleController vehicle = ActiveVehicle;
                if (vehicle == null)
                {
                    return false;
                }

                return SupplyPoint.HomeFor(
                    vehicle.transform.position, Team, vehicle is Helicopter) != null;
            }
        }

        /// <summary>Which side this player is on, as painted on their roster.</summary>
        public Team Team
        {
            get
            {
                foreach (VehicleController vehicle in roster)
                {
                    var paint = vehicle == null ? null : vehicle.GetComponent<VehicleTeamPaint>();
                    if (paint != null && paint.Team != Team.None)
                    {
                        return paint.Team;
                    }
                }

                return Team.None;
            }
        }

        /// <summary>
        /// Points this driver at its camera and its roster.
        /// </summary>
        /// <param name="rig">Camera rig to follow the driven vehicle with.</param>
        /// <param name="vehicles">The vehicles waiting in this player's bunker.</param>
        /// <remarks>
        /// Called by the sandbox scene builder. The controls arrive separately, at runtime,
        /// from <see cref="LocalMultiplayer"/> - which devices exist is not something a
        /// saved scene can know.
        /// </remarks>
        public void Configure(TopDownCameraRig rig, IEnumerable<VehicleController> vehicles)
        {
            cameraRig = rig;
            roster = new List<VehicleController>();
            if (vehicles != null)
            {
                roster.AddRange(vehicles);
            }
        }

        /// <summary>
        /// Hands this player the controls they are holding.
        /// </summary>
        /// <param name="held">
        /// The player's own controls, or null when they have no device - in which case the
        /// vehicle they were driving lets go and coasts to a stop.
        /// </param>
        public void UseControls(PlayerControls held)
        {
            controls = held;
            if (held == null && ActiveVehicle != null)
            {
                ActiveVehicle.ReleaseControls();
            }
        }

        /// <summary>
        /// Returns the bunker bay one roster entry waits in.
        /// </summary>
        /// <param name="index">Roster index.</param>
        /// <returns>That vehicle's bay, or null when it is outside the roster.</returns>
        public VehicleBay BayFor(int index)
        {
            if (bays.Count != roster.Count)
            {
                // Something asked before OnEnable ran - the editor's command-line still,
                // most likely, which builds a scene and photographs it without ever
                // starting the game.
                WatchRoster();
            }

            return index >= 0 && index < bays.Count ? bays[index] : null;
        }

        /// <summary>
        /// Highlights one entry in the bunker's roster.
        /// </summary>
        /// <param name="index">Roster index; wraps around at either end.</param>
        /// <remarks>
        /// Anything can be highlighted, including a vehicle that is still being repaired.
        /// Skipping over those would hide the countdown the player most wants to see - "how
        /// long until I can take the tank" is a question the panel should answer rather than
        /// silently refuse.
        /// </remarks>
        public void Select(int index)
        {
            if (roster.Count == 0)
            {
                selected = 0;
                return;
            }

            selected = Wrap(index);
            ShowTheChoice();
        }

        /// <summary>
        /// Sends the highlighted vehicle out of the bunker.
        /// </summary>
        /// <returns><c>true</c> when it is on its way.</returns>
        public bool DeploySelected()
        {
            if (!CanDeploy)
            {
                return false;
            }

            VehicleBay bay = BayFor(selected);
            if (bay == null || !bay.Deploy())
            {
                return false;
            }

            // The ride out is a pacing beat rather than a loading screen, and a beat nobody
            // is shown is just a delay - so it is watched either way, from wherever the whole
            // of it can be seen. From the cutaway that is where the camera already is: the
            // vehicle rolls out of its bay, boards the lift and climbs out of the top of the
            // picture. Without a hall to be inside there is nothing to see until it clears
            // the ground, so the camera goes with it as it always did.
            TeamBunker bunker = TeamBunker.For(Team);
            if (bunker == null || !bunker.HasHall)
            {
                Watch(roster[selected]);
            }

            return true;
        }

        /// <summary>
        /// Puts one vehicle straight onto the field, skipping the ride out of the bunker.
        /// </summary>
        /// <param name="index">Roster index to deploy.</param>
        /// <returns><c>true</c> when that vehicle is now the one being driven.</returns>
        /// <remarks>
        /// The pacing beat exists for the player, and there is no player in a test or in a
        /// still photograph. Everything else about it is the same route the button takes.
        /// </remarks>
        public bool TakeTheField(int index)
        {
            Select(index);

            if (!HasOneLeft(selected))
            {
                return false;
            }

            VehicleBay bay = BayFor(selected);
            if (bay == null || !bay.DeployNow())
            {
                return false;
            }

            if (activeIndex < 0)
            {
                // No event reached us, which is what happens outside play mode. Take the
                // controls the same way the event would have.
                OnDeployed(bay);
            }

            return true;
        }

        /// <summary>
        /// Takes the vehicle being driven off the field, one way or the other.
        /// </summary>
        /// <returns><c>true</c> when it left.</returns>
        /// <remarks>
        /// Parked when the player is standing on their own bunker and blown up when they are
        /// not. Both end at the same select screen, so the choice a player is really making
        /// is whether to spend the drive home or the repairs.
        /// </remarks>
        public bool Recall()
        {
            VehicleController vehicle = ActiveVehicle;
            if (vehicle == null)
            {
                return false;
            }

            holding = 0.0f;

            if (IsHome)
            {
                VehicleBay bay = BayFor(activeIndex);
                if (bay == null)
                {
                    return false;
                }

                bay.Stow();
                return true;
            }

            var hull = vehicle.GetComponent<VehicleHealth>();
            return hull != null && hull.SelfDestruct();
        }

        private void OnEnable() => WatchRoster();

        /// <summary>
        /// Puts the whole roster inside the bunker and the player in front of it.
        /// </summary>
        /// <remarks>
        /// Deliberately <c>Start</c> rather than <c>OnEnable</c>: stowing a vehicle needs
        /// its side's <see cref="TeamBunker"/> to have registered itself, and the order two
        /// objects in the same scene wake up in is not something to rely on. By the first
        /// frame everything in the scene is awake, and this only has to happen once.
        /// </remarks>
        private void Start()
        {
            selected = roster.Count == 0 ? 0 : Wrap(startIndex);
            activeIndex = -1;
            holding = 0.0f;
            wreckHold = 0.0f;

            // Everybody starts inside. The first thing either player does in a match is
            // pick something, which is step one of the design document's core loop.
            foreach (VehicleBay bay in bays)
            {
                if (bay != null)
                {
                    bay.Stow();
                }
            }

            ShowTheBunker();
        }

        private void OnDisable() => ForgetRoster();

        private void Update()
        {
            if (Match.IsFinished)
            {
                // The match is decided. Whatever is out on the field coasts to a stop and
                // stops answering the controls, because the last thing either player should
                // be able to do after somebody has won is drive off and keep playing.
                VehicleController driven = ActiveVehicle;
                if (driven != null)
                {
                    driven.ReleaseControls();
                }

                return;
            }

            if (wreckHold > 0.0f)
            {
                wreckHold -= Time.deltaTime;
                if (wreckHold <= 0.0f)
                {
                    ShowTheBunker();
                }
            }

            if (controls == null)
            {
                return;
            }

            if (AtTheBunker)
            {
                // The wreck-hold window is a moment for the player to watch, not to act in -
                // choosing and deploying a different vehicle here would cut away from the
                // wreck itself, throwing away the very beat OnReturned set up.
                if (!IsHoldingOnWreck)
                {
                    ChooseFromTheBunker();
                }

                return;
            }

            Drive();
        }

        /// <summary>
        /// The bunker screen: move the highlight, and send something out.
        /// </summary>
        private void ChooseFromTheBunker()
        {
            if (IsDeploying)
            {
                return;
            }

            if (controls.NextVehicle)
            {
                Select(selected + 1);
                Sfx.Play(SfxKind.UiSelect);
            }
            else if (controls.PreviousVehicle)
            {
                Select(selected - 1);
                Sfx.Play(SfxKind.UiSelect);
            }

            if (controls.Deploy)
            {
                // The bunker roster is a menu, so it answers like one. The refusal is worth
                // more than the acceptance here: a vehicle that goes out is a vehicle the
                // player can see leaving, and one that does not is a countdown or an empty
                // reserve they cannot - see CanDeploy for the three reasons.
                if (DeploySelected())
                {
                    Sfx.Play(SfxKind.UiClick);
                }
                else
                {
                    Sfx.Play(SfxKind.UiDenied);
                }
            }
        }

        /// <summary>
        /// The field: drive the vehicle, and count the button that takes you off it.
        /// </summary>
        private void Drive()
        {
            VehicleController vehicle = ActiveVehicle;

            if (!controls.DeployHeld)
            {
                // Letting go is what arms it. The ride out of the bunker takes longer than
                // the hold does, so a pilot who kept their thumb down after choosing would
                // otherwise scuttle the vehicle the moment it was handed to them.
                recallArmed = true;
                holding = 0.0f;
            }
            else if (recallArmed)
            {
                holding += Time.deltaTime;
                if (holding >= recallHoldSeconds)
                {
                    Recall();
                    return;
                }
            }

            vehicle.SetInput(
                new VehicleInput(controls.Drive, ReadAim(vehicle), controls.Firing));
        }

        /// <summary>
        /// Subscribes to every roster entry coming out of the bunker and going back into it.
        /// </summary>
        private void WatchRoster()
        {
            ForgetRoster();

            foreach (VehicleController vehicle in roster)
            {
                VehicleBay bay = vehicle == null ? null : vehicle.GetComponent<VehicleBay>();
                bays.Add(bay);

                if (bay == null)
                {
                    continue;
                }

                bay.Deployed += OnDeployed;
                bay.Returned += OnReturned;
            }
        }

        /// <summary>
        /// Lets go of every bay this driver was listening to.
        /// </summary>
        /// <remarks>
        /// Always paired with <see cref="WatchRoster"/> rather than only with a disable,
        /// because a roster that is watched twice reacts to every deploy twice.
        /// </remarks>
        private void ForgetRoster()
        {
            foreach (VehicleBay bay in bays)
            {
                if (bay != null)
                {
                    bay.Deployed -= OnDeployed;
                    bay.Returned -= OnReturned;
                }
            }

            bays.Clear();
        }

        /// <summary>
        /// Takes the controls of a vehicle that has finished riding out.
        /// </summary>
        /// <param name="bay">The bay that just delivered one.</param>
        private void OnDeployed(VehicleBay bay)
        {
            int index = bays.IndexOf(bay);
            if (index < 0)
            {
                return;
            }

            activeIndex = index;
            selected = index;
            holding = 0.0f;
            recallArmed = false;
            wreckHold = 0.0f;
            Watch(roster[index]);
        }

        /// <summary>
        /// Sends the player back to the select screen when their vehicle leaves the field.
        /// </summary>
        /// <param name="bay">The bay that just took one back.</param>
        /// <remarks>
        /// A wreck gets a moment before the cut. The explosion is the only feedback a player
        /// has that they were killed rather than that something went wrong, and cutting to a
        /// menu on the same frame throws it away. Parking the vehicle deliberately does not
        /// get the moment, because the player already knows what happened.
        /// </remarks>
        private void OnReturned(VehicleBay bay)
        {
            int index = bays.IndexOf(bay);
            if (index < 0)
            {
                return;
            }

            activeIndex = -1;
            selected = index;
            holding = 0.0f;

            if (bay.IsRepairing && wreckHoldSeconds > 0.0f)
            {
                wreckHold = wreckHoldSeconds;
                if (cameraRig != null)
                {
                    // Hold where the wreck was, which is where the explosion is.
                    cameraRig.SetTarget(null, false);
                }

                return;
            }

            ShowTheBunker();
        }

        /// <summary>
        /// Points the camera at this player's own bunker, which is where they now are.
        /// </summary>
        private void ShowTheBunker()
        {
            wreckHold = 0.0f;

            TeamBunker bunker = TeamBunker.For(Team);
            if (bunker != null)
            {
                bunker.ShowHall(true);
            }

            ShowTheChoice();

            if (cameraRig == null)
            {
                return;
            }

            if (bunker == null)
            {
                cameraRig.SetTarget(null, false);
                return;
            }

            if (!bunker.HasHall)
            {
                cameraRig.Park(bunker.transform.position);
                return;
            }

            Camera view = cameraRig.View;
            cameraRig.Park(BunkerView.Solve(bunker, view.fieldOfView, view.aspect));
        }

        /// <summary>
        /// Says which vehicle is highlighted, in the world rather than on a panel.
        /// </summary>
        /// <remarks>
        /// Two things say it: the bay lights up, and the lift car comes to that deck and
        /// waits there. The second is the half worth having - it is the only part of the
        /// select screen that answers "what did I just press" while the player is looking at
        /// the picture rather than at the words under it, and it means the ride out starts
        /// with the lift already where it needs to be.
        /// </remarks>
        private void ShowTheChoice()
        {
            TeamBunker bunker = TeamBunker.For(Team);
            if (bunker == null || !bunker.HasHall || !AtTheBunker)
            {
                return;
            }

            bunker.ChooseBay(selected);

            if (bunker.Car != null && !IsDeploying)
            {
                bunker.Car.SendTo(bunker.BayFor(selected).y);
            }
        }

        private void Watch(VehicleController vehicle)
        {
            TeamBunker bunker = TeamBunker.For(Team);
            if (bunker != null)
            {
                // Out on the field: nobody is looking into this base, so it is not drawn.
                // The ground is opaque from above and would hide it anyway - the shaft is
                // the hole in that argument, and a player driving over their own bunker
                // would otherwise be looking down a lit stairwell.
                bunker.ShowHall(false);
                bunker.ChooseBay(-1);

                if (bunker.Car != null)
                {
                    bunker.Car.SendTo(bunker.LiftPoint.y);
                }
            }

            if (cameraRig != null)
            {
                cameraRig.SetTarget(vehicle, true);
            }
        }

        private int Wrap(int index) => ((index % roster.Count) + roster.Count) % roster.Count;

        /// <summary>
        /// Works out where the pilot wants the gun pointed, in world space.
        /// </summary>
        /// <param name="vehicle">Vehicle doing the aiming, for the plane to aim on.</param>
        /// <returns>A direction on the ground plane, or zero to leave the turret stowed.</returns>
        private Vector2 ReadAim(VehicleController vehicle)
        {
            Vector2 value = controls.Aim;
            return controls.Scheme == ControlScheme.KeyboardAndMouse
                ? PointerAim(vehicle, value)
                : StickAim(value);
        }

        /// <summary>
        /// Turns a stick direction into a direction on the map.
        /// </summary>
        /// <param name="stick">Stick deflection, -1..1 on each axis.</param>
        /// <returns>A direction on the ground plane, or zero if the stick is near centre.</returns>
        /// <remarks>
        /// A stick points at the screen, and the screen is the world turned by the camera's
        /// fixed heading, so the two only agree once the stick is turned with it. The
        /// heading is the same for every player, which is the whole reason the camera never
        /// rotates.
        /// </remarks>
        private Vector2 StickAim(Vector2 stick)
        {
            if (stick.sqrMagnitude < 0.04f)
            {
                return Vector2.zero;
            }

            float cameraYaw = cameraRig == null ? 0.0f : cameraRig.YawDegrees;
            Vector3 world = Quaternion.Euler(0.0f, cameraYaw, 0.0f) * new Vector3(stick.x, 0.0f, stick.y);
            return new Vector2(world.x, world.z).normalized;
        }

        /// <summary>
        /// Turns a pointer position into a direction on the map.
        /// </summary>
        /// <param name="vehicle">Vehicle doing the aiming, for the plane to aim on.</param>
        /// <param name="screenPoint">Pointer position in pixels.</param>
        /// <returns>A direction on the ground plane, or zero when the pointer is on top of it.</returns>
        /// <remarks>
        /// The pointer is clamped into this player's viewport first. The mouse roams the
        /// whole window while it belongs to one half of a split screen, and a pointer left
        /// over the other player's view would otherwise aim this turret at somewhere its
        /// owner cannot see.
        /// </remarks>
        private Vector2 PointerAim(VehicleController vehicle, Vector2 screenPoint)
        {
            Camera view = cameraRig == null ? Camera.main : cameraRig.View;
            if (view == null)
            {
                return Vector2.zero;
            }

            Vector2 inside = SplitScreenLayout.ClampToViewport(
                view.rect, screenPoint, new Vector2(Screen.width, Screen.height));

            Vector3 origin = vehicle.transform.position;
            var ground = new Plane(Vector3.up, origin);
            Ray ray = view.ScreenPointToRay(inside);
            if (!ground.Raycast(ray, out float travel))
            {
                return Vector2.zero;
            }

            Vector3 hit = ray.GetPoint(travel);
            var aim = new Vector2(hit.x - origin.x, hit.z - origin.z);
            return aim.sqrMagnitude < 0.25f ? Vector2.zero : aim.normalized;
        }
    }
}
