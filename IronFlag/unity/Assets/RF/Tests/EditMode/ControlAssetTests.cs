using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;
using IronFlag.Editor.Gameplay;
using IronFlag.Players;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// Checks the controls asset against the names the code looks things up by.
    /// </summary>
    /// <remarks>
    /// Every one of these is a lookup by string that fails silently at runtime: a renamed
    /// action, a misspelt binding group or a scheme with no bindings all give a player who
    /// simply cannot do something, with nothing in the console to say why. The asset is
    /// edited in a window rather than in a diff, so it needs a test more than the code does.
    /// </remarks>
    public sealed class ControlAssetTests
    {
        private static readonly string[] Actions =
        {
            "Drive", "Aim", "Lift", "Fire", "NextVehicle", "PreviousVehicle", "Deploy",
        };

        private static readonly string[] Schemes =
        {
            ControlSchemes.KeyboardAndMouse, ControlSchemes.Gamepad,
        };

        [Test]
        public void TheAssetIsWhereEverythingExpectsItToBe()
        {
            Assert.That(Load(), Is.Not.Null, VehicleSandboxScene.ActionsPath);
        }

        [Test]
        public void TheVehicleMapHasEveryActionTheDriverReads()
        {
            InputActionMap map = VehicleMap();

            foreach (string action in Actions)
            {
                Assert.That(map.FindAction(action, false), Is.Not.Null, action);
            }
        }

        [Test]
        public void BothControlSchemesExistUnderTheNamesTheCodeUses()
        {
            InputActionAsset controls = Load();

            foreach (string scheme in Schemes)
            {
                Assert.That(
                    controls.FindControlSchemeIndex(scheme),
                    Is.GreaterThanOrEqualTo(0),
                    $"there is no '{scheme}' control scheme");
            }
        }

        [Test]
        public void EveryActionCanBeDoneOnEitherScheme()
        {
            InputActionMap map = VehicleMap();

            foreach (string name in Actions)
            {
                InputAction action = map.FindAction(name, false);
                foreach (string scheme in Schemes)
                {
                    Assert.That(
                        BoundIn(action, scheme),
                        Is.True,
                        $"'{name}' has no binding on {scheme}");
                }
            }
        }

        [Test]
        public void DrivingAndSwitchingVehiclesAreNotTheSameControl()
        {
            InputActionMap map = VehicleMap();
            var driving = new HashSet<string>(Paths(map.FindAction("Drive", false)));

            foreach (string name in new[] { "NextVehicle", "PreviousVehicle" })
            {
                foreach (string path in Paths(map.FindAction(name, false)))
                {
                    Assert.That(
                        driving.Contains(path),
                        Is.False,
                        $"'{path}' both drives and switches vehicles");
                }
            }
        }

        /// <summary>
        /// Deploying is the one button that means two things - send this one out, and take
        /// that one off the field - so it must not be a button that is already held for some
        /// other reason. Sharing it with the trigger would redeploy a player every time they
        /// died with their finger down.
        /// </summary>
        [Test]
        public void DeployingIsNotTheSameControlAsDrivingOrFiring()
        {
            InputActionMap map = VehicleMap();
            var taken = new HashSet<string>(Paths(map.FindAction("Drive", false)));
            taken.UnionWith(Paths(map.FindAction("Fire", false)));
            taken.UnionWith(Paths(map.FindAction("Lift", false)));

            foreach (string path in Paths(map.FindAction("Deploy", false)))
            {
                Assert.That(taken.Contains(path), Is.False, $"'{path}' deploys and does something else");
            }
        }

        [Test]
        public void AimingOnTheKeyboardReadsThePointerAndNotItsMovement()
        {
            InputAction aim = VehicleMap().FindAction("Aim", false);

            foreach (InputBinding binding in aim.bindings)
            {
                if (InGroup(binding, ControlSchemes.KeyboardAndMouse))
                {
                    Assert.That(
                        binding.path,
                        Is.EqualTo("<Mouse>/position"),
                        "a top-down turret wants somewhere to point at, not a nudge");
                }
            }
        }

        private static InputActionAsset Load()
            => AssetDatabase.LoadAssetAtPath<InputActionAsset>(VehicleSandboxScene.ActionsPath);

        private static InputActionMap VehicleMap()
        {
            InputActionMap map = Load().FindActionMap(PlayerControls.VehicleActionMap, false);
            Assert.That(map, Is.Not.Null, PlayerControls.VehicleActionMap);
            return map;
        }

        private static bool BoundIn(InputAction action, string scheme)
        {
            foreach (InputBinding binding in action.bindings)
            {
                if (!binding.isComposite && InGroup(binding, scheme))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool InGroup(InputBinding binding, string scheme)
            => !string.IsNullOrEmpty(binding.groups) && binding.groups.Contains(scheme);

        private static IEnumerable<string> Paths(InputAction action)
        {
            foreach (InputBinding binding in action.bindings)
            {
                if (!binding.isComposite)
                {
                    yield return binding.path;
                }
            }
        }
    }
}
