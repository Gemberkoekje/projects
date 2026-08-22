using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Vehicles;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// Covers the health pool: what takes hit points off, what does not, and what happens
    /// when they run out.
    /// </summary>
    /// <remarks>
    /// No scene and no projectiles. A round arriving is one call to
    /// <see cref="VehicleHealth.TakeDamage"/>, so everything about who is allowed to hurt
    /// whom can be settled here; the play-mode tests then only have to show that a round
    /// actually makes that call.
    /// </remarks>
    public sealed class VehicleHealthTests
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
        }

        [Test]
        public void AFreshVehicleIsAtFullHealth()
        {
            VehicleHealth hull = Create(Team.Green, 100.0f);

            Assert.That(hull.HitPoints, Is.EqualTo(100.0f));
            Assert.That(hull.Fraction, Is.EqualTo(1.0f));
            Assert.That(hull.IsDestroyed, Is.False);
        }

        [Test]
        public void AnEnemyRoundTakesHitPointsOff()
        {
            VehicleHealth hull = Create(Team.Green, 100.0f);

            float taken = hull.TakeDamage(34.0f, Team.Brown);

            Assert.That(taken, Is.EqualTo(34.0f));
            Assert.That(hull.HitPoints, Is.EqualTo(66.0f));
            Assert.That(hull.Fraction, Is.EqualTo(0.66f).Within(0.001f));
        }

        [Test]
        public void ThereIsNoFriendlyFire()
        {
            VehicleHealth hull = Create(Team.Green, 100.0f);

            Assert.That(hull.TakeDamage(34.0f, Team.Green), Is.EqualTo(0.0f));
            Assert.That(hull.HitPoints, Is.EqualTo(100.0f));
        }

        /// <summary>
        /// A vehicle that reached the field without a side is an authoring mistake, and
        /// damage that silently goes nowhere is much harder to notice than a vehicle
        /// everybody can shoot.
        /// </summary>
        [Test]
        public void AVehicleOnNobodysSideIsFairGame()
        {
            VehicleHealth hull = Create(Team.None, 100.0f);

            Assert.That(hull.TakeDamage(10.0f, Team.Green), Is.EqualTo(10.0f));
            Assert.That(hull.TakeDamage(10.0f, Team.Brown), Is.EqualTo(10.0f));
        }

        [Test]
        public void RunningOutOfHitPointsRaisesDestroyedExactlyOnce()
        {
            VehicleHealth hull = Create(Team.Green, 50.0f);
            int deaths = 0;
            hull.Destroyed += _ => deaths++;

            hull.TakeDamage(30.0f, Team.Brown);
            Assert.That(deaths, Is.Zero, "it died early");

            hull.TakeDamage(30.0f, Team.Brown);
            hull.TakeDamage(30.0f, Team.Brown);

            Assert.That(deaths, Is.EqualTo(1));
            Assert.That(hull.HitPoints, Is.EqualTo(0.0f));
            Assert.That(hull.IsDestroyed, Is.True);
        }

        /// <summary>
        /// A blast that catches a burning wreck must not send its pilot back to the bunker a
        /// second time, which is what a second <c>Destroyed</c> would do.
        /// </summary>
        [Test]
        public void AWreckAbsorbsNothingMore()
        {
            VehicleHealth hull = Create(Team.Green, 50.0f);
            hull.TakeDamage(50.0f, Team.Brown);

            Assert.That(hull.TakeDamage(50.0f, Team.Brown), Is.EqualTo(0.0f));
        }

        [Test]
        public void RepairingPutsTheVehicleBackTogether()
        {
            VehicleHealth hull = Create(Team.Green, 50.0f);
            bool repaired = false;
            hull.Repaired += _ => repaired = true;

            hull.TakeDamage(50.0f, Team.Brown);
            hull.Repair();

            Assert.That(hull.IsDestroyed, Is.False);
            Assert.That(hull.HitPoints, Is.EqualTo(50.0f));
            Assert.That(repaired, Is.True);
        }

        [Test]
        public void HarmlessDamageIsHarmless()
        {
            VehicleHealth hull = Create(Team.Green, 50.0f);

            Assert.That(hull.TakeDamage(0.0f, Team.Brown), Is.EqualTo(0.0f));
            Assert.That(hull.TakeDamage(-10.0f, Team.Brown), Is.EqualTo(0.0f));
            Assert.That(hull.HitPoints, Is.EqualTo(50.0f));
        }

        [Test]
        public void AVehicleWearsTheSideItIsPaintedIn()
        {
            VehicleHealth hull = Create(Team.Brown, 50.0f);

            Assert.That(hull.Team, Is.EqualTo(Team.Brown));

            hull.GetComponent<VehicleTeamPaint>().Team = Team.Green;

            Assert.That(hull.Team, Is.EqualTo(Team.Green), "the paint and the pool disagree");
        }

        /// <summary>
        /// Builds a bare health pool on a side, with no explosion to spawn.
        /// </summary>
        /// <param name="side">Side to paint it.</param>
        /// <param name="pool">Hit points when fresh.</param>
        /// <returns>The health component.</returns>
        private VehicleHealth Create(Team side, float pool)
        {
            var host = new GameObject("Target");
            spawned.Add(host);

            host.AddComponent<VehicleTeamPaint>().Team = side;

            VehicleHealth hull = host.AddComponent<VehicleHealth>();
            hull.Configure(pool, null, 3.0f);
            return hull;
        }
    }
}
