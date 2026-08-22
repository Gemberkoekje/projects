using System;

namespace IronFlag.Players
{
    /// <summary>
    /// Which controls one local player ends up holding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deciding this is arithmetic over "how many players, how many gamepads, is there a
    /// keyboard" - so <see cref="Solve"/> takes those three numbers rather than a list of
    /// devices, and can be checked without a single device existing.
    /// <see cref="LocalMultiplayer"/> does the mapping from a gamepad index to a real pad.
    /// </para>
    /// <para>
    /// The rule, in one sentence: <em>the second player is always on a gamepad, and the
    /// first player keeps the keyboard unless there are pads enough to go round.</em> That
    /// is the design doc's "keyboard/mouse for P1, gamepad for P2 (or dual gamepad)", and it
    /// falls out sensibly at the edges - a solo player with a pad plugged in still gets the
    /// keyboard, and a machine with no keyboard at all still deals its pads out from the
    /// first player.
    /// </para>
    /// </remarks>
    [Serializable]
    public readonly struct DeviceAssignment : IEquatable<DeviceAssignment>
    {
        /// <summary><see cref="GamepadIndex"/> for a player who is not on a gamepad.</summary>
        public const int NoGamepad = -1;

        /// <summary>
        /// Creates one player's assignment.
        /// </summary>
        /// <param name="scheme">Scheme the player is on.</param>
        /// <param name="gamepadIndex">
        /// Which connected gamepad, in the order the Input System lists them, or
        /// <see cref="NoGamepad"/>.
        /// </param>
        public DeviceAssignment(ControlScheme scheme, int gamepadIndex)
        {
            Scheme = scheme;
            GamepadIndex = gamepadIndex;
        }

        /// <summary>What a player holds when nothing was left to give them.</summary>
        public static DeviceAssignment Unpaired => new DeviceAssignment(ControlScheme.None, NoGamepad);

        /// <summary>The scheme this player's bindings are filtered to.</summary>
        public ControlScheme Scheme { get; }

        /// <summary>
        /// Which connected gamepad this player holds, or <see cref="NoGamepad"/> when they
        /// are not on one.
        /// </summary>
        public int GamepadIndex { get; }

        /// <summary>Whether this player has anything to play with.</summary>
        public bool IsPaired => Scheme != ControlScheme.None;

        /// <summary>
        /// Deals the available devices out to the players.
        /// </summary>
        /// <param name="playerCount">How many local players there are.</param>
        /// <param name="gamepadCount">How many gamepads are connected.</param>
        /// <param name="keyboardAndMouse">Whether both a keyboard and a mouse are connected.</param>
        /// <returns>
        /// One assignment per player, in slot order. Players nothing was left for come back
        /// <see cref="Unpaired"/>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Gamepads go to the players after the first, in connection order, and only a spare
        /// left over after that reaches the first player. That ordering is deliberate:
        /// plugging a second pad in mid-session hands it to player one and leaves player two
        /// holding the pad they are already holding, rather than swapping the two over.
        /// The cost is that a second pad moves player one off the keyboard, which is the
        /// right default for a game meant to be played on a sofa.
        /// </para>
        /// <para>
        /// There is only ever one keyboard and one mouse, so at most one player can be on
        /// that scheme, and it is the first player - the one sitting at the desk.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="playerCount"/> is negative.
        /// </exception>
        /// <example>
        /// Two players and one pad is the common case, and gives
        /// <c>[KeyboardAndMouse, Gamepad 0]</c>.
        /// </example>
        public static DeviceAssignment[] Solve(int playerCount, int gamepadCount, bool keyboardAndMouse)
        {
            if (playerCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(playerCount));
            }

            var assigned = new DeviceAssignment[playerCount];
            for (int slot = 0; slot < playerCount; slot++)
            {
                assigned[slot] = Unpaired;
            }

            if (playerCount == 0)
            {
                return assigned;
            }

            int dealt = 0;
            for (int slot = keyboardAndMouse ? 1 : 0; slot < playerCount && dealt < gamepadCount; slot++)
            {
                assigned[slot] = new DeviceAssignment(ControlScheme.Gamepad, dealt);
                dealt++;
            }

            if (!keyboardAndMouse)
            {
                return assigned;
            }

            bool spare = dealt < gamepadCount;
            bool everyOtherPlayerHasOne = dealt == playerCount - 1;
            assigned[0] = spare && everyOtherPlayerHasOne && playerCount > 1
                ? new DeviceAssignment(ControlScheme.Gamepad, dealt)
                : new DeviceAssignment(ControlScheme.KeyboardAndMouse, NoGamepad);

            return assigned;
        }

        /// <inheritdoc/>
        public bool Equals(DeviceAssignment other)
            => Scheme == other.Scheme && GamepadIndex == other.GamepadIndex;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is DeviceAssignment other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => ((int)Scheme * 397) ^ GamepadIndex;

        /// <inheritdoc/>
        public override string ToString()
            => GamepadIndex == NoGamepad ? Scheme.ToString() : $"{Scheme} {GamepadIndex}";
    }
}
