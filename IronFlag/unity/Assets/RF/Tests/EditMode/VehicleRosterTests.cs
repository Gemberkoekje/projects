using NUnit.Framework;
using UnityEngine;
using IronFlag.Vehicles;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// Covers the roster as a whole: the four vehicles are balanced against each other, and
    /// the design document's ordering is the thing being protected.
    /// </summary>
    /// <remarks>
    /// A single vehicle's numbers are a tuning decision and are free to move. Their order is
    /// not: "the jeep is the fastest and the ASV is the slowest" is a design pillar, and a
    /// retune that quietly inverts it should fail here rather than in a playtest.
    /// </remarks>
    public sealed class VehicleRosterTests
    {
        [Test]
        public void TheJeepIsTheFastestThingOnTheField()
        {
            float jeep = VehicleTuning.For(VehicleKind.Jeep).MaxSpeed;

            Assert.That(jeep, Is.GreaterThan(VehicleTuning.For(VehicleKind.Tank).MaxSpeed));
            Assert.That(jeep, Is.GreaterThan(VehicleTuning.For(VehicleKind.Asv).MaxSpeed));
            Assert.That(jeep, Is.GreaterThan(VehicleTuning.For(VehicleKind.Helicopter).MaxSpeed));
        }

        [Test]
        public void TheAsvIsTheSlowestAndTheHeaviestOfTheGroundVehicles()
        {
            VehicleTuning asv = VehicleTuning.For(VehicleKind.Asv);
            VehicleTuning tank = VehicleTuning.For(VehicleKind.Tank);

            Assert.That(asv.MaxSpeed, Is.LessThan(tank.MaxSpeed));
            Assert.That(asv.Acceleration, Is.LessThan(tank.Acceleration));
            Assert.That(asv.TurnRate, Is.LessThan(tank.TurnRate));
        }

        [Test]
        public void TheTankOutweighsEverythingElse()
        {
            float tank = VehicleTuning.For(VehicleKind.Tank).Mass;

            Assert.That(tank, Is.GreaterThan(VehicleTuning.For(VehicleKind.Asv).Mass));
            Assert.That(tank, Is.GreaterThan(VehicleTuning.For(VehicleKind.Jeep).Mass));
            Assert.That(tank, Is.GreaterThan(VehicleTuning.For(VehicleKind.Helicopter).Mass));
        }

        [Test]
        public void OnlyTheWheeledVehicleHasToBeRollingToSteer()
        {
            Assert.That(VehicleTuning.For(VehicleKind.Jeep).PivotTurn, Is.False);
            Assert.That(VehicleTuning.For(VehicleKind.Tank).PivotTurn, Is.True);
            Assert.That(VehicleTuning.For(VehicleKind.Asv).PivotTurn, Is.True);
            Assert.That(VehicleTuning.For(VehicleKind.Helicopter).PivotTurn, Is.True);
        }

        [Test]
        public void OnlyTheTurretedVehiclesTraverse()
        {
            Assert.That(VehicleTuning.For(VehicleKind.Tank).TurretTurnRate, Is.GreaterThan(0.0f));
            Assert.That(VehicleTuning.For(VehicleKind.Asv).TurretTurnRate, Is.GreaterThan(0.0f));
            Assert.That(VehicleTuning.For(VehicleKind.Jeep).TurretTurnRate, Is.EqualTo(0.0f));
            Assert.That(VehicleTuning.For(VehicleKind.Helicopter).TurretTurnRate, Is.EqualTo(0.0f));
        }

        [Test]
        public void TheTankTraversesFasterThanTheAsv()
        {
            Assert.That(
                VehicleTuning.For(VehicleKind.Tank).TurretTurnRate,
                Is.GreaterThan(VehicleTuning.For(VehicleKind.Asv).TurretTurnRate));
        }

        [Test]
        public void EveryVehicleBrakesHarderThanItAccelerates()
        {
            foreach (VehicleKind kind in new[]
            {
                VehicleKind.Jeep, VehicleKind.Tank, VehicleKind.Asv, VehicleKind.Helicopter,
            })
            {
                VehicleTuning tuning = VehicleTuning.For(kind);
                Assert.That(tuning.Braking, Is.GreaterThan(tuning.Acceleration), kind.ToString());
            }
        }

        [Test]
        public void EveryVehicleHasWorkableNumbers()
        {
            foreach (VehicleKind kind in new[]
            {
                VehicleKind.Jeep, VehicleKind.Tank, VehicleKind.Asv, VehicleKind.Helicopter,
            })
            {
                VehicleTuning tuning = VehicleTuning.For(kind);

                Assert.That(tuning.MaxSpeed, Is.GreaterThan(0.0f), kind.ToString());
                Assert.That(tuning.ReverseSpeed, Is.GreaterThan(0.0f), kind.ToString());
                Assert.That(tuning.TurnRate, Is.GreaterThan(0.0f), kind.ToString());
                Assert.That(tuning.Mass, Is.GreaterThan(0.0f), kind.ToString());
            }
        }

        [Test]
        public void AnUnassignedVehicleGetsTheHarmlessDefaults()
        {
            VehicleTuning none = VehicleTuning.For(VehicleKind.None);

            Assert.That(none, Is.Not.Null);
            Assert.That(none.MaxSpeed, Is.GreaterThan(0.0f));
            Assert.That(none.TurretTurnRate, Is.EqualTo(0.0f));
        }

        [Test]
        public void TheTurretStowsWhenNobodyIsAiming()
        {
            Assert.That(VehicleTurret.TargetYaw(Vector2.zero, 42.0f), Is.EqualTo(42.0f).Within(0.001f));
        }

        [Test]
        public void AimingEastPointsTheGunEast()
        {
            Assert.That(VehicleTurret.TargetYaw(Vector2.right, 0.0f), Is.EqualTo(90.0f).Within(0.001f));
            Assert.That(VehicleTurret.TargetYaw(Vector2.up, 0.0f), Is.EqualTo(0.0f).Within(0.001f));
        }

        /// <summary>
        /// The design document's armour column, read as an ordering rather than as numbers:
        /// the ASV is the toughest thing on the field and the two fast vehicles are the
        /// thinnest-skinned. Speed is paid for with armour, in that order.
        /// </summary>
        [Test]
        public void ArmorRunsTheOppositeWayToSpeed()
        {
            float jeep = VehicleTuning.For(VehicleKind.Jeep).HitPoints;
            float tank = VehicleTuning.For(VehicleKind.Tank).HitPoints;
            float asv = VehicleTuning.For(VehicleKind.Asv).HitPoints;
            float helicopter = VehicleTuning.For(VehicleKind.Helicopter).HitPoints;

            Assert.That(asv, Is.GreaterThan(tank), "the ASV is meant to be the toughest");
            Assert.That(tank, Is.GreaterThan(jeep));
            Assert.That(jeep, Is.GreaterThan(helicopter), "the helicopter is the most fragile");
        }

        [Test]
        public void EveryVehicleCanBeShotAtAll()
        {
            foreach (VehicleKind kind in new[]
            {
                VehicleKind.Jeep, VehicleKind.Tank, VehicleKind.Asv, VehicleKind.Helicopter,
            })
            {
                Assert.That(VehicleTuning.For(kind).HitPoints, Is.GreaterThan(0.0f), kind.ToString());
            }
        }
    }
}
