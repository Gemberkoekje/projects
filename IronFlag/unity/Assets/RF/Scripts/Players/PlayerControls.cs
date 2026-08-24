using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace IronFlag.Players
{
    /// <summary>
    /// One local player's private copy of the controls, bound to their devices alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Routing input to two players on one machine comes down to two settings on an
    /// <see cref="InputActionAsset"/>: <c>devices</c>, which decides whose hardware it
    /// listens to, and <c>bindingMask</c>, which decides which control scheme's bindings
    /// exist. Both are properties of the asset instance, so each player needs their own -
    /// hence the clone in <see cref="For"/>. Share one asset and the second player silently
    /// overwrites the first player's routing.
    /// </para>
    /// <para>
    /// This is deliberately not <c>PlayerInput</c> and not <c>InputUser</c>. Both exist to
    /// manage players joining, leaving and losing devices, and this game has a fixed two
    /// seats decided before the match starts. What is left after taking that away is the two
    /// lines above, which are worth owning outright: they are testable, they explain
    /// themselves, and nothing in them had to be undone when M4 put a bunker in front of
    /// the vehicles - the bunker screen reads the same six actions the field does.
    /// </para>
    /// <para>
    /// Aiming is read as a plain <see cref="Vector2"/> and is <em>not</em> the same quantity
    /// on both schemes - it is a pointer position in pixels on the keyboard, and a stick
    /// direction on a gamepad. <see cref="Scheme"/> says which, and
    /// <see cref="PlayerVehicleDriver"/> is what turns either into a direction in the world.
    /// </para>
    /// </remarks>
    public sealed class PlayerControls : IDisposable
    {
        /// <summary>Name of the action map the vehicles are driven from.</summary>
        public const string VehicleActionMap = "Vehicle";

        private readonly InputActionAsset asset;
        private readonly InputAction drive;
        private readonly InputAction aim;
        private readonly InputAction fire;
        private readonly InputAction next;
        private readonly InputAction previous;
        private readonly InputAction deploy;

        private PlayerControls(InputActionAsset ownAsset, InputActionMap map, ControlScheme scheme)
        {
            asset = ownAsset;
            Scheme = scheme;

            drive = map.FindAction("Drive", false);
            aim = map.FindAction("Aim", false);
            fire = map.FindAction("Fire", false);
            next = map.FindAction("NextVehicle", false);
            previous = map.FindAction("PreviousVehicle", false);
            deploy = map.FindAction("Deploy", false);
        }

        /// <summary>The scheme this player is on.</summary>
        public ControlScheme Scheme { get; }

        /// <summary>Steer on X and throttle on Y, each in -1..1.</summary>
        public Vector2 Drive => drive == null ? Vector2.zero : drive.ReadValue<Vector2>();

        /// <summary>
        /// Where the player is aiming: a pointer position in pixels on
        /// <see cref="ControlScheme.KeyboardAndMouse"/>, a stick direction on
        /// <see cref="ControlScheme.Gamepad"/>.
        /// </summary>
        public Vector2 Aim => aim == null ? Vector2.zero : aim.ReadValue<Vector2>();

        /// <summary>
        /// Whether the trigger is being held.
        /// </summary>
        /// <remarks>
        /// Held, not pressed: every weapon in the roster has its own rate of fire, so what a
        /// held trigger produces is a question for the gun rather than for the controls.
        /// </remarks>
        public bool Firing => fire != null && fire.IsPressed();

        /// <summary>Whether the player asked for the next vehicle this frame.</summary>
        public bool NextVehicle => next != null && next.WasPressedThisFrame();

        /// <summary>Whether the player asked for the previous vehicle this frame.</summary>
        public bool PreviousVehicle => previous != null && previous.WasPressedThisFrame();

        /// <summary>Whether the player pressed the deploy button this frame.</summary>
        /// <remarks>
        /// A tap at the bunker sends the highlighted vehicle out. The same button held down
        /// in the field takes the current one off it - see <see cref="DeployHeld"/> - which
        /// is one button doing the two halves of the same idea rather than two buttons that
        /// have to be learnt separately.
        /// </remarks>
        public bool Deploy => deploy != null && deploy.WasPressedThisFrame();

        /// <summary>Whether the deploy button is being held down.</summary>
        /// <remarks>
        /// How long it has been held is counted by
        /// <see cref="PlayerVehicleDriver"/> rather than by an input-system hold
        /// interaction, because the bar filling up on the HUD needs the elapsed time
        /// anyway and two clocks measuring the same press would eventually disagree.
        /// </remarks>
        public bool DeployHeld => deploy != null && deploy.IsPressed();

        /// <summary>
        /// Gives a player their own copy of the controls, listening to their devices only.
        /// </summary>
        /// <param name="controls">
        /// The shared actions asset. It is cloned, never enabled, and never modified.
        /// </param>
        /// <param name="scheme">Control scheme to filter the bindings down to.</param>
        /// <param name="devices">The devices this player is holding.</param>
        /// <returns>
        /// The player's controls, already enabled, or <c>null</c> when there is nothing to
        /// bind - no asset, no <see cref="VehicleActionMap"/>, no scheme or no devices.
        /// </returns>
        /// <remarks>
        /// Returning null rather than a dead object is deliberate: a player with no gamepad
        /// plugged in has no controls, and their vehicle should sit still rather than
        /// respond to whatever the other player is doing.
        /// </remarks>
        public static PlayerControls For(
            InputActionAsset controls, ControlScheme scheme, params InputDevice[] devices)
        {
            if (controls == null || scheme == ControlScheme.None || devices == null || devices.Length == 0)
            {
                return null;
            }

            var own = UnityEngine.Object.Instantiate(controls);
            own.name = $"{controls.name} ({scheme})";

            InputActionMap map = own.FindActionMap(VehicleActionMap, false);
            if (map == null)
            {
                Debug.LogWarning(
                    $"IronFlag: action map '{VehicleActionMap}' is missing from {controls.name}; " +
                    "nobody will be able to drive.");
                UnityEngine.Object.DestroyImmediate(own);
                return null;
            }

            // The order matters: mask first, so resolving the bindings against the devices
            // happens once, against the bindings this player actually has.
            own.bindingMask = InputBinding.MaskByGroup(ControlSchemes.GroupFor(scheme));
            own.devices = new ReadOnlyArray<InputDevice>(devices);

            var player = new PlayerControls(own, map, scheme);
            map.Enable();
            return player;
        }

        /// <summary>
        /// Disables the actions and throws the cloned asset away.
        /// </summary>
        public void Dispose()
        {
            if (asset == null)
            {
                return;
            }

            asset.Disable();
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(asset);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }
    }
}
