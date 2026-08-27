using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Vehicles;

namespace IronFlag.Tests.PlayMode
{
    /// <summary>
    /// Fires real rounds at real colliders: what the weapon table and the health pool cannot
    /// be asked without a physics scene under them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The arithmetic - who is an enemy, how far a grenade carries, how many shells a jeep
    /// survives - is all settled in edit mode. What is left for here is everything that only
    /// exists once the world does: whether a round crossing two metres per fixed step still
    /// finds a jeep, whether friendly fire really passes through instead of being absorbed,
    /// and whether a wreck really leaves the field. The bunker it goes back to is M4's, and
    /// has its own suite next door.
    /// </para>
    /// <para>
    /// Vehicles are assembled here rather than loaded from the prefabs so that a failure
    /// means the code is wrong, not the asset - but they are assembled at the <em>real</em>
    /// sizes and muzzle heights, because height is precisely what the first version of
    /// combat got wrong. A rig that gives every vehicle the same two-metre box cannot notice
    /// a tank shooting over a jeep, and the first one here did not.
    /// </para>
    /// </remarks>
    public sealed class CombatTests
    {
        private readonly List<GameObject> spawned = new List<GameObject>();
        private Projectile round;

        [TearDown]
        public void CleanUp()
        {
            foreach (Projectile stray in Object.FindObjectsByType<Projectile>())
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
        /// The reported bug, in one test. The tank's barrel is 1.9 m off the ground and a
        /// jeep is 1.6 m tall, so a shell resolved in three dimensions sails over the top of
        /// it - which is what shipped, and which no amount of aiming could fix.
        /// </summary>
        [UnityTest]
        public IEnumerator ATankShellHitsAJeepItIsTallerThan()
        {
            CreateGround();
            VehicleWeapon gun = CreateVehicle(VehicleKind.Tank, Team.Green, Vector3.zero).Gun;
            Vehicle jeep = CreateVehicle(VehicleKind.Jeep, Team.Brown, Ahead(24.0f));
            yield return Settle();

            Assert.That(
                gun.Muzzle.position.y,
                Is.GreaterThan(jeep.Shell.bounds.max.y),
                "the rig is not reproducing the geometry that broke");

            Assert.That(gun.TryFire(), Is.True, "the gun refused to fire");
            yield return Wait(1.0f);

            Assert.That(
                jeep.Hull.HitPoints, Is.LessThan(jeep.Hull.MaxHitPoints), "the shell went over it");
        }

        /// <summary>
        /// The second half of the same bug: the helicopter deploys at ten metres, so every
        /// round it fired passed over everything on the map for ever.
        /// </summary>
        [UnityTest]
        public IEnumerator AHelicopterTenMetresUpHitsWhatIsOnTheGround()
        {
            CreateGround();
            VehicleWeapon gun = CreateHelicopter(Team.Green, new Vector3(0.0f, 10.0f, 0.0f)).Gun;
            VehicleHealth target = CreateVehicle(VehicleKind.Tank, Team.Brown, Ahead(20.0f)).Hull;
            yield return Settle();

            Assert.That(
                gun.Muzzle.position.y, Is.GreaterThan(9.0f), "the helicopter is not up in the air");

            gun.TryFire();
            yield return Wait(1.0f);

            Assert.That(target.HitPoints, Is.LessThan(target.MaxHitPoints), "the burst went over it");
        }

        /// <summary>
        /// The sharp edge of resolving combat as a column: a column has no idea that the
        /// thing at its near end is ten metres <em>below</em> the barrel rather than in
        /// front of it, so the helicopter was detonating its own burst on whatever it
        /// happened to be flying over. The fuze is what fixes it, and both halves matter -
        /// the round has to pass over the near target and still hit the far one.
        /// </summary>
        [UnityTest]
        public IEnumerator AHelicopterCannotShootWhatItIsHoveringOver()
        {
            CreateGround();
            Vehicle heli = CreateHelicopter(Team.Green, new Vector3(0.0f, 10.0f, 0.0f));
            VehicleHealth under = CreateVehicle(VehicleKind.Tank, Team.Brown, Ahead(2.0f)).Hull;
            VehicleHealth ahead = CreateVehicle(VehicleKind.Tank, Team.Brown, Ahead(20.0f)).Hull;
            yield return Settle();

            heli.Gun.TryFire();
            yield return Wait(1.0f);

            Assert.That(
                under.HitPoints,
                Is.EqualTo(under.MaxHitPoints),
                "the burst went off on what it was flying over");
            Assert.That(
                ahead.HitPoints,
                Is.LessThan(ahead.MaxHitPoints),
                "the fuze never armed at all");
        }

        /// <summary>
        /// The same fuze on the heaviest warhead in the game, which is the other place a
        /// blast at arm's length is a shot nobody meant to take.
        /// </summary>
        [UnityTest]
        public IEnumerator ARocketDoesNotGoOffInItsOwnLauncher()
        {
            CreateGround();
            VehicleWeapon gun = CreateVehicle(VehicleKind.Asv, Team.Green, Vector3.zero).Gun;
            VehicleHealth touching = CreateVehicle(VehicleKind.Jeep, Team.Brown, Ahead(4.0f)).Hull;
            yield return Settle();

            gun.TryFire();
            yield return Wait(1.5f);

            Assert.That(
                touching.HitPoints,
                Is.EqualTo(touching.MaxHitPoints),
                "the rocket armed inside the launcher");
        }

        /// <summary>
        /// And the guard on the other side of it: the two guns fired from near hull height
        /// have no fuze delay, so a tank being rammed can still shoot what is touching it.
        /// </summary>
        [UnityTest]
        public IEnumerator ATankCanStillShootWhatIsTouchingIt()
        {
            CreateGround();
            VehicleWeapon gun = CreateVehicle(VehicleKind.Tank, Team.Green, Vector3.zero).Gun;
            VehicleHealth touching = CreateVehicle(VehicleKind.Jeep, Team.Brown, Ahead(6.0f)).Hull;
            yield return Settle();

            gun.TryFire();
            yield return Wait(1.0f);

            Assert.That(
                touching.HitPoints, Is.LessThan(touching.MaxHitPoints), "the tank has a dead zone");
        }

        /// <summary>
        /// And the other way round, which is what makes the helicopter's altitude a position
        /// rather than a hiding place.
        /// </summary>
        [UnityTest]
        public IEnumerator AGroundVehicleCanShootAHelicopterOutOfTheAir()
        {
            CreateGround();
            VehicleWeapon gun = CreateVehicle(VehicleKind.Tank, Team.Green, Vector3.zero).Gun;
            VehicleHealth target = CreateHelicopter(Team.Brown, new Vector3(0.0f, 10.0f, 22.0f)).Hull;
            yield return Settle();

            gun.TryFire();
            yield return Wait(1.0f);

            Assert.That(target.HitPoints, Is.LessThan(target.MaxHitPoints), "the shell passed under it");
        }

        [UnityTest]
        public IEnumerator AShellTakesHitPointsOffAnEnemy()
        {
            CreateGround();
            VehicleWeapon gun = CreateVehicle(VehicleKind.Tank, Team.Green, Vector3.zero).Gun;
            VehicleHealth target = CreateVehicle(VehicleKind.Tank, Team.Brown, Ahead(28.0f)).Hull;
            yield return Settle();

            Assert.That(gun.TryFire(), Is.True, "the gun refused to fire");
            yield return Wait(1.0f);

            Assert.That(
                target.HitPoints,
                Is.EqualTo(target.MaxHitPoints - gun.Tuning.Damage).Within(0.5f));
        }

        [UnityTest]
        public IEnumerator TwoShellsWreckAJeep()
        {
            CreateGround();
            VehicleWeapon gun = CreateVehicle(VehicleKind.Tank, Team.Green, Vector3.zero).Gun;
            VehicleHealth target = CreateVehicle(VehicleKind.Jeep, Team.Brown, Ahead(24.0f)).Hull;
            yield return Settle();

            gun.TryFire();
            yield return Wait(gun.Tuning.ShotInterval + 0.2f);
            Assert.That(target.IsDestroyed, Is.False, "one shell should not have been enough");

            gun.TryFire();
            yield return Wait(1.0f);

            Assert.That(target.IsDestroyed, Is.True, "two shells should have been");
        }

        /// <summary>
        /// A round stops at its reach and not a metre further. With the whole roster now
        /// firing inside what the camera can see, this is what keeps it that way.
        /// </summary>
        [UnityTest]
        public IEnumerator NothingIsHitBeyondTheWeaponsReach()
        {
            CreateGround();
            VehicleWeapon gun = CreateVehicle(VehicleKind.Tank, Team.Green, Vector3.zero).Gun;
            VehicleHealth target = CreateVehicle(
                VehicleKind.Tank, Team.Brown, Ahead(gun.Tuning.Range + 12.0f)).Hull;
            yield return Settle();

            gun.TryFire();
            yield return Wait(1.5f);

            Assert.That(target.HitPoints, Is.EqualTo(target.MaxHitPoints), "the shell outran its reach");
        }

        /// <summary>
        /// Both halves of the no-friendly-fire rule at once: the round does not hurt the
        /// friend, and it is not stopped by it either. A player firing down their own start
        /// line has to be able to hit what is on the other side of it.
        /// </summary>
        [UnityTest]
        public IEnumerator AShotAtAFriendPassesStraightThroughIt()
        {
            CreateGround();
            VehicleWeapon gun = CreateVehicle(VehicleKind.Tank, Team.Green, Vector3.zero).Gun;
            VehicleHealth friend = CreateVehicle(VehicleKind.Tank, Team.Green, Ahead(14.0f)).Hull;
            VehicleHealth enemy = CreateVehicle(VehicleKind.Tank, Team.Brown, Ahead(30.0f)).Hull;
            yield return Settle();

            gun.TryFire();
            yield return Wait(1.0f);

            Assert.That(friend.HitPoints, Is.EqualTo(friend.MaxHitPoints), "it shot its own side");
            Assert.That(enemy.HitPoints, Is.LessThan(enemy.MaxHitPoints), "the friend ate the shell");
        }

        [UnityTest]
        public IEnumerator AGunWillNotFireFasterThanItsInterval()
        {
            CreateGround();
            VehicleWeapon gun = CreateVehicle(VehicleKind.Asv, Team.Green, Vector3.zero).Gun;
            yield return Settle();

            Assert.That(gun.TryFire(), Is.True);
            Assert.That(gun.TryFire(), Is.False, "the launcher reloaded instantly");

            yield return Wait(gun.Tuning.ShotInterval + 0.1f);

            Assert.That(gun.TryFire(), Is.True, "the launcher never reloaded");
        }

        /// <summary>
        /// The chaingun's rounds cross over two metres per fixed step, which is most of the
        /// length of a jeep. A round that moved by teleporting would fly through one.
        /// </summary>
        [UnityTest]
        public IEnumerator TheFastestRoundStillFindsTheSmallestTarget()
        {
            CreateGround();
            VehicleWeapon gun = CreateVehicle(VehicleKind.Helicopter, Team.Green, Vector3.zero).Gun;
            VehicleHealth target = CreateVehicle(VehicleKind.Jeep, Team.Brown, Ahead(20.0f)).Hull;
            yield return Settle();

            gun.TryFire();
            yield return Wait(1.0f);

            Assert.That(target.HitPoints, Is.LessThan(target.MaxHitPoints), "the round tunnelled");
        }

        /// <summary>
        /// The jeep is a brawler: it hits what it is nearly touching and reaches nothing
        /// else. Both halves matter, and the first version shipped with the first one broken
        /// - the grenade arced clean over anything close enough to be worth shooting at.
        /// </summary>
        [UnityTest]
        public IEnumerator TheJeepsGrenadeHitsWhatIsCloseAndNothingFurther()
        {
            CreateGround();
            VehicleWeapon gun = CreateVehicle(VehicleKind.Jeep, Team.Green, Vector3.zero).Gun;
            VehicleHealth near = CreateVehicle(VehicleKind.Jeep, Team.Brown, Ahead(9.0f)).Hull;
            VehicleHealth far = CreateVehicle(VehicleKind.Jeep, Team.Brown, Ahead(30.0f)).Hull;
            yield return Settle();

            gun.TryFire();
            yield return Wait(2.0f);

            Assert.That(
                near.HitPoints, Is.LessThan(near.MaxHitPoints), "it arced over what it was next to");
            Assert.That(
                far.HitPoints, Is.EqualTo(far.MaxHitPoints), "it reached further than it should");
        }

        [UnityTest]
        public IEnumerator AWreckIsTakenOffTheFieldWhileItsPilotIsInTheBunker()
        {
            CreateGround();
            Vehicle jeep = CreateVehicle(VehicleKind.Jeep, Team.Green, new Vector3(0.0f, 0.0f, 20.0f));
            jeep.Bay.Configure(0.5f, 0.0f);
            yield return Settle();

            jeep.Hull.TakeDamage(999.0f, Team.Brown);
            yield return null;

            Assert.That(jeep.Bay.IsRepairing, Is.True, "nobody started the repairs");
            Assert.That(jeep.Skin.enabled, Is.False, "the wreck is still visible");
            Assert.That(jeep.Shell.enabled, Is.False, "the wreck is still in the way");
        }

        private static Vector3 Ahead(float metres) => new Vector3(0.0f, 0.0f, metres);

        /// <summary>
        /// One assembled vehicle and the parts of it these tests reach into.
        /// </summary>
        private readonly struct Vehicle
        {
            public Vehicle(
                VehicleController controller,
                VehicleHealth hull,
                VehicleWeapon gun,
                VehicleBay bay,
                Renderer skin,
                Collider shell)
            {
                Controller = controller;
                Hull = hull;
                Gun = gun;
                Bay = bay;
                Skin = skin;
                Shell = shell;
            }

            public VehicleController Controller { get; }

            public VehicleHealth Hull { get; }

            public VehicleWeapon Gun { get; }

            public VehicleBay Bay { get; }

            public Renderer Skin { get; }

            public Collider Shell { get; }
        }

        /// <summary>
        /// The hull each vehicle is built at, from the asset spec's dimension table.
        /// </summary>
        /// <param name="kind">Vehicle to size.</param>
        /// <returns>Width, height and length in metres.</returns>
        /// <remarks>
        /// Load-bearing. A rig that builds every vehicle the same size cannot see a weapon
        /// shooting over a short one, and combat's first version did exactly that.
        /// </remarks>
        private static Vector3 HullOf(VehicleKind kind)
        {
            switch (kind)
            {
                case VehicleKind.Jeep:
                    return new Vector3(1.8f, 1.6f, 4.0f);
                case VehicleKind.Tank:
                    return new Vector3(3.2f, 2.4f, 5.5f);
                case VehicleKind.Asv:
                    return new Vector3(2.8f, 2.3f, 4.8f);
                case VehicleKind.Helicopter:
                    return new Vector3(1.34f, 2.0f, 5.25f);
                default:
                    return new Vector3(2.0f, 2.0f, 4.0f);
            }
        }

        /// <summary>
        /// The height each vehicle's muzzle sits at, from the generated prefabs.
        /// </summary>
        /// <param name="kind">Vehicle to look up.</param>
        /// <returns>Height above the vehicle's origin, in metres.</returns>
        private static float MuzzleHeightOf(VehicleKind kind)
        {
            switch (kind)
            {
                case VehicleKind.Jeep:
                    return 1.16f;
                case VehicleKind.Tank:
                    return 1.90f;
                case VehicleKind.Asv:
                    return 2.10f;
                case VehicleKind.Helicopter:
                    return 0.58f;
                default:
                    return 1.0f;
            }
        }

        private Vehicle CreateVehicle(VehicleKind kind, Team side, Vector3 at)
            => Assemble<GroundVehicle>(kind, side, at, null);

        /// <summary>
        /// Assembles a helicopter that is actually in the air and stays there.
        /// </summary>
        /// <param name="side">Side to paint it.</param>
        /// <param name="at">Where to put it, altitude included.</param>
        /// <returns>The vehicle and the parts these tests look at.</returns>
        private Vehicle CreateHelicopter(Team side, Vector3 at)
            => Assemble<Helicopter>(
                VehicleKind.Helicopter,
                side,
                at,
                flyer => flyer.ConfigureFlight(new FlightTuning(), null));

        /// <summary>
        /// Assembles a vehicle that can shoot, be shot, and come back: the prefab's combat
        /// half, built in code and at the real size.
        /// </summary>
        /// <typeparam name="T">Movement model to attach.</typeparam>
        /// <param name="kind">Vehicle whose tuning and weapon to use.</param>
        /// <param name="side">Side to paint it.</param>
        /// <param name="at">Where to put it.</param>
        /// <param name="setUp">Extra configuration for the movement model, or null.</param>
        /// <returns>The vehicle and the parts these tests look at.</returns>
        /// <remarks>
        /// Built while inactive, exactly as the driving tests do it, so every component is
        /// configured before any of them wake up - the ordering a real prefab gets for free.
        /// </remarks>
        private Vehicle Assemble<T>(VehicleKind kind, Team side, Vector3 at, System.Action<T> setUp)
            where T : VehicleController
        {
            var host = new GameObject($"{kind} ({side})");
            host.SetActive(false);
            host.transform.position = at;

            Vector3 hull = HullOf(kind);
            var box = host.AddComponent<BoxCollider>();
            box.size = hull;
            box.center = new Vector3(0.0f, hull.y * 0.5f, 0.0f);

            host.AddComponent<Rigidbody>();
            var controller = host.AddComponent<T>();
            controller.Configure(kind, VehicleTuning.For(kind));
            setUp?.Invoke(controller);

            host.AddComponent<VehicleTeamPaint>().Team = side;

            VehicleHealth health = host.AddComponent<VehicleHealth>();
            health.Configure(VehicleTuning.For(kind).HitPoints, null, 3.0f);

            GameObject skin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            skin.name = "Visual";
            Object.DestroyImmediate(skin.GetComponent<Collider>());
            skin.transform.SetParent(host.transform, false);
            skin.transform.localPosition = new Vector3(0.0f, hull.y * 0.5f, 0.0f);
            skin.transform.localScale = hull;

            var muzzle = new GameObject("MuzzlePoint");
            muzzle.transform.SetParent(host.transform, false);
            muzzle.transform.localPosition =
                new Vector3(0.0f, MuzzleHeightOf(kind), (hull.z * 0.5f) + 0.2f);

            VehicleWeapon gun = host.AddComponent<VehicleWeapon>();
            gun.Configure(controller, muzzle.transform, WeaponTuning.For(kind), Round(), null);

            var bay = host.AddComponent<VehicleBay>();

            host.SetActive(true);
            spawned.Add(host);

            return new Vehicle(
                controller, health, gun, bay, skin.GetComponent<Renderer>(), box);
        }

        /// <summary>
        /// Builds the round every gun in these tests fires.
        /// </summary>
        /// <returns>A projectile to be instantiated from.</returns>
        /// <remarks>
        /// Kept switched off, because a live round sitting in the scene with no velocity
        /// would count out its own reach and destroy the template.
        /// </remarks>
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

        private void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Test Ground";
            ground.transform.localScale = new Vector3(400.0f, 1.0f, 400.0f);
            ground.transform.position = new Vector3(0.0f, -0.5f, 0.0f);
            spawned.Add(ground);
        }

        /// <summary>
        /// Lets the physics scene catch up with everything that was just created.
        /// </summary>
        /// <returns>An enumerator to yield on.</returns>
        /// <remarks>
        /// Transforms are not auto-synced in this project, so a collider created this frame
        /// is not somewhere a sweep can find it until the next fixed step has run.
        /// </remarks>
        private static IEnumerator Settle() => Wait(0.2f);

        private static IEnumerator Wait(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
            {
                yield return new WaitForFixedUpdate();
            }
        }
    }
}
