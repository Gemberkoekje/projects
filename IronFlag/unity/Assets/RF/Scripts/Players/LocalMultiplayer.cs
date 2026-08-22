using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using IronFlag.Core;

namespace IronFlag.Players
{
    /// <summary>
    /// The two seats in front of the screen: who is holding what, and which half of the
    /// screen each of them is looking at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One of these owns the whole local session. It hands every
    /// <see cref="PlayerVehicleDriver"/> in its list a private
    /// <see cref="PlayerControls"/> built from whatever devices are plugged in, and gives
    /// each of their camera rigs its slice of the screen. Both are decided by code that does
    /// not touch the scene - <see cref="DeviceAssignment.Solve"/> and
    /// <see cref="SplitScreenLayout.ViewportFor"/> - so what is left here is the wiring.
    /// </para>
    /// <para>
    /// Devices are re-dealt whenever one is plugged in or unplugged, which is the difference
    /// between a second player being able to join by picking up a controller and having to
    /// restart the game. A player who ends up with nothing simply stops receiving input, and
    /// their vehicle coasts to a halt.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Local Multiplayer")]
    [DefaultExecutionOrder(-100)]
    public sealed class LocalMultiplayer : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The controls every player gets a private copy of.")]
        private InputActionAsset controls;

        [SerializeField]
        [Tooltip("The local players, in seat order. The first one is at the top of the screen.")]
        private List<PlayerVehicleDriver> players = new List<PlayerVehicleDriver>();

        private readonly List<PlayerControls> held = new List<PlayerControls>();

        /// <summary>The local players, in seat order.</summary>
        public IReadOnlyList<PlayerVehicleDriver> Players => players;

        /// <summary>
        /// Points this session at the controls and the players sharing the screen.
        /// </summary>
        /// <param name="controlsAsset">Actions asset each player is given a copy of.</param>
        /// <param name="seated">The local players, in seat order.</param>
        /// <remarks>Called by the sandbox scene builder.</remarks>
        public void Configure(InputActionAsset controlsAsset, IEnumerable<PlayerVehicleDriver> seated)
        {
            controls = controlsAsset;
            players = new List<PlayerVehicleDriver>();
            if (seated != null)
            {
                players.AddRange(seated);
            }
        }

        /// <summary>
        /// Gives every player their slice of the screen.
        /// </summary>
        /// <remarks>
        /// Safe to call while not playing, which is how the generated scene comes out of the
        /// builder already split - the Game view shows the real framing without anyone
        /// having to press Play.
        /// </remarks>
        public void ApplyViewports()
        {
            for (int slot = 0; slot < players.Count; slot++)
            {
                TopDownCameraRig rig = players[slot] == null ? null : players[slot].CameraRig;
                if (rig != null)
                {
                    rig.Viewport = SplitScreenLayout.ViewportFor(slot, players.Count);
                }
            }
        }

        /// <summary>
        /// Deals the connected devices out and hands each player their controls.
        /// </summary>
        /// <remarks>
        /// Everything already held is thrown away first, so this is safe to call again when
        /// the set of devices changes.
        /// </remarks>
        public void AssignDevices()
        {
            ReleaseControls();

            var pads = new List<Gamepad>(Gamepad.all);
            bool keyboardAndMouse = Keyboard.current != null && Mouse.current != null;
            DeviceAssignment[] assignments =
                DeviceAssignment.Solve(players.Count, pads.Count, keyboardAndMouse);

            for (int slot = 0; slot < players.Count; slot++)
            {
                PlayerControls seat = Hold(assignments[slot], pads);
                held.Add(seat);

                if (players[slot] != null)
                {
                    players[slot].UseControls(seat);
                }
            }
        }

        private PlayerControls Hold(DeviceAssignment assignment, List<Gamepad> pads)
        {
            switch (assignment.Scheme)
            {
                case ControlScheme.KeyboardAndMouse:
                    return PlayerControls.For(
                        controls, assignment.Scheme, Keyboard.current, Mouse.current);
                case ControlScheme.Gamepad:
                    return PlayerControls.For(controls, assignment.Scheme, pads[assignment.GamepadIndex]);
                default:
                    return null;
            }
        }

        private void OnEnable()
        {
            if (controls == null)
            {
                Debug.LogWarning(
                    $"IronFlag: {name} has no controls assigned; nobody will be able to drive.", this);
            }

            ApplyViewports();
            AssignDevices();
            InputSystem.onDeviceChange += OnDeviceChange;
        }

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            ReleaseControls();
        }

        /// <summary>
        /// Re-deals the devices when one arrives or leaves.
        /// </summary>
        /// <param name="device">The device that changed.</param>
        /// <param name="change">What happened to it.</param>
        /// <remarks>
        /// Only connection changes matter: the rest of the notifications - a device being
        /// reconfigured, or its usages changing - do not alter who is holding what.
        /// </remarks>
        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Removed
                || change == InputDeviceChange.Reconnected || change == InputDeviceChange.Disconnected)
            {
                AssignDevices();
            }
        }

        private void ReleaseControls()
        {
            foreach (PlayerControls seat in held)
            {
                if (seat != null)
                {
                    seat.Dispose();
                }
            }

            held.Clear();

            foreach (PlayerVehicleDriver player in players)
            {
                if (player != null)
                {
                    player.UseControls(null);
                }
            }
        }
    }
}
