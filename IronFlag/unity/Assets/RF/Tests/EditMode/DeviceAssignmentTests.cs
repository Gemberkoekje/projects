using System;
using NUnit.Framework;
using IronFlag.Players;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// Checks who ends up holding what, for every combination of hardware worth caring about.
    /// </summary>
    /// <remarks>
    /// This is the rule the whole split-screen session hangs off, and getting it wrong shows
    /// up as "player two cannot move" rather than as an error. It is also the kind of rule
    /// that is easy to break while fixing something else, which is why every case is spelled
    /// out rather than covered by one loop.
    /// </remarks>
    public sealed class DeviceAssignmentTests
    {
        [Test]
        public void OnePlayerAtADeskKeepsTheKeyboard()
        {
            DeviceAssignment[] seats = DeviceAssignment.Solve(1, 0, true);

            Assert.That(seats.Length, Is.EqualTo(1));
            Assert.That(seats[0].Scheme, Is.EqualTo(ControlScheme.KeyboardAndMouse));
        }

        [Test]
        public void APadPluggedInDoesNotTakeTheKeyboardOffASoloPlayer()
        {
            DeviceAssignment[] seats = DeviceAssignment.Solve(1, 1, true);

            Assert.That(seats[0].Scheme, Is.EqualTo(ControlScheme.KeyboardAndMouse));
        }

        [Test]
        public void TheSecondPlayerTakesTheGamepad()
        {
            DeviceAssignment[] seats = DeviceAssignment.Solve(2, 1, true);

            Assert.That(seats[0].Scheme, Is.EqualTo(ControlScheme.KeyboardAndMouse));
            Assert.That(seats[1].Scheme, Is.EqualTo(ControlScheme.Gamepad));
            Assert.That(seats[1].GamepadIndex, Is.EqualTo(0));
        }

        [Test]
        public void TwoGamepadsPutBothPlayersOnGamepads()
        {
            DeviceAssignment[] seats = DeviceAssignment.Solve(2, 2, true);

            Assert.That(seats[0].Scheme, Is.EqualTo(ControlScheme.Gamepad));
            Assert.That(seats[1].Scheme, Is.EqualTo(ControlScheme.Gamepad));
            Assert.That(
                seats[0].GamepadIndex,
                Is.Not.EqualTo(seats[1].GamepadIndex),
                "both players are holding the same pad");
        }

        [Test]
        public void PluggingASecondPadInLeavesTheFirstPadWhereItWas()
        {
            DeviceAssignment before = DeviceAssignment.Solve(2, 1, true)[1];
            DeviceAssignment after = DeviceAssignment.Solve(2, 2, true)[1];

            Assert.That(after, Is.EqualTo(before), "player two's pad was swapped out from under them");
        }

        [Test]
        public void APlayerWithNothingToHoldIsUnpaired()
        {
            DeviceAssignment[] seats = DeviceAssignment.Solve(2, 0, true);

            Assert.That(seats[0].Scheme, Is.EqualTo(ControlScheme.KeyboardAndMouse));
            Assert.That(seats[1].IsPaired, Is.False);
            Assert.That(seats[1].GamepadIndex, Is.EqualTo(DeviceAssignment.NoGamepad));
        }

        [Test]
        public void WithoutAKeyboardThePadsAreDealtFromTheFirstPlayer()
        {
            DeviceAssignment[] seats = DeviceAssignment.Solve(2, 1, false);

            Assert.That(seats[0].Scheme, Is.EqualTo(ControlScheme.Gamepad));
            Assert.That(seats[0].GamepadIndex, Is.EqualTo(0));
            Assert.That(seats[1].IsPaired, Is.False);
        }

        [Test]
        public void NoDevicesAtAllLeavesEverybodyUnpaired()
        {
            DeviceAssignment[] seats = DeviceAssignment.Solve(2, 0, false);

            Assert.That(seats[0].IsPaired, Is.False);
            Assert.That(seats[1].IsPaired, Is.False);
        }

        [Test]
        public void NobodyIsEverHandedAGamepadThatIsNotThere()
        {
            for (int players = 0; players <= 4; players++)
            {
                for (int pads = 0; pads <= 4; pads++)
                {
                    foreach (bool keyboard in new[] { true, false })
                    {
                        DeviceAssignment[] seats = DeviceAssignment.Solve(players, pads, keyboard);
                        string what = $"{players} players, {pads} pads, keyboard {keyboard}";

                        Assert.That(seats.Length, Is.EqualTo(players), what);
                        AssertPadsAreRealAndUnshared(seats, pads, what);
                        AssertAtMostOneKeyboardPlayer(seats, what);
                    }
                }
            }
        }

        [Test]
        public void ANegativePlayerCountIsRefused()
        {
            Assert.That(
                () => DeviceAssignment.Solve(-1, 0, true), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        private static void AssertPadsAreRealAndUnshared(
            DeviceAssignment[] seats, int pads, string what)
        {
            var taken = new bool[Math.Max(pads, 1)];
            foreach (DeviceAssignment seat in seats)
            {
                if (seat.Scheme != ControlScheme.Gamepad)
                {
                    Assert.That(seat.GamepadIndex, Is.EqualTo(DeviceAssignment.NoGamepad), what);
                    continue;
                }

                Assert.That(seat.GamepadIndex, Is.InRange(0, pads - 1), what);
                Assert.That(taken[seat.GamepadIndex], Is.False, $"{what}: two players share a pad");
                taken[seat.GamepadIndex] = true;
            }
        }

        private static void AssertAtMostOneKeyboardPlayer(DeviceAssignment[] seats, string what)
        {
            int atTheKeyboard = 0;
            foreach (DeviceAssignment seat in seats)
            {
                if (seat.Scheme == ControlScheme.KeyboardAndMouse)
                {
                    atTheKeyboard++;
                }
            }

            Assert.That(atTheKeyboard, Is.LessThanOrEqualTo(1), $"{what}: the keyboard was shared");
        }
    }
}
