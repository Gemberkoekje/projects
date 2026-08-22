using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Supply;
using IronFlag.Vehicles;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// The fuel and ammunition columns of the two tuning tables, read against each other and
    /// against the map they have to cross.
    /// </summary>
    /// <remarks>
    /// The same kind of test as the weapon roster: none of these numbers means anything on
    /// its own, and all of them mean something next to their neighbours. A tank of fuel is
    /// only large or small compared to the distance between the two bunkers, and a load of
    /// ammunition is only generous or mean compared to how fast the gun empties it.
    /// </remarks>
    public sealed class SupplyRosterTests
    {
        /// <summary>Metres between the two bunkers in the generated sandbox.</summary>
        private const float MapCrossing = 104.0f;

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
        public void EveryVehicleCarriesFuelAndEveryGunCarriesRounds()
        {
            foreach (VehicleKind kind in Roster())
            {
                VehicleTuning tuning = VehicleTuning.For(kind);
                WeaponTuning weapon = WeaponTuning.For(kind);

                Assert.That(tuning.FuelCapacity, Is.GreaterThan(0.0f), $"{kind} has no tank");
                Assert.That(
                    tuning.IdleFuelDraw,
                    Is.GreaterThan(0.0f),
                    $"{kind} burns nothing standing still, so fuel never depletes with time");
                Assert.That(weapon.Rounds, Is.GreaterThan(0), $"{kind} carries no ammunition");
            }
        }

        /// <summary>
        /// The jeep is the only vehicle that has to make a round trip - it is the only one
        /// that can carry the flag - so running it dry on the way home would be a punishment
        /// for doing its job.
        /// </summary>
        [Test]
        public void EveryVehicleCanCrossTheMapAndComeBackSeveralTimes()
        {
            foreach (VehicleKind kind in Roster())
            {
                VehicleTuning tuning = VehicleTuning.For(kind);

                Assert.That(
                    tuning.FuelRange,
                    Is.GreaterThan(MapCrossing * 4.0f),
                    $"{kind} runs dry before it has been anywhere");
            }
        }

        /// <summary>
        /// The drawback the design document's roster table gives the helicopter, in numbers:
        /// the smallest tank in the game, and an idle draw that is most of the full one,
        /// because holding a hover is nearly as much work as crossing the map.
        /// </summary>
        [Test]
        public void TheHelicopterRunsOutOfFuelFirstAndFastestStandingStill()
        {
            VehicleTuning flyer = VehicleTuning.For(VehicleKind.Helicopter);

            foreach (VehicleKind kind in Roster())
            {
                if (kind == VehicleKind.Helicopter)
                {
                    continue;
                }

                VehicleTuning other = VehicleTuning.For(kind);

                Assert.That(
                    flyer.FuelCapacity,
                    Is.LessThan(other.FuelCapacity),
                    $"the helicopter carries more fuel than the {kind}");
                Assert.That(
                    flyer.IdleEndurance,
                    Is.LessThan(other.IdleEndurance),
                    $"the helicopter can loiter longer than the {kind}");
            }

            Assert.That(
                flyer.IdleEndurance,
                Is.LessThan(150.0f),
                "a helicopter that can hover for minutes has no reason to ever go home");
        }

        /// <summary>
        /// Four guns firing at eight times each other's rate can only be compared on how
        /// long a full load lasts, and that is the figure they are balanced on.
        /// </summary>
        [Test]
        public void EveryGunCarriesAboutTheSameNumberOfSecondsOfFire()
        {
            foreach (VehicleKind kind in Roster())
            {
                WeaponTuning weapon = WeaponTuning.For(kind);

                Assert.That(
                    weapon.SustainedFire,
                    Is.InRange(20.0f, 40.0f),
                    $"the {kind} is on a different economy from the rest of the roster");
            }
        }

        /// <summary>
        /// The helicopter has the highest sustained damage in the game, and M3 left it with
        /// nothing paying for that but its armour. A load that is worth more than everyone
        /// else's would hand it the other half of the argument as well.
        /// </summary>
        [Test]
        public void NoLoadOfAmmunitionIsWorthTwiceAnotherOne()
        {
            float most = 0.0f;
            float least = float.MaxValue;

            foreach (VehicleKind kind in Roster())
            {
                float load = WeaponTuning.For(kind).LoadDamage;
                most = Mathf.Max(most, load);
                least = Mathf.Min(least, load);
            }

            Assert.That(most, Is.LessThan(least * 2.0f), "one gun carries twice the fight of another");
        }

        /// <summary>
        /// A round trip has to be worth making. Coming home has to fill both pools faster
        /// than a depot does, or the bunker is only ever somewhere to die near.
        /// </summary>
        [Test]
        public void SittingOnAPointFillsAPoolInSecondsRatherThanMinutes()
        {
            SupplyPoint bunker = Point(Team.Green, 0.25f, 0.25f, true);
            SupplyPoint depot = Point(Team.None, 0.12f, 0.0f, false);

            Assert.That(bunker.SecondsToRefuel, Is.LessThan(8.0f), "coming home takes too long");
            Assert.That(bunker.SecondsToRearm, Is.LessThan(8.0f));
            Assert.That(
                depot.SecondsToRefuel,
                Is.GreaterThan(bunker.SecondsToRefuel),
                "a field depot is as good as going home");
            Assert.That(
                depot.SecondsToRearm,
                Is.EqualTo(Mathf.Infinity),
                "a fuel depot is handing out ammunition");
        }

        /// <summary>
        /// The draw is a straight line between doing nothing and working flat out, which is
        /// what makes fuel deplete with use and with time at once.
        /// </summary>
        [Test]
        public void FuelIsDrawnBetweenTheIdleRateAndTheFullOne()
        {
            Assert.That(VehicleSupply.DrawFor(0.0f, 0.2f), Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(VehicleSupply.DrawFor(1.0f, 0.2f), Is.EqualTo(1.0f).Within(0.0001f));
            Assert.That(VehicleSupply.DrawFor(0.5f, 0.2f), Is.EqualTo(0.6f).Within(0.0001f));
        }

        /// <summary>
        /// Reversing is work and so is descending, and neither is a way of driving for free.
        /// </summary>
        [Test]
        public void DemandIsTheLargerOfTheThrottleAndTheCollectiveWhicheverWay()
        {
            Assert.That(
                VehicleSupply.DemandFrom(new VehicleInput(new Vector2(0.0f, -1.0f), Vector2.zero, 0.0f)),
                Is.EqualTo(1.0f).Within(0.0001f),
                "reversing is free");
            Assert.That(
                VehicleSupply.DemandFrom(new VehicleInput(Vector2.zero, Vector2.zero, -1.0f)),
                Is.EqualTo(1.0f).Within(0.0001f),
                "descending is free");
            Assert.That(
                VehicleSupply.DemandFrom(new VehicleInput(new Vector2(1.0f, 0.0f), Vector2.zero, 0.0f)),
                Is.EqualTo(0.0f).Within(0.0001f),
                "steering costs fuel, which would tax the tracked vehicles for turning");
        }

        /// <summary>
        /// A stranded pilot keeps the turret and the trigger, which is the whole of the
        /// design document's "it can still fight in place".
        /// </summary>
        [Test]
        public void ADryVehicleKeepsItsGunAndLosesItsEngine()
        {
            var asked = new VehicleInput(new Vector2(1.0f, 1.0f), Vector2.right, 1.0f, true);

            VehicleInput ground = VehicleSupply.Stranded(asked, false);
            Assert.That(ground.Drive, Is.EqualTo(Vector2.zero), "it is still driving");
            Assert.That(ground.Fire, Is.True, "it cannot fight where it stands");
            Assert.That(ground.Aim, Is.EqualTo(Vector2.right), "its turret froze too");
            Assert.That(ground.Lift, Is.EqualTo(0.0f));

            VehicleInput air = VehicleSupply.Stranded(asked, true);
            Assert.That(air.Lift, Is.EqualTo(-1.0f), "a helicopter with no fuel is still hovering");
        }

        private static VehicleKind[] Roster()
            => new[] { VehicleKind.Jeep, VehicleKind.Tank, VehicleKind.Asv, VehicleKind.Helicopter };

        /// <summary>
        /// Builds a supply point, and throws it away with the test.
        /// </summary>
        /// <param name="side">Side it serves.</param>
        /// <param name="fuel">Fraction of a tank per second.</param>
        /// <param name="ammo">Fraction of a load per second.</param>
        /// <param name="aircraft">Whether it takes the helicopter.</param>
        /// <returns>The point.</returns>
        private SupplyPoint Point(Team side, float fuel, float ammo, bool aircraft)
        {
            var host = new GameObject($"Supply ({side})");
            spawned.Add(host);

            SupplyPoint point = host.AddComponent<SupplyPoint>();
            point.Configure(side, 9.0f, fuel, ammo, aircraft);
            return point;
        }
    }
}
