using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Vehicles;
using IronFlag.Vfx;

namespace IronFlag.Tests.PlayMode
{
    /// <summary>
    /// What damage smoke does over a hull's life: nothing while it is healthy, a plume once
    /// it is half gone, and a column left standing where it died.
    /// </summary>
    /// <remarks>
    /// The rule itself is checked in the edit-mode suite, which can ask
    /// <c>DamageSmoke.ShouldSmoke</c> directly. What needs a running scene is the part the
    /// rule cannot answer: that the component is actually watching the hull, that it notices
    /// a repair as well as a hit, and that the column it spawns survives the wreck being
    /// taken off the field - which is the one behaviour here that would be invisible in every
    /// still and obvious in every match.
    /// </remarks>
    public sealed class SmokeTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void CleanUp()
        {
            foreach (GameObject item in spawned)
            {
                if (item != null)
                {
                    Object.DestroyImmediate(item);
                }
            }

            spawned.Clear();

            // Columns are spawned detached, so they are nobody's children to clean up with.
            foreach (ParticleBurst burst in Object.FindObjectsByType<ParticleBurst>(
                FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(burst.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator AHullSmokesOnceItIsHalfGoneAndStopsWhenItIsPatchedUp()
        {
            VehicleHealth hull = CreateHull(Team.Green, out DamageSmoke smoke);
            yield return null;

            Assert.That(smoke.IsSmoking, Is.False, "a fresh hull is already smoking");

            hull.TakeDamage(hull.MaxHitPoints * 0.6f, Team.Brown);
            yield return null;

            Assert.That(smoke.IsSmoking, Is.True, "a hull at forty per cent is not smoking");

            hull.Repair();
            yield return null;

            Assert.That(smoke.IsSmoking, Is.False, "a repaired hull is still smoking");
        }

        /// <summary>
        /// A scratch is not damage worth showing. The threshold is what keeps the map from
        /// being a field of smouldering vehicles the first time anybody fires a chaingun.
        /// </summary>
        [UnityTest]
        public IEnumerator AHullThatHasOnlyBeenScratchedDoesNotSmoke()
        {
            VehicleHealth hull = CreateHull(Team.Green, out DamageSmoke smoke);
            yield return null;

            hull.TakeDamage(hull.MaxHitPoints * 0.2f, Team.Brown);
            yield return null;

            Assert.That(smoke.IsSmoking, Is.False);
        }

        /// <summary>
        /// The whole reason the column is a separate, detached prefab rather than another
        /// emitter on the hull: a wrecked vehicle is switched off and sent back to its bunker
        /// within the second, and smoke that belonged to it would leave with it.
        /// </summary>
        [UnityTest]
        public IEnumerator AWreckLeavesItsSmokeBehindWhenTheHullGoesHome()
        {
            VehicleHealth hull = CreateHull(Team.Green, out DamageSmoke smoke);
            yield return null;

            hull.SelfDestruct();
            yield return null;

            ParticleBurst[] columns = Object.FindObjectsByType<ParticleBurst>(
                FindObjectsSortMode.None);
            Assert.That(columns.Length, Is.EqualTo(1), "a wreck threw up no smoke column");
            Assert.That(smoke.IsSmoking, Is.False, "a wreck is still running its damage plume");

            ParticleBurst column = columns[0];
            Assert.That(
                column.transform.IsChildOf(hull.transform),
                Is.False,
                "the column belongs to the wreck and will be taken away with it");

            hull.gameObject.SetActive(false);
            yield return null;

            Assert.That(column == null, Is.False, "the column went home with the wreck");
        }

        /// <summary>
        /// One death, one column. The check runs for several frames because the component
        /// polls: a latch that was not held would put up a fresh column every frame for as
        /// long as the wreck sat there.
        /// </summary>
        [UnityTest]
        public IEnumerator AWreckThrowsUpExactlyOneColumnHoweverLongItLiesThere()
        {
            VehicleHealth hull = CreateHull(Team.Green, out DamageSmoke _);
            yield return null;

            hull.SelfDestruct();

            for (int frame = 0; frame < 5; frame++)
            {
                yield return null;
            }

            Assert.That(
                Object.FindObjectsByType<ParticleBurst>(FindObjectsSortMode.None).Length,
                Is.EqualTo(1));
        }

        /// <summary>
        /// Builds a hull that can be hurt and can smoke about it.
        /// </summary>
        /// <param name="side">Side it belongs to, so a hostile shot can land on it.</param>
        /// <param name="smoke">The smoke component, already wired.</param>
        /// <returns>The health pool.</returns>
        /// <remarks>
        /// Assembled by hand rather than off the vehicle prefab, because the prefab lives
        /// behind the editor assembly and because what is under test is the component rather
        /// than the builder - the edit-mode suite is what checks every vehicle actually
        /// carries one. The plume is a bare particle system and the column a bare object with
        /// a burst on it; neither has to look like anything to be started, stopped or counted.
        /// </remarks>
        private VehicleHealth CreateHull(Team side, out DamageSmoke smoke)
        {
            var host = new GameObject("Hull");
            host.SetActive(false);
            spawned.Add(host);

            host.AddComponent<VehicleTeamPaint>().Team = side;
            VehicleHealth hull = host.AddComponent<VehicleHealth>();
            hull.Configure(VehicleTuning.For(VehicleKind.Tank).HitPoints, null, 3.0f);

            var plumeNode = new GameObject("Plume");
            plumeNode.transform.SetParent(host.transform, false);
            ParticleSystem plume = plumeNode.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = plume.main;
            main.playOnAwake = false;
            main.loop = true;

            var columnHost = new GameObject("Column");
            columnHost.SetActive(false);
            spawned.Add(columnHost);
            columnHost.AddComponent<ParticleSystem>();
            ParticleBurst column = columnHost.AddComponent<ParticleBurst>();
            column.Configure(4.0f, 3.0f);

            smoke = host.AddComponent<DamageSmoke>();
            smoke.Configure(plume, column, 3.0f);

            host.SetActive(true);
            return hull;
        }
    }
}
