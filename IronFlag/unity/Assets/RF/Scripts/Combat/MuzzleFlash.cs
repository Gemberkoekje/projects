using UnityEngine;

namespace IronFlag.Combat
{
    /// <summary>
    /// The light a gun makes when it goes off: a stub of flame at the barrel mouth, full
    /// size the instant it appears and gone again in a couple of frames.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The opposite curve to <see cref="Explosion"/>, and that is the whole point of it
    /// being a separate component rather than a small explosion. A detonation swells and
    /// then fades, because something is expanding; a muzzle flash is already at full extent
    /// before the eye finds it and collapses from there, because nothing is expanding - the
    /// propellant has finished burning by the time the round has cleared the barrel. Give a
    /// flash the explosion's swell and it reads as a shell going off in the gun.
    /// </para>
    /// <para>
    /// Hand-coded rather than a particle system, for the reason
    /// <see cref="IronFlag.Destruction.DebrisBurst"/> gives: this one fires on <em>every
    /// shot</em>, eight times a second from a chaingun and from every emplacement on the
    /// map at once, so it is the last thing in the game that should be an asset nobody can
    /// review in a diff.
    /// </para>
    /// <para>
    /// It hangs off the muzzle rather than standing where the muzzle was. Sixty-five
    /// milliseconds is long enough for a strafing helicopter to travel a metre and
    /// a half, and a flash left behind in the air is a flash visibly detached from
    /// the gun that made it. Being a child also means a vehicle that is destroyed
    /// mid-flash takes its own flash with it.
    /// </para>
    /// <para>
    /// <see cref="Flare"/> is the whole animation - static and side-effect free, like
    /// <see cref="Explosion.Scale"/> - so what a flash looks like can be checked without
    /// firing a gun.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Muzzle Flash")]
    public sealed class MuzzleFlash : MonoBehaviour
    {
        /// <summary>How many calibres long a flash is drawn.</summary>
        /// <remarks>
        /// The one number that turns a weapon into a flash, kept here rather than in the
        /// weapon table because it is a drawing decision and not a balance one: no shot
        /// lands differently for it. Five puts the chaingun's flash at half a metre and the
        /// rocket's at 1.1 m, which is a spread you can tell apart from thirty-four metres
        /// up without the rocket's reaching further than the vehicle firing it is long.
        /// </remarks>
        public const float Calibres = 5.0f;

        /// <summary>How wide a flash is drawn, as a fraction of its length.</summary>
        /// <remarks>
        /// Measured off a render rather than predicted, and fatter than the first pass. A
        /// thin lance on the end of a thin barrel read as a lollipop on a stick; the game's
        /// camera looks down at 58 degrees, and from up there a flash is mostly a bright
        /// patch at the muzzle rather than a shape pointing anywhere. It stays well under
        /// one, though - a flash as wide as it is long is a ball, and a ball at the end of a
        /// gun is the explosion this deliberately is not.
        /// </remarks>
        public const float Stoutness = 0.55f;

        [SerializeField]
        [Tooltip("The flame that collapses. Scaled by this component; do not scale it by hand.")]
        private Transform flame;

        [SerializeField]
        [Tooltip("Point light that pulses with the flame. Optional.")]
        private Light glow;

        [SerializeField]
        [Tooltip("Seconds from the shot to gone.")]
        private float duration = 0.065f;

        [SerializeField]
        [Tooltip("Length of the flame at full extent, in metres.")]
        private float length = 0.9f;

        [SerializeField]
        [Tooltip("Light intensity at the instant of the shot.")]
        private float peakIntensity = 2.5f;

        private float elapsed;

        /// <summary>Seconds from the shot to gone.</summary>
        public float Duration => duration;

        /// <summary>Length of the flame at full extent, in metres.</summary>
        public float Length => length;

        /// <summary>
        /// Lights a flash at the end of a barrel.
        /// </summary>
        /// <param name="prefab">Muzzle flash prefab to instantiate; null does nothing.</param>
        /// <param name="muzzle">Point the rounds leave from, which the flash hangs off.</param>
        /// <param name="calibre">Radius of the round being fired, in metres.</param>
        /// <returns>The flash, or <c>null</c> when there was nothing to spawn it from.</returns>
        /// <remarks>
        /// Null-tolerant on both counts, for the reason <see cref="Explosion.Spawn"/> is: a
        /// gun assembled in a test has no flash prefab bound and no model to find a muzzle
        /// on, and pulling its trigger should still put a round in the air.
        /// </remarks>
        public static MuzzleFlash Spawn(MuzzleFlash prefab, Transform muzzle, float calibre)
        {
            if (prefab == null || muzzle == null)
            {
                return null;
            }

            MuzzleFlash flash = Instantiate(prefab, muzzle.position, muzzle.rotation, muzzle);
            flash.length = Mathf.Max(0.05f, calibre * Calibres);
            return flash;
        }

        /// <summary>
        /// Points this component at the parts it animates.
        /// </summary>
        /// <param name="cone">The flame that collapses.</param>
        /// <param name="light">Point light to pulse with it, or null for no light.</param>
        /// <param name="seconds">Seconds from the shot to gone.</param>
        /// <param name="flameLength">Length of the flame at full extent, in metres.</param>
        /// <remarks>Called by the prefab builder.</remarks>
        public void Configure(Transform cone, Light light, float seconds, float flameLength)
        {
            flame = cone;
            glow = light;
            duration = Mathf.Max(0.01f, seconds);
            length = Mathf.Max(0.05f, flameLength);
        }

        /// <summary>
        /// Returns how bright the flash is, as a fraction of full, at one moment.
        /// </summary>
        /// <param name="progress">How far through the flash is, in 0..1.</param>
        /// <returns>A fraction in 0..1, starting at one and falling away.</returns>
        /// <remarks>
        /// Squared rather than linear so that most of the flash is over in the first third
        /// of its life. A linear fade over sixty milliseconds still reads as a lamp being
        /// switched off; this reads as a bang.
        /// </remarks>
        public static float Flare(float progress)
        {
            float left = 1.0f - Mathf.Clamp01(progress);
            return left * left;
        }

        /// <summary>
        /// Draws the flash as it looks at one moment of its life.
        /// </summary>
        /// <param name="progress">How far through the flash is, in 0..1.</param>
        /// <remarks>
        /// Everything <c>Update</c> does, and public so it can be done without one running.
        /// <c>Update</c> does not tick outside play mode, so a flash dropped into a scene by
        /// the preview builder would otherwise sit at the zero size its prefab is saved at -
        /// which is to say it would be invisible in exactly the picture that exists to show
        /// it. The same reason <see cref="Flare"/> is static: this animation should be
        /// checkable without firing a gun.
        /// </remarks>
        public void PoseAt(float progress)
        {
            float bright = Flare(progress);

            if (flame != null)
            {
                float across = length * Stoutness * bright;
                flame.localScale = new Vector3(across, across, length * bright);
                // Pushed forward by half its own length so the flame grows out of the barrel
                // mouth rather than back through it: the muzzle point is the far end of the
                // barrel, and a sphere centred on it is half inside the gun.
                flame.localPosition = new Vector3(0.0f, 0.0f, length * bright * 0.5f);
            }

            if (glow != null)
            {
                // Deliberately short. The first pass gave the light four barrel-lengths of
                // reach at an explosion's intensity, and what it drew was not a flash on the
                // gun but the whole flank of the tank lit up like a floodlit wall - the
                // flame itself was the small white dot lost inside it. A muzzle flash lights
                // the barrel and about a metre of what is in front of it, and nothing else.
                glow.range = Mathf.Max(1.0f, length * 1.6f);
                glow.intensity = peakIntensity * bright;
            }
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / duration;

            if (progress >= 1.0f)
            {
                Destroy(gameObject);
                return;
            }

            PoseAt(progress);
        }
    }
}
