using UnityEngine;

namespace IronFlag.Core
{
    /// <summary>
    /// The car that runs up and down a bunker's shaft. It owns its own height, and everything
    /// that wants it somewhere asks for a height rather than moving it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things drive this car and they want different arrangements. While a player is
    /// choosing, the car <em>follows the highlight</em>: it drops to whichever bay is
    /// selected and waits there, which is the one piece of the select screen that answers
    /// "what did I just press" without any text. While a vehicle is riding out, the car is
    /// carrying it, and the two have to be in exactly the same place on exactly the same
    /// frame - so <see cref="VehicleBay"/> writes the height directly instead of asking.
    /// </para>
    /// <para>
    /// That is the whole reason there are two methods rather than one: an eased target and a
    /// hard set. A single eased target would leave a vehicle riding half a metre above its own
    /// lift, and a single hard set would make the car snap between bays as the highlight
    /// moves.
    /// </para>
    /// <para>
    /// The car has no collider and never carries anything physically. A vehicle riding it is
    /// kinematic and is being placed frame by frame, exactly as it was before there was a car
    /// under it at all.
    /// </para>
    /// </remarks>
    [AddComponentMenu("IronFlag/Bunker Lift")]
    public sealed class BunkerLift : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Metres a second the car travels when it is following the highlight.")]
        private float speed = 9.0f;

        private float target;
        private bool aimed;

        /// <summary>Where the car's deck is now, in world space.</summary>
        public float Height => transform.position.y;

        /// <summary>Where the car is heading, in world space.</summary>
        public float Target => aimed ? target : Height;

        /// <summary>Metres a second the car travels when it is following the highlight.</summary>
        public float Speed => speed;

        /// <summary>Whether the car has arrived where it was last sent.</summary>
        public bool HasArrived => !aimed || Mathf.Abs(Height - target) <= 0.01f;

        /// <summary>
        /// Sets how fast the car travels of its own accord.
        /// </summary>
        /// <param name="metresPerSecond">Travel rate.</param>
        public void Configure(float metresPerSecond) => speed = Mathf.Max(0.01f, metresPerSecond);

        /// <summary>
        /// Sends the car to a height, which it travels to at its own rate.
        /// </summary>
        /// <param name="height">Height of the deck, in world space.</param>
        /// <remarks>
        /// Outside play mode this arrives immediately, because nothing is going to call
        /// <c>Update</c> to carry it there. That is what puts the car in the right place in
        /// the saved scene and in the command-line still, which are the two pictures of this
        /// game taken without the game running.
        /// </remarks>
        public void SendTo(float height)
        {
            target = height;
            aimed = true;

            if (!Application.isPlaying)
            {
                Place(height);
            }
        }

        /// <summary>
        /// Puts the car at a height this instant.
        /// </summary>
        /// <param name="height">Height of the deck, in world space.</param>
        /// <remarks>
        /// What a ride out uses, every frame of it. It also clears the eased target, so a car
        /// that was on its way somewhere when a vehicle boarded it does not carry on
        /// travelling afterwards.
        /// </remarks>
        public void Snap(float height)
        {
            target = height;
            aimed = true;
            Place(height);
        }

        private void Update()
        {
            if (!aimed)
            {
                return;
            }

            Place(Mathf.MoveTowards(Height, target, Mathf.Max(0.01f, speed) * Time.deltaTime));
        }

        private void Place(float height)
        {
            Vector3 at = transform.position;
            transform.position = new Vector3(at.x, height, at.z);
        }
    }
}
