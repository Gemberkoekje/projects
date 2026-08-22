using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Supply;
using IronFlag.Editor.Gameplay;
using IronFlag.Vehicles;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// Checks that the generated vehicle prefabs carry what the movement code expects to
    /// find on them.
    /// </summary>
    /// <remarks>
    /// The prefabs are generated from the models, so these tests are really about the
    /// contract between the two: a renamed wheel or a turret that stopped being its own
    /// object in Blender shows up here rather than as a vehicle that silently stops
    /// animating.
    /// </remarks>
    public sealed class VehiclePrefabTests
    {
        [Test]
        public void EveryVehicleHasAPrefab()
        {
            foreach (VehicleKind kind in VehiclePrefabBuilder.Roster())
            {
                Assert.That(Load(kind), Is.Not.Null, VehiclePrefabBuilder.PrefabPathFor(kind));
            }
        }

        [Test]
        public void EveryPrefabIsRootVisualModel()
        {
            foreach (VehicleKind kind in VehiclePrefabBuilder.Roster())
            {
                GameObject prefab = Load(kind);
                Transform visual = prefab.transform.Find(VehiclePrefabBuilder.VisualNodeName);

                Assert.That(visual, Is.Not.Null, $"{kind} has no visual node");
                Assert.That(
                    visual.Find(VehiclePrefabBuilder.ModelNodeName), Is.Not.Null, $"{kind} has no model");
            }
        }

        [Test]
        public void EveryPrefabDrivesAndCollides()
        {
            foreach (VehicleKind kind in VehiclePrefabBuilder.Roster())
            {
                GameObject prefab = Load(kind);
                var controller = prefab.GetComponent<VehicleController>();
                var body = prefab.GetComponent<Rigidbody>();
                var box = prefab.GetComponent<BoxCollider>();

                Assert.That(controller, Is.Not.Null, $"{kind} has no controller");
                Assert.That(controller.Kind, Is.EqualTo(kind));
                Assert.That(body, Is.Not.Null, $"{kind} has no rigidbody");
                Assert.That(box, Is.Not.Null, $"{kind} has no collider");
                Assert.That(box.size.magnitude, Is.GreaterThan(0.5f), $"{kind} has an empty collider");
            }
        }

        /// <summary>
        /// The prefab carries a stamped copy of the tuning table, so this also fails when the
        /// table was edited and <c>Build Vehicle Prefabs</c> was not run afterwards.
        /// </summary>
        [Test]
        public void EveryPrefabCarriesItsOwnTuning()
        {
            foreach (VehicleKind kind in VehiclePrefabBuilder.Roster())
            {
                VehicleTuning expected = VehicleTuning.For(kind);
                VehicleTuning actual = Load(kind).GetComponent<VehicleController>().Tuning;

                Assert.That(actual.MaxSpeed, Is.EqualTo(expected.MaxSpeed).Within(0.001f), kind.ToString());
                Assert.That(
                    actual.Acceleration, Is.EqualTo(expected.Acceleration).Within(0.001f), kind.ToString());
                Assert.That(actual.Braking, Is.EqualTo(expected.Braking).Within(0.001f), kind.ToString());
                Assert.That(actual.TurnRate, Is.EqualTo(expected.TurnRate).Within(0.001f), kind.ToString());
                Assert.That(actual.PivotTurn, Is.EqualTo(expected.PivotTurn), kind.ToString());
            }
        }

        [Test]
        public void EveryPrefabHasTheRigidbodyItsMovementModelNeeds()
        {
            foreach (VehicleKind kind in VehiclePrefabBuilder.Roster())
            {
                GameObject prefab = Load(kind);
                var body = prefab.GetComponent<Rigidbody>();
                bool flies = kind == VehicleKind.Helicopter;

                Assert.That(body.mass, Is.EqualTo(VehicleTuning.For(kind).Mass).Within(0.001f));
                Assert.That(body.useGravity, Is.EqualTo(!flies), $"{kind} gravity");
                Assert.That(body.interpolation, Is.EqualTo(RigidbodyInterpolation.Interpolate));
                Assert.That(
                    body.constraints.HasFlag(RigidbodyConstraints.FreezeRotationX),
                    Is.True,
                    $"{kind} is free to tip over");
            }
        }

        [Test]
        public void EveryPrefabCanTakeASide()
        {
            foreach (VehicleKind kind in VehiclePrefabBuilder.Roster())
            {
                Assert.That(
                    Load(kind).GetComponent<VehicleTeamPaint>(), Is.Not.Null, $"{kind} has no team paint");
            }
        }

        [Test]
        public void OnlyTheWheeledVehicleHasWheelsToAnimate()
        {
            Assert.That(Load(VehicleKind.Jeep).GetComponent<VehicleWheels>(), Is.Not.Null);
            Assert.That(Load(VehicleKind.Helicopter).GetComponent<VehicleWheels>(), Is.Null);
        }

        [Test]
        public void TheTurretedVehiclesHaveATurretToTraverse()
        {
            Assert.That(Load(VehicleKind.Tank).GetComponent<VehicleTurret>(), Is.Not.Null);
            Assert.That(Load(VehicleKind.Asv).GetComponent<VehicleTurret>(), Is.Not.Null);
            Assert.That(Load(VehicleKind.Jeep).GetComponent<VehicleTurret>(), Is.Null);
        }

        [Test]
        public void TheHelicopterFliesAndSpinsItsRotors()
        {
            GameObject prefab = Load(VehicleKind.Helicopter);

            Assert.That(prefab.GetComponent<Helicopter>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<VehicleRotors>(), Is.Not.Null);
        }

        [Test]
        public void TheHelicopterColliderIsTheFuselageAndNotTheRotorDisc()
        {
            // The rotor spans 4.4m and the fuselage 1.34m. A collider fitted to the disc
            // would stop the aircraft fitting through gaps it plainly clears.
            float width = Load(VehicleKind.Helicopter).GetComponent<BoxCollider>().size.x;

            Assert.That(width, Is.LessThan(2.5f), "the collider caught the rotor");
            Assert.That(width, Is.GreaterThan(0.5f));
        }

        [Test]
        public void ColliderWidthsMatchTheAssetSpec()
        {
            (VehicleKind Kind, float Width)[] expected =
            {
                (VehicleKind.Jeep, 1.80f),
                (VehicleKind.Tank, 3.19f),
                (VehicleKind.Asv, 2.80f),
            };

            foreach ((VehicleKind kind, float width) in expected)
            {
                float actual = Load(kind).GetComponent<BoxCollider>().size.x;
                Assert.That(actual, Is.EqualTo(width).Within(0.2f), kind.ToString());
            }
        }

        [Test]
        public void EveryColliderSitsOnTheGroundItStandsOn()
        {
            foreach (VehicleKind kind in VehiclePrefabBuilder.Roster())
            {
                BoxCollider box = Load(kind).GetComponent<BoxCollider>();
                float bottom = box.center.y - (0.5f * box.size.y);

                Assert.That(bottom, Is.EqualTo(0.0f).Within(0.05f), $"{kind} floats or sinks");
            }
        }

        [Test]
        public void EveryPrefabCanBeShotAtAndComesBack()
        {
            foreach (VehicleKind kind in VehiclePrefabBuilder.Roster())
            {
                GameObject prefab = Load(kind);
                var hull = prefab.GetComponent<VehicleHealth>();

                Assert.That(hull, Is.Not.Null, $"{kind} has no health");
                Assert.That(
                    hull.MaxHitPoints,
                    Is.EqualTo(VehicleTuning.For(kind).HitPoints).Within(0.001f),
                    $"{kind}'s armour does not match the table");
                Assert.That(
                    prefab.GetComponent<VehicleBay>(), Is.Not.Null, $"{kind} has no bay to come back to");
            }
        }

        /// <summary>
        /// Both pools are stamped onto the prefab from the two tables that own them, so this
        /// is the same staleness check the weapon numbers get: a retune that never had
        /// <c>Build Vehicle Prefabs</c> run after it shows up here rather than in a match.
        /// </summary>
        [Test]
        public void EveryPrefabCarriesItsOwnFuelAndAmmunition()
        {
            foreach (VehicleKind kind in VehiclePrefabBuilder.Roster())
            {
                VehicleTuning tuning = VehicleTuning.For(kind);
                var supply = Load(kind).GetComponent<VehicleSupply>();

                Assert.That(supply, Is.Not.Null, $"{kind} carries no fuel");
                Assert.That(
                    supply.FuelCapacity,
                    Is.EqualTo(tuning.FuelCapacity).Within(0.001f),
                    $"{kind} has the wrong sized tank");
                Assert.That(
                    supply.RoundsCarried,
                    Is.EqualTo(WeaponTuning.For(kind).Rounds),
                    $"{kind} has the wrong number of rounds");
                Assert.That(
                    supply.IsAircraft,
                    Is.EqualTo(kind == VehicleKind.Helicopter),
                    $"{kind} disagrees with itself about whether it flies");
            }
        }

        /// <summary>
        /// The prefab carries a stamped copy of the weapon table too, so this also fails
        /// when the table was edited and <c>Build Vehicle Prefabs</c> was not run. Every
        /// number is checked rather than a representative few, because a stale field that
        /// nothing looks at is exactly the one that goes unnoticed - this test was watching
        /// four of the eight when a retune of the blast radii sailed straight past it.
        /// </summary>
        [Test]
        public void EveryPrefabCarriesItsOwnWeapon()
        {
            foreach (VehicleKind kind in VehiclePrefabBuilder.Roster())
            {
                WeaponTuning expected = WeaponTuning.For(kind);
                var mount = Load(kind).GetComponent<VehicleWeapon>();

                Assert.That(mount, Is.Not.Null, $"{kind} has no weapon");
                Assert.That(mount.Tuning.Kind, Is.EqualTo(expected.Kind), kind.ToString());

                foreach ((string Field, float Expected, float Actual) number in new[]
                {
                    ("damage", expected.Damage, mount.Tuning.Damage),
                    ("splash radius", expected.SplashRadius, mount.Tuning.SplashRadius),
                    ("muzzle speed", expected.MuzzleSpeed, mount.Tuning.MuzzleSpeed),
                    ("shot interval", expected.ShotInterval, mount.Tuning.ShotInterval),
                    ("range", expected.Range, mount.Tuning.Range),
                    ("drop", expected.Drop, mount.Tuning.Drop),
                    ("elevation", expected.ElevationDegrees, mount.Tuning.ElevationDegrees),
                    ("radius", expected.Radius, mount.Tuning.Radius),
                    ("arming distance", expected.ArmingDistance, mount.Tuning.ArmingDistance),
                })
                {
                    Assert.That(
                        number.Actual,
                        Is.EqualTo(number.Expected).Within(0.001f),
                        $"{kind}'s {number.Field}");
                }

                Assert.That(mount.IsLoaded, Is.True, $"{kind} has no round bound to fire");
            }
        }

        /// <summary>
        /// Where the rounds come out is measured off the model's own muzzle geometry, so
        /// this is really a test that every vehicle still exports a <c>Muzzle</c> object -
        /// and that it is at the front of the vehicle rather than somewhere behind it.
        /// </summary>
        [Test]
        public void EveryMuzzlePointsForwardsOutOfTheVehicle()
        {
            foreach (VehicleKind kind in VehiclePrefabBuilder.Roster())
            {
                GameObject prefab = Load(kind);
                Transform muzzle = prefab.GetComponent<VehicleWeapon>().Muzzle;
                BoxCollider hull = prefab.GetComponent<BoxCollider>();

                Assert.That(muzzle, Is.Not.Null, $"{kind} has no muzzle point");
                Assert.That(
                    muzzle.name, Is.EqualTo(VehiclePrefabBuilder.MuzzlePointName), kind.ToString());
                Assert.That(
                    muzzle.forward.z, Is.GreaterThan(0.9f), $"{kind}'s gun does not point forwards");
                Assert.That(
                    muzzle.position.z,
                    Is.GreaterThan(hull.center.z - (hull.size.z * 0.5f)),
                    $"{kind} fires out of its own back");
            }
        }

        /// <summary>
        /// The turreted vehicles hang their muzzle off the part that traverses, so aiming
        /// the gun and firing it cannot disagree. The other two hang theirs off the hull,
        /// which is why they have to drive at what they want to hit.
        /// </summary>
        [Test]
        public void OnlyTheTurretedVehiclesCarryTheirGunOnTheTurret()
        {
            Assert.That(MuzzleTraverses(VehicleKind.Tank), Is.True);
            Assert.That(MuzzleTraverses(VehicleKind.Asv), Is.True);
            Assert.That(MuzzleTraverses(VehicleKind.Jeep), Is.False);
            Assert.That(MuzzleTraverses(VehicleKind.Helicopter), Is.False);
        }

        private static bool MuzzleTraverses(VehicleKind kind)
        {
            GameObject prefab = Load(kind);
            var mount = prefab.GetComponent<VehicleTurret>();
            if (mount == null)
            {
                return false;
            }

            return prefab.GetComponent<VehicleWeapon>().Muzzle.IsChildOf(mount.Turret);
        }

        private static GameObject Load(VehicleKind kind)
            => AssetDatabase.LoadAssetAtPath<GameObject>(VehiclePrefabBuilder.PrefabPathFor(kind));
    }
}
