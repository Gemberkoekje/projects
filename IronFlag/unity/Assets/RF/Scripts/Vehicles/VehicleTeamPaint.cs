using UnityEngine;
using IronFlag.Core;

namespace IronFlag.Vehicles
{
    /// <summary>
    /// Puts a vehicle on a side by swapping the one material that carries team color.
    /// </summary>
    /// <remarks>
    /// The Blender pipeline exports every team-colored face as a single child object named
    /// <c>TeamTrim</c> with one material on it, precisely so this is a material assignment
    /// and never a mesh swap. There is no green model and no brown model: one model, two
    /// materials, and a third side would cost one more material and no art at all.
    /// </remarks>
    [ExecuteAlways]
    [AddComponentMenu("IronFlag/Vehicle Team Paint")]
    public sealed class VehicleTeamPaint : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Which side this vehicle belongs to.")]
        private Team team = Team.Green;

        [SerializeField]
        [Tooltip("The TeamTrim renderer exported with the model.")]
        private Renderer trimRenderer;

        [SerializeField]
        [Tooltip("Material worn by the green side.")]
        private Material greenMaterial;

        [SerializeField]
        [Tooltip("Material worn by the brown side.")]
        private Material brownMaterial;

        /// <summary>
        /// Which side this vehicle belongs to. Assigning repaints it immediately.
        /// </summary>
        public Team Team
        {
            get => team;
            set
            {
                team = value;
                Apply();
            }
        }

        /// <summary>
        /// Points this component at the renderer and materials it swaps between.
        /// </summary>
        /// <param name="renderer">The model's team-trim renderer.</param>
        /// <param name="green">Material worn by the green side.</param>
        /// <param name="brown">Material worn by the brown side.</param>
        /// <remarks>Called by the prefab builder, which finds the trim renderer in the model.</remarks>
        public void Configure(Renderer renderer, Material green, Material brown)
        {
            trimRenderer = renderer;
            greenMaterial = green;
            brownMaterial = brown;
        }

        /// <summary>
        /// Applies the current team's material to the trim renderer.
        /// </summary>
        /// <remarks>
        /// A no-op when the trim renderer or the material for the current side is missing,
        /// which leaves the model wearing what Blender exported - a neutral grey placeholder
        /// - rather than nothing at all.
        /// </remarks>
        public void Apply()
        {
            if (trimRenderer == null)
            {
                return;
            }

            Material paint = MaterialFor(team);
            if (paint == null)
            {
                return;
            }

            trimRenderer.sharedMaterial = paint;
        }

        private void OnEnable() => Apply();

        private void OnValidate() => Apply();

        private Material MaterialFor(Team side)
        {
            switch (side)
            {
                case Team.Green:
                    return greenMaterial;
                case Team.Brown:
                    return brownMaterial;
                default:
                    return null;
            }
        }
    }
}
