using UnityEngine;

namespace IronFlag.UI
{
    /// <summary>
    /// How anything on the interface gets from one value to the next: the easing every bar
    /// slides by, and the pulse anything in trouble breathes with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Static arithmetic rather than an animator or a coroutine, and every function here is
    /// a pure one taking the time as an argument. That is the same choice
    /// <see cref="IronFlag.Combat.Explosion"/> makes and for the same reason: a curve asset
    /// is a thing nobody can review in a diff, and a HUD whose motion is a closed-form
    /// function of the numbers behind it is one a test can assert about without waiting for
    /// a frame.
    /// </para>
    /// <para>
    /// <strong>Everything here is driven by scaled time on purpose.</strong> The pause panel
    /// sets <see cref="Time.timeScale"/> to zero and the interface keeps being drawn
    /// underneath it - <c>LateUpdate</c> does not stop - so a HUD easing on unscaled time
    /// would carry on sliding and strobing behind a panel that exists to stop the match. A
    /// paused gauge is a still picture of the moment the player paused, which is what they
    /// paused to look at. The menus are the exception and say so where they use it: a panel
    /// that only ever appears while the game is stopped has to move on unscaled time or it
    /// never moves at all.
    /// </para>
    /// </remarks>
    public static class HudMotion
    {
        /// <summary>How fast a bar chases the number behind it, per second.</summary>
        /// <remarks>
        /// Two thirds of the way there in eighty milliseconds and settled inside a quarter of
        /// a second. Fast enough that a shell landing reads as an instant drop rather than as
        /// a slide, slow enough that the eye is given a direction to read: the point of easing
        /// a gauge at all is that a bar which is <em>falling</em> looks different from one that
        /// is merely short, and a bar that snaps never looks like either.
        /// </remarks>
        public const float BarRate = 12.0f;

        /// <summary>How fast a panel fades in or out, per second.</summary>
        /// <remarks>
        /// Quicker than a bar. A gauge is being watched and a panel is being waited for, and
        /// a menu that takes a visible moment to answer a button is a menu that feels broken
        /// rather than smooth.
        /// </remarks>
        public const float FadeRate = 16.0f;

        /// <summary>How long one breath of the alarm pulse takes, in seconds.</summary>
        /// <remarks>
        /// Slower than a heartbeat and much slower than a blink. A fast flash is an error
        /// message; this is a gauge saying it is nearly empty, which the player is meant to
        /// notice out of the corner of an eye and then keep driving.
        /// </remarks>
        public const float AlarmPeriod = 0.9f;

        /// <summary>How far the alarm pulse dims at the bottom of its breath.</summary>
        public const float AlarmDepth = 0.35f;

        /// <summary>How close counts as arrived, so an approach terminates.</summary>
        /// <remarks>
        /// An exponential approach never actually gets there, and a bar left a thousandth
        /// short of full is a bar that redraws its mesh every frame for the rest of the match.
        /// </remarks>
        public const float Settled = 0.001f;

        /// <summary>
        /// Moves a value towards another one, by a fraction of what is left.
        /// </summary>
        /// <param name="current">Where the value is now.</param>
        /// <param name="target">Where it is heading.</param>
        /// <param name="rate">How fast it closes the gap, per second.</param>
        /// <param name="deltaTime">Seconds since this was last called.</param>
        /// <returns>The value one step closer, or exactly the target once it is near enough.</returns>
        /// <remarks>
        /// <para>
        /// Exponential rather than a fixed step per second, which buys two things worth
        /// having. A bar with a long way to go moves faster than one with a short way to go,
        /// so a vehicle taking half its armour off in one hit reads as violent and topping up
        /// the last drop of fuel at a depot reads as gentle - without either being written
        /// down anywhere. And the result does not depend on the frame rate: two half-steps
        /// land where one whole step does, which is what lets a test assert about it.
        /// </para>
        /// <para>
        /// Nothing moves on a zero or negative step. That is the pause case rather than a
        /// guard against nonsense - see the class summary - and it has to return the current
        /// value rather than the target, or pausing would finish every animation on screen.
        /// </para>
        /// </remarks>
        public static float Ease(float current, float target, float rate, float deltaTime)
        {
            if (deltaTime <= 0.0f || rate <= 0.0f)
            {
                return current;
            }

            if (Mathf.Abs(target - current) <= Settled)
            {
                return target;
            }

            return Mathf.Lerp(current, target, 1.0f - Mathf.Exp(-rate * deltaTime));
        }

        /// <summary>
        /// Returns where in its breath a pulse is.
        /// </summary>
        /// <param name="time">Seconds on whichever clock the caller is using.</param>
        /// <param name="period">How long one whole breath takes, in seconds.</param>
        /// <returns>Nought at the bottom of the breath, one at the top.</returns>
        /// <remarks>
        /// A cosine rather than a triangle or a square. A pulse that reaches its ends sharply
        /// reads as blinking, and a blinking gauge is one the player switches off in their
        /// head after a minute.
        /// </remarks>
        public static float Pulse(float time, float period)
        {
            if (period <= 0.0f)
            {
                return 1.0f;
            }

            return 0.5f - (0.5f * Mathf.Cos(time * 2.0f * Mathf.PI / period));
        }

        /// <summary>
        /// Returns a colour dimmed and brightened on the alarm pulse.
        /// </summary>
        /// <param name="colour">The colour at the top of the breath.</param>
        /// <param name="time">Seconds on whichever clock the caller is using.</param>
        /// <returns>The same colour, breathing.</returns>
        /// <remarks>
        /// Brightness only, never alpha and never hue. A gauge that faded would read as
        /// switching off and one that changed colour would read as changing meaning; the only
        /// thing this is allowed to say is "still here, still empty."
        /// </remarks>
        public static Color Flash(Color colour, float time)
        {
            float lit = Mathf.Lerp(1.0f - AlarmDepth, 1.0f, Pulse(time, AlarmPeriod));
            return new Color(colour.r * lit, colour.g * lit, colour.b * lit, colour.a);
        }
    }
}
