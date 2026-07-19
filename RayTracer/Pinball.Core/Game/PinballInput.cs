using Pinball.Physics;

namespace Pinball.Game;

/// <summary>
/// A per-tick input snapshot (pinball-plan §6.3): the game host samples the keyboard/gamepad and hands this
/// to <see cref="PinballGame.Tick"/>. Flippers are hold-state (pressed = raised), <see cref="Launch"/> serves
/// the next ball, and <see cref="Nudge"/> is an impulse (kg·m/s) applied once on the tick it is non-zero.
/// A pure value type so the loop stays deterministic and headless-testable.
/// </summary>
/// <param name="LeftFlipper">Left flipper held.</param>
/// <param name="RightFlipper">Right flipper held.</param>
/// <param name="Launch">Serve/launch the ball (only acts when no ball is in play).</param>
/// <param name="Nudge">Nudge impulse (kg·m/s); <c>default</c> = no nudge.</param>
public readonly record struct PinballInput(
    bool LeftFlipper = false,
    bool RightFlipper = false,
    bool Launch = false,
    Vector3D Nudge = default);
