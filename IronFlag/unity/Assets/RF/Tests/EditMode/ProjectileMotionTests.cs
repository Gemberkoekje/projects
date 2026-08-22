using NUnit.Framework;
using UnityEngine;
using IronFlag.Combat;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// Covers the flight of a round without firing one.
    /// </summary>
    /// <remarks>
    /// The whole point of keeping the ballistics pure is that they can be checked like this
    /// - no scene, no rigidbody, no play mode - and the interesting property is the one in
    /// <see cref="StepsAtAnySizeLandInTheSamePlace"/>: a grenade whose range depended on the
    /// frame rate would be a balance number nobody could rely on.
    /// </remarks>
    public sealed class ProjectileMotionTests
    {
        [Test]
        public void AFlatRoundKeepsItsSpeedAndItsHeight()
        {
            var start = new ProjectileState(Vector3.zero, Vector3.forward * 90.0f);

            ProjectileState after = ProjectileMotion.Step(start, 0.0f, 0.5f);

            Assert.That(after.Position.z, Is.EqualTo(45.0f).Within(0.0001f));
            Assert.That(after.Position.y, Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(after.Speed, Is.EqualTo(90.0f).Within(0.0001f));
        }

        [Test]
        public void AFallingRoundGathersDownwardSpeed()
        {
            var start = new ProjectileState(Vector3.zero, Vector3.forward * 20.0f);

            ProjectileState after = ProjectileMotion.Step(start, 10.0f, 2.0f);

            Assert.That(after.Velocity.y, Is.EqualTo(-20.0f).Within(0.0001f));
            Assert.That(after.Position.y, Is.EqualTo(-20.0f).Within(0.0001f));
        }

        /// <summary>
        /// Constant acceleration has a closed form and the step uses it, so a hundred small
        /// steps land exactly where one big one does. An Euler integration would not.
        /// </summary>
        [Test]
        public void StepsAtAnySizeLandInTheSamePlace()
        {
            var start = new ProjectileState(Vector3.zero, new Vector3(0.0f, 12.0f, 24.0f));

            ProjectileState once = ProjectileMotion.Step(start, 22.0f, 1.0f);

            ProjectileState many = start;
            for (int step = 0; step < 100; step++)
            {
                many = ProjectileMotion.Step(many, 22.0f, 0.01f);
            }

            Assert.That(many.Position.x, Is.EqualTo(once.Position.x).Within(0.001f));
            Assert.That(many.Position.y, Is.EqualTo(once.Position.y).Within(0.001f));
            Assert.That(many.Position.z, Is.EqualTo(once.Position.z).Within(0.001f));
            Assert.That(many.Velocity.y, Is.EqualTo(once.Velocity.y).Within(0.001f));
        }

        [Test]
        public void ALaunchLeavesTheMuzzleAtTheMuzzleSpeed()
        {
            ProjectileState launch = ProjectileMotion.Launch(
                new Vector3(1.0f, 2.0f, 3.0f), Vector3.forward, 40.0f, 0.0f);

            Assert.That(launch.Position, Is.EqualTo(new Vector3(1.0f, 2.0f, 3.0f)));
            Assert.That(launch.Speed, Is.EqualTo(40.0f).Within(0.001f));
            Assert.That(launch.Velocity.y, Is.EqualTo(0.0f).Within(0.001f));
        }

        [Test]
        public void ElevationTiltsTheRoundUpwardsWithoutChangingItsSpeed()
        {
            ProjectileState launch = ProjectileMotion.Launch(
                Vector3.zero, Vector3.forward, 26.0f, 30.0f);

            Assert.That(launch.Speed, Is.EqualTo(26.0f).Within(0.001f));
            Assert.That(launch.Velocity.y, Is.EqualTo(13.0f).Within(0.001f));
            Assert.That(launch.Velocity.z, Is.GreaterThan(launch.Velocity.y));
        }

        /// <summary>
        /// A helicopter banking through a turn tilts the node its muzzle hangs off. The gun
        /// must not follow it down, or every shot fired in a turn goes into the ground.
        /// </summary>
        [Test]
        public void ABarrelPointingAtTheGroundStillShootsAlongIt()
        {
            ProjectileState launch = ProjectileMotion.Launch(
                Vector3.zero, new Vector3(0.0f, -1.0f, 1.0f), 50.0f, 0.0f);

            Assert.That(launch.Velocity.y, Is.EqualTo(0.0f).Within(0.001f));
            Assert.That(launch.Velocity.z, Is.EqualTo(50.0f).Within(0.001f));
        }

        [Test]
        public void ARoundWithNowhereToGoDoesNotGoAnywhere()
        {
            ProjectileState launch = ProjectileMotion.Launch(Vector3.zero, Vector3.up, 50.0f, 0.0f);

            Assert.That(launch.Velocity, Is.EqualTo(Vector3.zero));
        }

        /// <summary>
        /// The closed-form range and a simulated arc have to agree, because the weapon table
        /// balances the jeep on the first one and the player only ever sees the second.
        /// </summary>
        [Test]
        public void TheSolvedRangeIsWhereTheArcActuallyLands()
        {
            const float speed = 26.0f;
            const float elevation = 22.0f;
            const float drop = 22.0f;

            float solved = ProjectileMotion.BallisticRange(speed, elevation, drop);

            ProjectileState round = ProjectileMotion.Launch(Vector3.zero, Vector3.forward, speed, elevation);
            ProjectileState previous = round;
            while (round.Position.y >= 0.0f || round.Velocity.y > 0.0f)
            {
                previous = round;
                round = ProjectileMotion.Step(round, drop, 0.001f);
            }

            Assert.That(previous.Position.z, Is.EqualTo(solved).Within(0.05f));
        }

        /// <summary>
        /// The weapon table authors a reach and solves the angle from it, so the solver and
        /// the range formula have to be exact inverses. If they are not, the arc a player
        /// watches lands somewhere other than where the weapon says it reaches.
        /// </summary>
        [Test]
        public void TheSolvedAngleReachesExactlyTheDistanceItWasAskedFor()
        {
            const float speed = 28.0f;
            const float drop = 22.0f;

            foreach (float wanted in new[] { 6.0f, 14.0f, 25.0f })
            {
                float elevation = ProjectileMotion.LaunchElevation(speed, wanted, drop);

                Assert.That(
                    ProjectileMotion.BallisticRange(speed, elevation, drop),
                    Is.EqualTo(wanted).Within(0.01f),
                    $"at {wanted} m");
            }
        }

        /// <summary>
        /// Every reachable distance has a flat solution and a steep one. A round that climbs
        /// out of the frame on its way to something fourteen metres away is not a thing
        /// anyone can aim, so the solver has to return the flat one.
        /// </summary>
        [Test]
        public void TheSolvedAngleIsTheFlatOneOfTheTwo()
        {
            Assert.That(ProjectileMotion.LaunchElevation(28.0f, 14.0f, 22.0f), Is.LessThan(45.0f));
        }

        [Test]
        public void AnAngleIsOnlySolvedWhenThereIsOneToSolve()
        {
            Assert.That(
                ProjectileMotion.LaunchElevation(28.0f, 14.0f, 0.0f),
                Is.EqualTo(0.0f),
                "a round that does not fall cannot be lobbed");
            Assert.That(
                ProjectileMotion.LaunchElevation(5.0f, 400.0f, 22.0f),
                Is.EqualTo(0.0f),
                "nothing can be thrown that far at that speed");
        }

        [Test]
        public void ARoundThatDoesNotFallHasNoBallisticRange()
        {
            Assert.That(ProjectileMotion.BallisticRange(90.0f, 0.0f, 0.0f), Is.EqualTo(0.0f));
            Assert.That(ProjectileMotion.BallisticRange(0.0f, 22.0f, 22.0f), Is.EqualTo(0.0f));
        }
    }
}
