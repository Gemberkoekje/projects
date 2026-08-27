using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Vehicles;

namespace IronFlag.Tests.PlayMode
{
    /// <summary>
    /// The one destructible that shoots back: who it points at, who it does not, and what
    /// happens to it once somebody knocks it down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The arithmetic of the emplacement's gun - reach, damage, rate - is settled in edit
    /// mode against the four the roster carries. What is left for here is the part that only
    /// exists once there are vehicles on the field: an emplacement is a component walking a
    /// roll-call every frame and deciding where to aim, and only a real frame can say
    /// whether it decides right.
    /// </para>
    /// <para>
    /// Built out of cubes rather than the real prefab, like
    /// <see cref="DestructionTests"/> and for the same reason: what is under test is the
    /// targeting, not the art pipeline. That the shipped prefab has a turret node and a
    /// muzzle in the right states is <c>StructureRosterTests</c>' business.
    /// </para>
    /// </remarks>
    public sealed class TurretTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private Projectile round;

        [TearDown]
        public void CleanUp()
        {
            foreach (Projectile stray in Object.FindObjectsByType<Projectile>(FindObjectsInactive.Include))
            {
                if (stray != null)
                {
                    Object.Destroy(stray.gameObject);
                }
            }

            foreach (GameObject item in spawned)
            {
                if (item != null)
                {
                    Object.Destroy(item);
                }
            }

            spawned.Clear();
            round = null;
        }

        /// <summary>
        /// The whole feature in one test: an emplacement with nobody in it puts rounds into
        /// an enemy vehicle that drove into its reach.
        /// </summary>
        [UnityTest]
        public IEnumerator ATurretShootsTheEnemyWithoutAnybodyPullingTheTrigger()
        {
            AutoTurret turret = CreateTurret(Team.Green, Vector3.zero);
            VehicleHealth intruder = CreateVehicle(Team.Brown, new Vector3(0.0f, 0.0f, 12.0f));
            yield return Wait(2.5f);

            Assert.That(
                intruder.HitPoints,
                Is.LessThan(intruder.MaxHitPoints),
                "the turret never fired at a vehicle standing in front of it");
            Assert.That(turret.Target(), Is.Not.Null, "it is not even tracking anything");
        }

        /// <summary>
        /// And it does not shoot its own. Friendly fire is off game-wide, so this is the
        /// same rule the vehicles play by rather than a second one written for turrets - but
        /// an emplacement is the one gun a player cannot simply stop firing, so it is worth
        /// saying out loud.
        /// </summary>
        [UnityTest]
        public IEnumerator ATurretIgnoresItsOwnSide()
        {
            AutoTurret turret = CreateTurret(Team.Green, Vector3.zero);
            VehicleHealth friend = CreateVehicle(Team.Green, new Vector3(0.0f, 0.0f, 12.0f));
            yield return Wait(2.5f);

            Assert.That(turret.Target(), Is.Null, "it is aiming at its own side");
            Assert.That(
                friend.HitPoints,
                Is.EqualTo(friend.MaxHitPoints),
                "the turret shot the team that built it");
        }

        /// <summary>
        /// Reach is a real limit, and it is measured across the map: a vehicle a metre past
        /// the range is as safe as one on the far shore. It is watched while it stands
        /// there - which is the point of the watch - and never fired at.
        /// </summary>
        [UnityTest]
        public IEnumerator NothingOutsideItsReachIsATarget()
        {
            AutoTurret turret = CreateTurret(Team.Green, Vector3.zero);
            float reach = turret.Range;
            VehicleHealth far = CreateVehicle(Team.Brown, new Vector3(0.0f, 0.0f, reach + 4.0f));
            yield return Wait(1.5f);

            Assert.That(turret.Target(), Is.Null, "it counts something out of range as a target");
            Assert.That(turret.Watching(), Is.Not.Null, "it did not even notice it");
            Assert.That(
                far.HitPoints,
                Is.EqualTo(far.MaxHitPoints),
                "the turret hit something it cannot reach");
        }

        /// <summary>
        /// The watch is a limit too, and a wider one. Past it the emplacement does nothing
        /// at all - so a turret is something a player drives around rather than a thing
        /// whose barrel follows them across the whole map.
        /// </summary>
        [UnityTest]
        public IEnumerator NothingOutsideItsWatchIsEvenLookedAt()
        {
            AutoTurret turret = CreateTurret(Team.Green, Vector3.zero);
            CreateVehicle(Team.Brown, new Vector3(-(turret.WatchRange + 4.0f), 0.0f, 0.0f));
            yield return Wait(1.5f);

            Assert.That(turret.Watching(), Is.Null, "it is tracking something across the map");
            Assert.That(
                Mathf.Abs(Mathf.DeltaAngle(turret.AimYawDegrees, 0.0f)),
                Is.LessThan(1.0f),
                "the gun came off its rest for something it could never reach");
        }

        /// <summary>
        /// The whole of the watch in one test: a vehicle that is inside it and outside the
        /// reach is tracked and not shot at, and when it does cross into range the gun is
        /// already pointing at it and fires at once.
        /// </summary>
        /// <remarks>
        /// This is what <see cref="AutoTurret.WatchMargin"/> buys, and it is worth spelling
        /// out as one sequence rather than two tests: the value of the early swing is not
        /// that the barrel moves sooner, it is that there is no swing left to pay for at
        /// the moment the shooting starts.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheGunComesRoundBeforeTheVehicleIsInRangeToBeShot()
        {
            AutoTurret turret = CreateTurret(Team.Green, Vector3.zero);
            float watched = turret.Range + (AutoTurret.WatchMargin * 0.5f);
            VehicleHealth approaching =
                CreateVehicle(Team.Brown, new Vector3(-watched, 0.0f, 0.0f));

            // Comfortably past the ~1.1 s an 80 deg/s traverse needs for a quarter turn.
            yield return Wait(1.5f);

            Assert.That(turret.Watching(), Is.Not.Null, "it never picked the approach up");
            Assert.That(
                turret.Target(), Is.Null, "it can already shoot it, so this proves nothing");
            Assert.That(
                Mathf.Abs(Mathf.DeltaAngle(turret.AimYawDegrees, -90.0f)),
                Is.LessThan(AutoTurret.AimTolerance),
                "the gun waited until the vehicle was in range before it started turning");
            Assert.That(
                approaching.HitPoints,
                Is.EqualTo(approaching.MaxHitPoints),
                "it fired at something out of reach");

            // Straight down the same bearing, so nothing is owed to a second traverse.
            approaching.transform.position = new Vector3(-(turret.Range - 4.0f), 0.0f, 0.0f);
            yield return Wait(1.0f);

            Assert.That(
                approaching.HitPoints,
                Is.LessThan(approaching.MaxHitPoints),
                "the gun was already aimed and still did not fire once the vehicle closed");
        }

        /// <summary>
        /// A helicopter overhead is as much of a target as a tank beside it. The turret
        /// measures across the map, exactly as the round it fires resolves, and the two have
        /// to agree or it would track something it could never hit.
        /// </summary>
        [UnityTest]
        public IEnumerator HeightIsNotCover()
        {
            AutoTurret turret = CreateTurret(Team.Green, Vector3.zero);
            VehicleHealth overhead = CreateVehicle(Team.Brown, new Vector3(0.0f, 10.0f, 8.0f));
            yield return Wait(0.5f);

            Assert.That(turret.Target(), Is.Not.Null, "ten metres up put it out of reach");
        }

        /// <summary>
        /// The gun swings onto the target rather than starting there, which is what makes
        /// driving round an emplacement a real answer to it.
        /// </summary>
        [UnityTest]
        public IEnumerator TheGunTraversesOntoWhatItIsShootingAt()
        {
            AutoTurret turret = CreateTurret(Team.Green, Vector3.zero);
            CreateVehicle(Team.Brown, new Vector3(-10.0f, 0.0f, 0.0f));
            yield return null;

            float first = turret.AimYawDegrees;
            yield return Wait(1.5f);

            Assert.That(
                Mathf.Abs(Mathf.DeltaAngle(turret.AimYawDegrees, -90.0f)),
                Is.LessThan(AutoTurret.AimTolerance),
                "the gun never came round onto the target");
            Assert.That(
                Mathf.Abs(Mathf.DeltaAngle(first, -90.0f)),
                Is.GreaterThan(AutoTurret.AimTolerance),
                "the gun snapped onto the target, so circling it would cost nothing");
        }

        /// <summary>
        /// A traversing gun does not fire. Without this, standing just outside the arc the
        /// barrel has not swept yet would not exist as a safe moment - the turret would be
        /// dangerous from the instant it noticed anything, aimed or not.
        /// </summary>
        [UnityTest]
        public IEnumerator ATurretWithholdsFireWhileTheBarrelIsStillSwinging()
        {
            AutoTurret turret = CreateTurret(Team.Green, Vector3.zero);
            VehicleHealth intruder = CreateVehicle(Team.Brown, new Vector3(-10.0f, 0.0f, 0.0f));
            yield return null;

            Assert.That(turret.Target(), Is.Not.Null, "it never noticed the target at all");
            Assert.That(
                Mathf.Abs(Mathf.DeltaAngle(turret.AimYawDegrees, -90.0f)),
                Is.GreaterThan(AutoTurret.AimTolerance),
                "the gun started already aimed, so this cannot tell traversal from firing");

            // Well short of the ~1.1 s the 80 deg/s traverse needs to close a 90-degree turn.
            yield return Wait(0.2f);

            Assert.That(
                intruder.HitPoints,
                Is.EqualTo(intruder.MaxHitPoints),
                "it fired before the barrel finished swinging onto the target");

            yield return Wait(2.0f);

            Assert.That(
                intruder.HitPoints,
                Is.LessThan(intruder.MaxHitPoints),
                "it never fired once the barrel caught up");
        }

        /// <summary>
        /// Nothing on the field, and the gun goes back to facing the way the emplacement
        /// does. A barrel left pointing wherever the last raider died reads as a turret still
        /// tracking somebody who is not there.
        /// </summary>
        /// <remarks>
        /// It is also what keeps a side's emplacements looking like one defence rather than
        /// several: they are placed on a single heading per side - see
        /// <c>LevelEdits.FacingTheEnemy</c> - and stowing is what puts them back on it once
        /// the raid is over.
        /// </remarks>
        [UnityTest]
        public IEnumerator AnIdleTurretStowsItsGun()
        {
            AutoTurret turret = CreateTurret(Team.Green, Vector3.zero);
            GameObject intruder = CreateVehicle(Team.Brown, new Vector3(-10.0f, 0.0f, 0.0f)).gameObject;
            yield return Wait(1.5f);

            Object.DestroyImmediate(intruder);
            yield return Wait(2.0f);

            Assert.That(
                Mathf.Abs(Mathf.DeltaAngle(turret.AimYawDegrees, 0.0f)),
                Is.LessThan(1.0f),
                "the gun stayed pointing at where the last target was");
        }

        /// <summary>
        /// The answer to a turret is to shoot it. Once it is rubble it stops firing, which
        /// is what makes it a thing a player removes rather than a hazard they route round
        /// for the rest of the match.
        /// </summary>
        [UnityTest]
        public IEnumerator AWreckedTurretStopsFiring()
        {
            AutoTurret turret = CreateTurret(Team.Green, Vector3.zero);
            VehicleHealth intruder = CreateVehicle(Team.Brown, new Vector3(0.0f, 0.0f, 12.0f));
            yield return Wait(1.5f);

            Assert.That(
                intruder.HitPoints,
                Is.LessThan(intruder.MaxHitPoints),
                "the turret was never a threat to begin with");

            var shell = turret.GetComponent<Destructible>();
            shell.TakeDamage(shell.Tuning.HitPoints, Team.Brown);

            // Let whatever was already in the air arrive before the hull is patched up. A
            // round fired a tenth of a second before the turret came down is a fair hit, and
            // counting it against the rubble would make this test fail for the wrong reason.
            yield return Wait(0.5f);
            intruder.Repair();
            yield return Wait(2.0f);

            Assert.That(
                shell.State,
                Is.EqualTo(DestructionState.Destroyed),
                "it did not come down");
            Assert.That(
                intruder.HitPoints,
                Is.EqualTo(intruder.MaxHitPoints),
                "the rubble is still shooting");
        }

        /// <summary>
        /// Its own side cannot knock it down. The same answer that points the gun makes the
        /// emplacement immune to the fire of the team it belongs to - one rule, read twice.
        /// </summary>
        [UnityTest]
        public IEnumerator ATurretCannotBeShotDownByItsOwners()
        {
            AutoTurret turret = CreateTurret(Team.Green, Vector3.zero);
            var shell = turret.GetComponent<Destructible>();
            yield return null;

            Assert.That(
                shell.TakeDamage(shell.Tuning.HitPoints, Team.Green),
                Is.EqualTo(0.0f),
                "a side can demolish its own defences");
            Assert.That(shell.State, Is.EqualTo(DestructionState.Intact));

            Assert.That(
                shell.TakeDamage(shell.Tuning.HitPoints, Team.Brown),
                Is.GreaterThan(0.0f),
                "the enemy cannot touch it either, which makes it permanent");
        }

        /// <summary>
        /// Builds an emplacement out of cubes: a base per destruction state, and a turret
        /// node with a muzzle inside the two states that still have a gun.
        /// </summary>
        /// <param name="side">Side it defends.</param>
        /// <param name="at">Where to put it.</param>
        /// <returns>Its targeting component.</returns>
        /// <remarks>
        /// The destroyed state gets no turret node, exactly as the model does not, so the
        /// rubble is silent for the same reason in the test as in the game.
        /// </remarks>
        private AutoTurret CreateTurret(Team side, Vector3 at)
        {
            var host = new GameObject($"Turret ({side})");
            host.SetActive(false);
            host.transform.position = at;
            spawned.Add(host);

            GameObject intact = CreateState(host, Destructible.IntactNodeName, armed: true);
            GameObject damaged = CreateState(host, Destructible.DamagedNodeName, armed: true);
            GameObject destroyed = CreateState(host, Destructible.DestroyedNodeName, armed: false);

            Destructible shell = host.AddComponent<Destructible>();
            shell.Configure(
                StructureKind.Turret, StructureTuning.For(StructureKind.Turret),
                intact, damaged, destroyed, null);
            shell.SetTeam(side);

            VehicleWeapon gun = host.AddComponent<VehicleWeapon>();
            gun.Configure(null, null, WeaponTuning.Emplacement(), Round(), null);

            AutoTurret turret = host.AddComponent<AutoTurret>();
            turret.Configure(gun, 80.0f);

            host.SetActive(true);
            return turret;
        }

        /// <summary>
        /// Builds one destruction state, with or without a gun in it.
        /// </summary>
        /// <param name="host">The emplacement being assembled.</param>
        /// <param name="name">State node name.</param>
        /// <param name="armed">Whether this state carries a traversing turret.</param>
        /// <returns>The state's child object.</returns>
        private static GameObject CreateState(GameObject host, string name, bool armed)
        {
            var state = new GameObject(name);
            state.transform.SetParent(host.transform, false);

            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Base";
            box.transform.SetParent(state.transform, false);
            box.transform.localScale = new Vector3(2.4f, 1.0f, 2.4f);
            box.transform.localPosition = new Vector3(0.0f, 0.5f, 0.0f);

            if (!armed)
            {
                return state;
            }

            var turret = new GameObject(AutoTurret.TurretNodeName);
            turret.transform.SetParent(state.transform, false);
            turret.transform.localPosition = new Vector3(0.0f, 1.4f, 0.0f);

            var point = new GameObject(AutoTurret.MuzzlePointName);
            point.transform.SetParent(turret.transform, false);
            point.transform.localPosition = new Vector3(0.0f, 0.0f, 1.8f);

            return state;
        }

        /// <summary>
        /// Puts a hull on the field for a turret to find.
        /// </summary>
        /// <param name="side">Side to paint it.</param>
        /// <param name="at">Where to put it.</param>
        /// <returns>Its health, which is what these tests read.</returns>
        /// <remarks>
        /// A real <see cref="VehicleController"/>, because that is what
        /// <see cref="VehicleController.OnTheField"/> holds and the roll-call is how a
        /// turret finds anything at all. Kinematic, so it stands where it is put.
        /// </remarks>
        private VehicleHealth CreateVehicle(Team side, Vector3 at)
        {
            var host = new GameObject($"Vehicle ({side})");
            host.SetActive(false);
            host.transform.position = at;
            spawned.Add(host);

            host.AddComponent<BoxCollider>().size = new Vector3(2.0f, 2.0f, 4.0f);
            host.AddComponent<Rigidbody>().isKinematic = true;
            host.AddComponent<VehicleTeamPaint>().Team = side;

            VehicleTuning tuning = VehicleTuning.For(VehicleKind.Tank);
            host.AddComponent<GroundVehicle>().Configure(VehicleKind.Tank, tuning);

            VehicleHealth hull = host.AddComponent<VehicleHealth>();
            hull.Configure(tuning.HitPoints, null, 3.0f);

            host.SetActive(true);
            return hull;
        }

        private Projectile Round()
        {
            if (round != null)
            {
                return round;
            }

            var host = new GameObject("Round");
            host.SetActive(false);

            var body = new GameObject("Body");
            body.transform.SetParent(host.transform, false);

            round = host.AddComponent<Projectile>();
            round.Configure(body.transform, null, null, null);
            spawned.Add(host);
            return round;
        }

        private static IEnumerator Wait(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
            {
                yield return null;
            }
        }
    }
}
