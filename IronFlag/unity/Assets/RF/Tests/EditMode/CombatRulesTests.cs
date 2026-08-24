using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Vehicles;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// The small rules combat is built out of: who counts as an enemy, where a bunker puts
    /// the vehicles it deploys, and the shape of an explosion.
    /// </summary>
    /// <remarks>
    /// All three are pure enough to check without a scene running, which is the same reason
    /// the split-screen layout and the camera placement have their own tests. What they have
    /// in common is that each is used from more than one place, so getting one wrong shows
    /// up somewhere far away from the mistake.
    /// </remarks>
    public sealed class CombatRulesTests
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
        public void OppositeSidesAreHostileAndTheSameSideIsNot()
        {
            Assert.That(Teams.IsHostile(Team.Green, Team.Brown), Is.True);
            Assert.That(Teams.IsHostile(Team.Brown, Team.Green), Is.True);
            Assert.That(Teams.IsHostile(Team.Green, Team.Green), Is.False);
            Assert.That(Teams.IsHostile(Team.Brown, Team.Brown), Is.False);
        }

        [Test]
        public void AnythingOnNoSideIsHostileToBothOfThem()
        {
            Assert.That(Teams.IsHostile(Team.Green, Team.None), Is.True);
            Assert.That(Teams.IsHostile(Team.None, Team.Brown), Is.True);
            Assert.That(Teams.IsHostile(Team.None, Team.None), Is.False);
        }

        [Test]
        public void TheBunkerDeploysFromTheLiftItsModelCarries()
        {
            TeamBunker bunker = Create(Team.Green, Vector3.zero, 0.0f, Modelled);

            Assert.That(bunker.IsModelled, Is.True, "the markers were not found");
            Assert.That(bunker.LiftPoint.z, Is.EqualTo(5.2f).Within(0.001f), "the lift is not out front");
            Assert.That(bunker.LiftPoint.y, Is.EqualTo(0.25f).Within(0.001f), "the lift is not on the ground");
            Assert.That(bunker.HelipadPoint.y, Is.EqualTo(3.9f).Within(0.001f), "the pad is not on the roof");
        }

        /// <summary>
        /// One branch, and this is it: everything that flies leaves from the roof and
        /// everything else out of the door.
        /// </summary>
        [Test]
        public void TheHelicopterLeavesFromTheRoofAndTheRestFromTheLift()
        {
            TeamBunker bunker = Create(Team.Green, Vector3.zero, 0.0f, Modelled);

            Assert.That(
                bunker.DeployPointFor(VehicleKind.Helicopter),
                Is.EqualTo(bunker.HelipadPoint));

            foreach (VehicleKind kind in new[] { VehicleKind.Jeep, VehicleKind.Tank, VehicleKind.Asv })
            {
                Assert.That(bunker.DeployPointFor(kind), Is.EqualTo(bunker.LiftPoint), kind.ToString());
            }
        }

        /// <summary>
        /// The two sides face each other, so a bunker turned around has to put its lift on
        /// the field rather than behind itself.
        /// </summary>
        [Test]
        public void ATurnedBunkerDeploysTowardsTheMapAsWell()
        {
            TeamBunker bunker = Create(Team.Brown, new Vector3(0.0f, 0.0f, 52.0f), 180.0f, Modelled);

            Assert.That(bunker.LiftPoint.z, Is.EqualTo(46.8f).Within(0.001f));
        }

        /// <summary>
        /// A bunker with no model behind it is what every test builds, and it still has to
        /// deploy somewhere in front of itself rather than inside itself.
        /// </summary>
        [Test]
        public void ABunkerWithNoModelStillHasSomewhereToDeployFrom()
        {
            TeamBunker bunker = Create(Team.Green, Vector3.zero, 0.0f, false);

            Assert.That(bunker.IsModelled, Is.False);
            Assert.That(bunker.LiftPoint.z, Is.GreaterThan(0.0f), "the fallback lift is not out front");
            Assert.That(bunker.HelipadPoint.y, Is.GreaterThan(0.0f), "the fallback pad is not up top");
        }

        /// <summary>
        /// The column has to reach from under the shortest thing on the field to over the
        /// tallest. A ceiling the helicopter could climb past would turn its altitude into a
        /// hiding place, and a floor above a jeep's roof would put the tank's shells back
        /// over the top of it.
        /// </summary>
        [Test]
        public void TheCombatColumnCoversEverythingThatCanBeShotAt()
        {
            CombatPlane.Column(new Vector3(3.0f, 7.0f, -4.0f), out Vector3 bottom, out Vector3 top);

            Assert.That(bottom.x, Is.EqualTo(3.0f), "the column moved off the map");
            Assert.That(bottom.z, Is.EqualTo(-4.0f));
            Assert.That(top.x, Is.EqualTo(3.0f));
            Assert.That(top.z, Is.EqualTo(-4.0f));
            Assert.That(bottom.y, Is.EqualTo(CombatPlane.Floor));
            Assert.That(top.y, Is.EqualTo(CombatPlane.Ceiling));

            Assert.That(
                CombatPlane.Floor,
                Is.GreaterThan(0.0f),
                "a column on the floor would hit the map on the first step of every shot");
            Assert.That(
                CombatPlane.Floor,
                Is.LessThan(1.0f),
                "the jeep is 1.6 m tall and has to be shootable");
            Assert.That(
                CombatPlane.Ceiling,
                Is.GreaterThan(new FlightTuning().CruiseAltitude + 3.0f),
                "the helicopter can climb out of the fight");
        }

        [Test]
        public void TheMapDoesNotCareHowHighUpSomethingIs()
        {
            var ground = new Vector3(0.0f, 0.0f, 0.0f);
            var overhead = new Vector3(3.0f, 20.0f, 4.0f);

            Assert.That(CombatPlane.DistanceOnMap(ground, overhead), Is.EqualTo(5.0f).Within(0.001f));
            Assert.That(CombatPlane.Flatten(overhead).y, Is.EqualTo(0.0f));
        }

        /// <summary>
        /// The column is what decides hits; this is only what keeps the picture honest. A
        /// helicopter ten metres up has to be seen firing at the ground it is hitting.
        /// </summary>
        [Test]
        public void AHighMuzzleAimsDownAndALowOneDoesNot()
        {
            float fromTheAir = CombatPlane.DepressionFrom(11.0f, 26.0f);
            float fromATurret = CombatPlane.DepressionFrom(1.9f, 36.0f);

            Assert.That(fromTheAir, Is.LessThan(-15.0f), "the helicopter is firing level");
            Assert.That(fromATurret, Is.LessThan(0.0f), "the tank is firing level");
            Assert.That(fromATurret, Is.GreaterThan(-3.0f), "the tank is firing at its own tracks");
        }

        [Test]
        public void AMuzzleAlreadyOnThePlaneAimsStraightAhead()
        {
            Assert.That(CombatPlane.DepressionFrom(CombatPlane.ShootingHeight, 30.0f), Is.EqualTo(0.0f));
            Assert.That(CombatPlane.DepressionFrom(0.2f, 30.0f), Is.EqualTo(0.0f));
            Assert.That(CombatPlane.DepressionFrom(11.0f, 0.0f), Is.EqualTo(0.0f));
        }

        [Test]
        public void AnExplosionStartsAndEndsAtNothing()
        {
            Assert.That(Explosion.Scale(0.0f), Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(Explosion.Scale(1.0f), Is.EqualTo(0.0f).Within(0.0001f));
        }

        /// <summary>
        /// It has to swell fast and fade slowly, or it reads as a pulsing ball rather than
        /// as something going off.
        /// </summary>
        [Test]
        public void AnExplosionPeaksEarlyAndSpendsTheRestOfItsLifeFading()
        {
            Assert.That(Explosion.Scale(0.25f), Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(Explosion.Scale(0.1f), Is.GreaterThan(Explosion.Scale(0.9f)));
            Assert.That(Explosion.Scale(0.5f), Is.GreaterThan(Explosion.Scale(0.75f)));
        }

        [Test]
        public void AnExplosionCannotBeAskedForMoreThanItHas()
        {
            Assert.That(Explosion.Scale(-1.0f), Is.EqualTo(0.0f).Within(0.0001f));
            Assert.That(Explosion.Scale(2.0f), Is.EqualTo(0.0f).Within(0.0001f));
        }

        /// <summary>Whether a bunker is built with the markers the real model carries.</summary>
        private const bool Modelled = true;

        /// <summary>
        /// Builds a bunker, with or without the two deploy markers its model exports.
        /// </summary>
        /// <param name="side">Side it belongs to.</param>
        /// <param name="at">Where it stands.</param>
        /// <param name="yaw">Which way it faces.</param>
        /// <param name="modelled">Whether to give it the lift and pad the real model has.</param>
        /// <returns>The bunker.</returns>
        /// <remarks>
        /// The offsets are the ones the Blender asset actually places: the lift platform
        /// 5.2 m out of the door at ankle height, and the helipad up on the roof and off to
        /// one side. They are copied here rather than read from the model so that this test
        /// still means something when the .glb is missing.
        /// </remarks>
        private TeamBunker Create(Team side, Vector3 at, float yaw, bool modelled)
        {
            var host = new GameObject($"Bunker ({side})");
            spawned.Add(host);
            host.transform.SetPositionAndRotation(at, Quaternion.Euler(0.0f, yaw, 0.0f));

            Transform lift = modelled
                ? Marker(host.transform, TeamBunker.LiftNodeName, new Vector3(0.0f, 0.25f, 5.2f))
                : null;
            Transform pad = modelled
                ? Marker(host.transform, TeamBunker.HelipadNodeName, new Vector3(2.2f, 3.9f, -1.8f))
                : null;

            TeamBunker bunker = host.AddComponent<TeamBunker>();
            bunker.Configure(side, lift, pad);
            return bunker;
        }

        private static Transform Marker(Transform parent, string name, Vector3 at)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            host.transform.localPosition = at;
            return host.transform;
        }
    }
}
