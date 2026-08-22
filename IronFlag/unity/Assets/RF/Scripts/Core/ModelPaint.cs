using System;
using UnityEngine;

namespace IronFlag.Core
{
    /// <summary>
    /// Rebinds the material groups Blender exports to the materials the game renders with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Blender pipeline gives every group that Unity needs to <em>change</em> its own
    /// child object with its own material - team trim, headlights, tail lights - and names
    /// those materials with a fixed prefix. Swapping one is therefore a name match rather
    /// than a slot index, which is what lets an asset carry any subset of the groups.
    /// </para>
    /// <para>
    /// This lives in the runtime assembly rather than in the editor's material factory
    /// because the game now builds its own map: <see cref="IronFlag.Levels.LevelBuilder"/>
    /// paints a bunker green in a built player, where the asset database does not exist.
    /// The editor side still owns which material asset each of these <em>is</em>; this owns
    /// the rule about where they go, and there is one of it.
    /// </para>
    /// <para>
    /// A group whose replacement is <c>null</c> is left alone, which is how a prefab keeps
    /// the neutral placeholder Blender exported until something puts it on a side.
    /// </para>
    /// </remarks>
    public static class ModelPaint
    {
        /// <summary>Material name prefix on team-colored geometry, as exported by Blender.</summary>
        public const string AccentPrefix = "RF_TeamAccent";

        /// <summary>Material name prefix on headlight geometry, as exported by Blender.</summary>
        public const string FrontLightPrefix = "RF_LightFront";

        /// <summary>Material name prefix on tail-light geometry, as exported by Blender.</summary>
        public const string RearLightPrefix = "RF_LightRear";

        /// <summary>
        /// Which group an imported material belongs to.
        /// </summary>
        private enum Group
        {
            /// <summary>Not one of the groups: the body, the muzzle, anything else.</summary>
            None = 0,

            /// <summary>Team-colored trim.</summary>
            Team = 1,

            /// <summary>Headlights.</summary>
            FrontLight = 2,

            /// <summary>Tail lights.</summary>
            RearLight = 3,
        }

        /// <summary>
        /// Returns the group an imported material name belongs to.
        /// </summary>
        /// <param name="sourceName">Material name as exported by Blender.</param>
        /// <returns>The group, or <see cref="Group.None"/>.</returns>
        private static Group GroupOf(string sourceName)
        {
            if (string.IsNullOrEmpty(sourceName))
            {
                return Group.None;
            }

            if (sourceName.StartsWith(AccentPrefix, StringComparison.Ordinal))
            {
                return Group.Team;
            }

            if (sourceName.StartsWith(FrontLightPrefix, StringComparison.Ordinal))
            {
                return Group.FrontLight;
            }

            if (sourceName.StartsWith(RearLightPrefix, StringComparison.Ordinal))
            {
                return Group.RearLight;
            }

            return Group.None;
        }

        /// <summary>
        /// Reports whether a renderer wears the team accent Blender exported.
        /// </summary>
        /// <param name="renderer">Renderer to check.</param>
        /// <returns><c>true</c> when one of its materials is the accent placeholder.</returns>
        public static bool IsTeamTrim(Renderer renderer)
        {
            if (renderer == null)
            {
                return false;
            }

            foreach (Material material in renderer.sharedMaterials)
            {
                if (material != null && GroupOf(material.name) == Group.Team)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Rebinds every material group under an object.
        /// </summary>
        /// <param name="instance">Scene object or prefab contents holding imported renderers.</param>
        /// <param name="team">Material for team trim, or <c>null</c> to leave it alone.</param>
        /// <param name="frontLight">Material for headlights, or <c>null</c> to leave them alone.</param>
        /// <param name="rearLight">Material for tail lights, or <c>null</c> to leave them alone.</param>
        /// <example>
        /// <code>
        /// ModelPaint.Apply(bunker, catalog.TrimFor(Team.Green), catalog.FrontLight, catalog.RearLight);
        /// </code>
        /// </example>
        public static void Apply(GameObject instance, Material team, Material frontLight, Material rearLight)
        {
            if (instance == null)
            {
                return;
            }

            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;

                for (int slot = 0; slot < materials.Length; slot++)
                {
                    if (materials[slot] == null)
                    {
                        continue;
                    }

                    Material replacement = Replacement(
                        GroupOf(materials[slot].name), team, frontLight, rearLight);

                    if (replacement != null)
                    {
                        materials[slot] = replacement;
                        changed = true;
                    }
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                }
            }
        }

        private static Material Replacement(
            Group group, Material team, Material frontLight, Material rearLight)
        {
            switch (group)
            {
                case Group.Team:
                    return team;
                case Group.FrontLight:
                    return frontLight;
                case Group.RearLight:
                    return rearLight;
                default:
                    return null;
            }
        }
    }
}
