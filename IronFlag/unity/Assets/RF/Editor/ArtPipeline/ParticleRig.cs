using UnityEngine;

namespace IronFlag.Editor.ArtPipeline
{
    /// <summary>
    /// The one place that knows how to build a particle system in this project's style.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Particle systems are the first thing in this game that is not a primitive on a
    /// closed-form curve, and the reason they were kept out for nine milestones is written
    /// down in <c>DebrisBurst.cs</c>: <em>a particle system is an asset nobody can review in
    /// a diff.</em> That objection is answered here rather than waived. Every system in the
    /// game is built by this function out of a <see cref="Look"/> - a dozen named numbers -
    /// so what a diff shows is the numbers, and the twenty lines of Unity module API that
    /// turn them into a system are written once instead of once per effect.
    /// </para>
    /// <para>
    /// <strong>Mesh particles, not billboards.</strong> Every particle is a sphere drawn
    /// flat, which is what the rest of this game is made of. A soft photographic puff would
    /// be the one thing in the frame that came from somewhere else - and it would need a
    /// texture, which is the other thing this project does not have. It also means the
    /// material can be one plain white transparent asset for smoke, dust and spray alike:
    /// the shader multiplies by the particle's own colour, so an effect's colour is a field
    /// rather than an asset. See <see cref="GeneratedMaterials.Particle"/>.
    /// </para>
    /// <para>
    /// <strong>Everything simulates in world space.</strong> Smoke that followed the wreck
    /// it came off and dust that followed the wheel that kicked it up are both the same bug,
    /// and it is the default: a system parented to a moving vehicle drags its whole plume
    /// along unless it is told not to.
    /// </para>
    /// </remarks>
    public static class ParticleRig
    {
        /// <summary>
        /// What one effect looks like: the numbers a diff should show.
        /// </summary>
        /// <remarks>
        /// A class of public fields rather than a constructor, for the reason every tuning
        /// table in this project is one - the numbers are read against each other, and a
        /// named field at each call site is what makes a wrong one visible. Defaults are a
        /// small pale puff, so a <see cref="Look"/> that sets nothing still produces
        /// something you can see rather than nothing at all.
        /// </remarks>
        public sealed class Look
        {
            /// <summary>What colour the particles are, before their own fade.</summary>
            public Color Tint = Color.white;

            /// <summary>How opaque a particle is at its brightest, in 0..1.</summary>
            public float Opacity = 0.6f;

            /// <summary>Seconds one particle lives.</summary>
            public float Lifetime = 1.5f;

            /// <summary>How wide a particle starts, in metres.</summary>
            public float StartSize = 0.5f;

            /// <summary>How much wider it ends, as a multiple of where it started.</summary>
            public float Growth = 2.0f;

            /// <summary>Particles emitted per second, or zero for a system that only bursts.</summary>
            public float Rate;

            /// <summary>Particles emitted the instant it plays, or zero for none.</summary>
            public int Burst;

            /// <summary>How fast a particle leaves the emitter, in metres per second.</summary>
            public float Speed = 1.0f;

            /// <summary>Gravity, as a multiple of the world's. Negative rises.</summary>
            public float Fall = -0.15f;

            /// <summary>How wide the emitter is, in metres.</summary>
            public float Radius = 0.3f;

            /// <summary>Half-angle of the cone particles leave through, in degrees.</summary>
            public float ConeAngle = 25.0f;

            /// <summary>Whether particles go outwards along the ground instead of upwards.</summary>
            /// <remarks>
            /// What makes a ring a ring. A splash is two systems - a crown going up and a
            /// ring going out - and this one field is the whole difference between them.
            /// </remarks>
            public bool Flat;

            /// <summary>Whether it starts playing on its own, or waits to be told.</summary>
            public bool PlayOnAwake = true;

            /// <summary>Whether it keeps going round, or emits once and stops.</summary>
            /// <remarks>
            /// A plume that runs while a hull is burning loops; the feed of a smoke column
            /// does not, because a column that fed for ever would be a chimney. Separate
            /// from <see cref="Rate"/> on purpose - the two combinations that look alike are
            /// a looping rate and a one-shot rate, and they are the difference between smoke
            /// that stops and smoke that does not.
            /// </remarks>
            public bool Loops;

            /// <summary>Seconds the emitter runs for, or zero to use one particle's life.</summary>
            public float Duration;

            /// <summary>The most particles it may have alive at once.</summary>
            public int MaxParticles = 50;
        }

        /// <summary>
        /// Builds a particle system on an object and returns it.
        /// </summary>
        /// <param name="host">Object to put the system on. Gets a renderer too.</param>
        /// <param name="look">The numbers.</param>
        /// <returns>The system, already configured and not yet playing.</returns>
        /// <remarks>
        /// The host is expected to be an object of its own rather than the prefab root, so
        /// the emitter can be positioned where the effect comes from - the back of a hull,
        /// the middle of a building - without moving whatever it hangs off.
        /// </remarks>
        public static ParticleSystem Create(GameObject host, Look look)
        {
            ParticleSystem system = host.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = system.main;
            main.duration = Mathf.Max(
                0.05f, look.Duration > 0.0f ? look.Duration : look.Lifetime);
            main.loop = look.Loops;
            main.playOnAwake = look.PlayOnAwake;
            main.startLifetime = look.Lifetime;
            main.startSpeed = look.Speed;
            main.startSize = look.StartSize;
            main.startColor = new Color(look.Tint.r, look.Tint.g, look.Tint.b, look.Opacity);
            main.gravityModifier = look.Fall;
            main.maxParticles = look.MaxParticles;
            // World space, always. See the class remarks: a plume that follows the thing
            // that made it is the single most common way a particle effect looks wrong.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = look.Rate;
            emission.SetBursts(look.Burst > 0
                ? new[] { new ParticleSystem.Burst(0.0f, (short)look.Burst) }
                : new ParticleSystem.Burst[0]);

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.radius = Mathf.Max(0.01f, look.Radius);
            if (look.Flat)
            {
                // A circle emitting along its own plane: every particle leaves outwards and
                // none leaves upwards, which is what draws a ring rather than a dome.
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.arc = 360.0f;
                shape.radiusThickness = 0.0f;
                shape.rotation = new Vector3(90.0f, 0.0f, 0.0f);
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = look.ConeAngle;
                shape.rotation = new Vector3(-90.0f, 0.0f, 0.0f);
            }

            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            fade.color = new ParticleSystem.MinMaxGradient(Dissolve());

            ParticleSystem.SizeOverLifetimeModule swell = system.sizeOverLifetime;
            swell.enabled = true;
            swell.size = new ParticleSystem.MinMaxCurve(
                1.0f, AnimationCurve.Linear(0.0f, 1.0f, 1.0f, Mathf.Max(0.05f, look.Growth)));

            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = SphereMesh();
            renderer.sharedMaterial = GeneratedMaterials.Load(GeneratedMaterials.Particle);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // Sorted so the near puff of a plume draws over the far one. Without it a cloud
            // of transparent spheres flickers as they cross each other.
            renderer.sortMode = ParticleSystemSortMode.Distance;

            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return system;
        }

        /// <summary>
        /// Returns the alpha ramp every particle in this game fades on.
        /// </summary>
        /// <returns>A gradient at full colour throughout, whose alpha rises then falls.</returns>
        /// <remarks>
        /// The rise matters as much as the fall. A particle that is at full opacity on the
        /// frame it is born pops into existence, which is the tell that turns a plume back
        /// into a list of objects; a tenth of its life spent arriving is enough to hide it.
        /// The colour keys are flat because the tint is the particle's own - see the class
        /// remarks.
        /// </remarks>
        private static Gradient Dissolve()
        {
            var ramp = new Gradient();
            ramp.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0.0f),
                    new GradientColorKey(Color.white, 1.0f),
                },
                new[]
                {
                    new GradientAlphaKey(0.0f, 0.0f),
                    new GradientAlphaKey(1.0f, 0.12f),
                    new GradientAlphaKey(0.85f, 0.45f),
                    new GradientAlphaKey(0.0f, 1.0f),
                });

            return ramp;
        }

        /// <summary>
        /// Returns Unity's built-in sphere mesh.
        /// </summary>
        /// <returns>The mesh a primitive sphere is drawn with.</returns>
        /// <remarks>
        /// Taken off a throwaway primitive rather than looked up by name, for the reason
        /// <see cref="GeneratedMaterials.EnsureAssets"/> takes its material template off one:
        /// the built-in resource names have moved between Unity versions and a
        /// <c>GetBuiltinResource</c> that misses returns null silently, which here would be a
        /// particle system that emits nothing visible at all.
        /// </remarks>
        private static Mesh SphereMesh()
        {
            GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            try
            {
                return probe.GetComponent<MeshFilter>().sharedMesh;
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }
    }
}
