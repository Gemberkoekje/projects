using System.Collections.Generic;
using UnityEngine;

namespace IronFlag.Vehicles
{
    /// <summary>
    /// What everything drivable has in common: an identity, a set of handling numbers, a
    /// rigidbody, and one frame of pilot intent at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Subclasses own the movement model - <see cref="GroundVehicle"/> for the three that
    /// drive, <see cref="Helicopter"/> for the one that flies - and everything else in the
    /// game talks to this type. The camera follows a <see cref="VehicleController"/>, the
    /// player driver hands one <see cref="VehicleInput"/> per frame to a
    /// <see cref="VehicleController"/>, and a weapon reads its trigger off one.
    /// </para>
    /// <para>
    /// A vehicle nobody is driving is not a special state: it simply stops receiving input,
    /// so it brakes to a halt and its turret recentres. Nothing has to be switched off.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(Rigidbody))]
    public abstract class VehicleController : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Which vehicle this is. Decides the roster slot, the bunker entry and both tunings.")]
        private VehicleKind kind = VehicleKind.None;

        [SerializeField]
        [Tooltip("Handling numbers. Stamped from VehicleTuning.For(kind) when the prefab is built.")]
        private VehicleTuning tuning = new VehicleTuning();

        /// <summary>Shared frictionless surface, created when the first vehicle wakes.</summary>
        private static PhysicsMaterial slickSurface;

        /// <summary>Every vehicle currently on the field, in the order they woke up.</summary>
        private static readonly List<VehicleController> Live = new List<VehicleController>();

        private VehicleInput input = VehicleInput.Idle;
        private Rigidbody body;

        /// <summary>Which vehicle this is.</summary>
        public VehicleKind Kind => kind;

        /// <summary>The handling numbers this vehicle is driving with.</summary>
        public VehicleTuning Tuning => tuning;

        /// <summary>The most recent frame of pilot intent.</summary>
        public VehicleInput CurrentInput => input;

        /// <summary>Speed along the vehicle's own forward axis; negative is reversing.</summary>
        public abstract float ForwardSpeed { get; }

        /// <summary>World-space velocity, including any vertical component.</summary>
        public abstract Vector3 Velocity { get; }

        /// <summary>Heading in degrees, clockwise from world +Z.</summary>
        public float YawDegrees => transform.eulerAngles.y;

        /// <summary>The rigidbody the movement model drives.</summary>
        protected Rigidbody Body => body;

        /// <summary>
        /// Every vehicle that is out on the field right now.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The same static roll-call <see cref="IronFlag.Core.TeamBunker"/> and
        /// <see cref="IronFlag.Supply.SupplyPoint"/> keep, and it exists for the same
        /// reason: something that is not a vehicle needs to ask what is near it, and there
        /// are four of these in a match. A flag looking for a jeep and a tower looking for a
        /// scout both walk this list rather than sweeping physics, so what they find is
        /// decided by where things are rather than by which colliders happened to be
        /// enabled.
        /// </para>
        /// <para>
        /// Membership is exactly "drivable": <see cref="IronFlag.Core.VehicleBay"/> disables
        /// the controller while a vehicle is stowed, being repaired, or riding out of the
        /// bunker, which takes it off this list for free. A vehicle waiting in its bunker
        /// therefore cannot pick anything up, and neither can one halfway up the lift.
        /// </para>
        /// </remarks>
        public static IReadOnlyList<VehicleController> OnTheField => Live;

        /// <summary>
        /// Hands this vehicle one frame of pilot intent.
        /// </summary>
        /// <param name="value">What the pilot is asking for this frame.</param>
        /// <remarks>
        /// Safe to call from <c>Update</c>: input is stored and consumed by the next fixed
        /// step, so a frame that produces no call simply repeats the last intent rather
        /// than dropping to neutral.
        /// </remarks>
        public void SetInput(VehicleInput value) => input = value;

        /// <summary>
        /// Stops taking orders: the vehicle brakes to a halt where it stands.
        /// </summary>
        public void ReleaseControls() => input = VehicleInput.Idle;

        /// <summary>
        /// Sets the identity and handling numbers.
        /// </summary>
        /// <param name="vehicleKind">Which vehicle this is.</param>
        /// <param name="vehicleTuning">Handling numbers to drive with.</param>
        /// <remarks>
        /// Called by the prefab builder while authoring, and by anything that spawns a
        /// vehicle. Changing the tuning at runtime takes effect on the next fixed step;
        /// changing the mass needs <see cref="ApplyBodySettings"/>, which happens on the
        /// next enable.
        /// </remarks>
        public void Configure(VehicleKind vehicleKind, VehicleTuning vehicleTuning)
        {
            kind = vehicleKind;
            tuning = vehicleTuning == null ? VehicleTuning.For(vehicleKind) : vehicleTuning;
        }

        /// <summary>
        /// Moves the vehicle somewhere else, discarding the momentum it had.
        /// </summary>
        /// <param name="position">Where to put it.</param>
        /// <param name="yawDegrees">Heading to face, clockwise from world +Z.</param>
        /// <remarks>
        /// Goes through the rigidbody rather than the transform, so physics does not spend
        /// the next step resolving the old position against the new one.
        /// </remarks>
        public virtual void Teleport(Vector3 position, float yawDegrees)
        {
            EnsureBody();
            Quaternion rotation = Quaternion.Euler(0.0f, yawDegrees, 0.0f);
            body.position = position;
            body.rotation = rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(position, rotation);
            input = VehicleInput.Idle;
        }

        /// <summary>
        /// Caches the rigidbody and applies the settings the movement model needs.
        /// </summary>
        protected virtual void Awake() => ApplyBodySettings();

        /// <summary>
        /// Joins the roll-call of vehicles on the field.
        /// </summary>
        /// <remarks>
        /// Virtual, and paired with <see cref="OnDisable"/>, for the same reason
        /// <see cref="Awake"/> is: Unity calls only the most derived declaration, so a
        /// subclass that needs one of these has to override it and call back here. A
        /// subclass that declared its own without doing so would silently take every vehicle
        /// of that type off <see cref="OnTheField"/>.
        /// </remarks>
        protected virtual void OnEnable() => Live.Add(this);

        /// <summary>
        /// Leaves the roll-call of vehicles on the field.
        /// </summary>
        protected virtual void OnDisable() => Live.Remove(this);

        /// <summary>
        /// Applies the rigidbody settings this movement model depends on, now.
        /// </summary>
        /// <remarks>
        /// Called on awake, and by the prefab builder so a saved prefab shows the same
        /// rigidbody the vehicle will actually run with rather than Unity's defaults.
        /// </remarks>
        public void ApplyBodySettings()
        {
            EnsureBody();
            ConfigureBody();
        }

        /// <summary>
        /// Sets the rigidbody up for this movement model.
        /// </summary>
        /// <remarks>
        /// Done in code rather than left to the prefab because the movement models are only
        /// correct for one configuration each - a helicopter with gravity on, or a ground
        /// vehicle free to tumble, does not misbehave visibly so much as quietly stop
        /// matching its own maths.
        /// </remarks>
        protected virtual void ConfigureBody()
        {
            body.mass = Tuning.Mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            if (Application.isPlaying)
            {
                ApplyFrictionlessSurface();
            }
        }

        /// <summary>
        /// Gives every collider on this vehicle a frictionless surface.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The movement model owns the speed outright and rewrites the rigidbody's velocity
        /// every fixed step, so surface friction is not a force any vehicle here is
        /// modelling - it is a second, invisible brake. It is also the difference between a
        /// jeep that accelerates and one that does not: a ground vehicle reads its velocity
        /// back to notice obstructions, and friction from the ground it is standing on reads
        /// exactly like driving into a wall. That failure is silent and looks like a tuning
        /// problem, which is why it is worth a paragraph rather than a line.
        /// </para>
        /// <para>
        /// Created at runtime and shared, so no generated prefab has to carry a reference to
        /// it and one frictionless surface serves the whole roster.
        /// </para>
        /// </remarks>
        private void ApplyFrictionlessSurface()
        {
            if (slickSurface == null)
            {
                slickSurface = new PhysicsMaterial("RF_VehicleSurface")
                {
                    dynamicFriction = 0.0f,
                    staticFriction = 0.0f,
                    bounciness = 0.0f,
                    frictionCombine = PhysicsMaterialCombine.Minimum,
                    bounceCombine = PhysicsMaterialCombine.Minimum,
                };
            }

            foreach (Collider surface in GetComponentsInChildren<Collider>())
            {
                surface.sharedMaterial = slickSurface;
            }
        }

        private void EnsureBody()
        {
            if (body == null)
            {
                body = GetComponent<Rigidbody>();
            }
        }
    }
}
