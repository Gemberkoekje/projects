using NUnit.Framework;
using UnityEngine;
using IronFlag.Combat;
using IronFlag.Vehicles;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// Covers the arsenal as a whole: four weapons balanced against each other and against
    /// the armour they are shooting at.
    /// </summary>
    /// <remarks>
    /// Written the same way <see cref="VehicleRosterTests"/> is, and for the same reason.
    /// Any single number here is a tuning decision and free to move; what is not free to
    /// move is the shape - the tank outranges everybody, the jeep has to be nearly on top of
    /// what it is shooting, one rocket ends a jeep. Those are design pillars, and a retune
    /// that inverts one should fail here rather than in a playtest.
    /// </remarks>
    public sealed class WeaponRosterTests
    {
        [Test]
        public void EveryVehicleIsArmed()
        {
            foreach (VehicleKind kind in Roster())
            {
                Assert.That(WeaponTuning.For(kind).Exists, Is.True, kind.ToString());
            }
        }

        [Test]
        public void EveryVehicleCarriesItsOwnWeapon()
        {
            Assert.That(WeaponTuning.For(VehicleKind.Jeep).Kind, Is.EqualTo(WeaponKind.Grenade));
            Assert.That(WeaponTuning.For(VehicleKind.Tank).Kind, Is.EqualTo(WeaponKind.Cannon));
            Assert.That(WeaponTuning.For(VehicleKind.Asv).Kind, Is.EqualTo(WeaponKind.Rocket));
            Assert.That(WeaponTuning.For(VehicleKind.Helicopter).Kind, Is.EqualTo(WeaponKind.Chaingun));
        }

        [Test]
        public void AnUnassignedVehicleCarriesNothing()
        {
            Assert.That(WeaponTuning.For(VehicleKind.None).Exists, Is.False);
        }

        [Test]
        public void TheTankOutrangesEverything()
        {
            float tank = WeaponTuning.For(VehicleKind.Tank).Range;

            Assert.That(tank, Is.GreaterThan(WeaponTuning.For(VehicleKind.Asv).Range));
            Assert.That(tank, Is.GreaterThan(WeaponTuning.For(VehicleKind.Helicopter).Range));
            Assert.That(tank, Is.GreaterThan(WeaponTuning.For(VehicleKind.Jeep).Range));
        }

        /// <summary>
        /// The jeep is the only vehicle that can take the flag, so it is not also allowed to
        /// fight at a distance. Shortest reach is not enough - measured against the gun the
        /// roster is anchored on, it has to be a different order of thing.
        /// </summary>
        [Test]
        public void TheJeepHasToDriveAtWhatItWantsToHit()
        {
            float jeep = WeaponTuning.For(VehicleKind.Jeep).Range;

            foreach (VehicleKind kind in new[] { VehicleKind.Tank, VehicleKind.Asv, VehicleKind.Helicopter })
            {
                Assert.That(
                    jeep, Is.LessThan(WeaponTuning.For(kind).Range), $"against the {kind}");
            }

            Assert.That(jeep * 2.0f, Is.LessThan(WeaponTuning.For(VehicleKind.Tank).Range));
        }

        /// <summary>
        /// The camera shows roughly thirty metres ahead of the vehicle it is following, and
        /// a weapon that outranges the screen is one whose hits the player never sees. That
        /// was the second thing wrong with the first version of combat: the tank's
        /// eighty-five metres put most of its shots somewhere nobody was looking.
        /// </summary>
        [Test]
        public void NoWeaponOutrangesWhatThePlayerCanSee()
        {
            const float horizon = 40.0f;

            foreach (VehicleKind kind in Roster())
            {
                Assert.That(WeaponTuning.For(kind).Range, Is.LessThan(horizon), kind.ToString());
            }
        }

        [Test]
        public void TheGrenadeIsTheOnlyRoundThatFalls()
        {
            Assert.That(WeaponTuning.For(VehicleKind.Jeep).Drop, Is.GreaterThan(0.0f));
            Assert.That(WeaponTuning.For(VehicleKind.Tank).Drop, Is.EqualTo(0.0f));
            Assert.That(WeaponTuning.For(VehicleKind.Asv).Drop, Is.EqualTo(0.0f));
            Assert.That(WeaponTuning.For(VehicleKind.Helicopter).Drop, Is.EqualTo(0.0f));
        }

        /// <summary>
        /// The grenade's arc is angled to reach exactly as far as the weapon claims, rather
        /// than being stated next to the claim and left to drift away from it. This is the
        /// test that says so.
        /// </summary>
        [Test]
        public void TheGrenadesArcComesDownExactlyAtItsReach()
        {
            WeaponTuning grenade = WeaponTuning.For(VehicleKind.Jeep);

            Assert.That(
                ProjectileMotion.BallisticRange(
                    grenade.MuzzleSpeed, grenade.ElevationDegrees, grenade.Drop),
                Is.EqualTo(grenade.Range).Within(0.01f));
        }

        /// <summary>
        /// A steeper launch flies higher, and the higher the drawn round arcs the further it
        /// strays from the column that actually decides the hit. The lob has to stay low
        /// enough that the picture and the mechanic still tell the same story.
        /// </summary>
        [Test]
        public void TheGrenadeLobsLowEnoughToLookLikeWhatItHits()
        {
            WeaponTuning grenade = WeaponTuning.For(VehicleKind.Jeep);
            float apex = grenade.Range * Mathf.Tan(grenade.ElevationDegrees * Mathf.Deg2Rad) / 4.0f;

            Assert.That(grenade.ElevationDegrees, Is.GreaterThan(0.0f), "it is not lobbed at all");
            Assert.That(apex, Is.LessThan(1.5f), "the arc climbs out of the fight");
        }

        [Test]
        public void OnlyTheLobbedWeaponIsAngledInTheTable()
        {
            foreach (VehicleKind kind in new[]
            {
                VehicleKind.Tank, VehicleKind.Asv, VehicleKind.Helicopter,
            })
            {
                Assert.That(
                    WeaponTuning.For(kind).ElevationDegrees,
                    Is.EqualTo(0.0f),
                    $"the {kind} is aimed per shot, not from the table");
            }
        }

        [Test]
        public void TheRocketIsTheSlowestRoundAndTheChaingunTheFastest()
        {
            float rocket = WeaponTuning.For(VehicleKind.Asv).MuzzleSpeed;
            float chaingun = WeaponTuning.For(VehicleKind.Helicopter).MuzzleSpeed;

            Assert.That(rocket, Is.LessThan(WeaponTuning.For(VehicleKind.Tank).MuzzleSpeed));
            Assert.That(chaingun, Is.GreaterThan(WeaponTuning.For(VehicleKind.Tank).MuzzleSpeed));
        }

        [Test]
        public void TheChaingunTradesTheLightestRoundForTheHeaviestStreamOfThem()
        {
            WeaponTuning chaingun = WeaponTuning.For(VehicleKind.Helicopter);

            foreach (VehicleKind kind in new[] { VehicleKind.Jeep, VehicleKind.Tank, VehicleKind.Asv })
            {
                WeaponTuning other = WeaponTuning.For(kind);
                Assert.That(chaingun.Damage, Is.LessThan(other.Damage), $"against the {kind}");
                Assert.That(
                    chaingun.DamagePerSecond, Is.GreaterThan(other.DamagePerSecond), $"against the {kind}");
            }
        }

        [Test]
        public void TheBlastRadiusFallsOffWithTheSizeOfTheRound()
        {
            Assert.That(
                WeaponTuning.For(VehicleKind.Asv).SplashRadius,
                Is.GreaterThan(WeaponTuning.For(VehicleKind.Jeep).SplashRadius));
            Assert.That(
                WeaponTuning.For(VehicleKind.Jeep).SplashRadius,
                Is.GreaterThan(WeaponTuning.For(VehicleKind.Tank).SplashRadius));
            Assert.That(WeaponTuning.For(VehicleKind.Helicopter).SplashRadius, Is.EqualTo(0.0f));
        }

        /// <summary>
        /// The design document gives the jeep one to two hits of armour. Against the two
        /// weapons built to punch, it is one.
        /// </summary>
        [Test]
        public void OneRocketEndsAJeepOrAHelicopter()
        {
            Assert.That(RoundsToKill(VehicleKind.Asv, VehicleKind.Jeep), Is.EqualTo(1));
            Assert.That(RoundsToKill(VehicleKind.Asv, VehicleKind.Helicopter), Is.EqualTo(1));
        }

        [Test]
        public void TheTankNeedsTwoShellsForTheThinSkinnedPairAndMoreForTheRest()
        {
            Assert.That(RoundsToKill(VehicleKind.Tank, VehicleKind.Jeep), Is.EqualTo(2));
            Assert.That(RoundsToKill(VehicleKind.Tank, VehicleKind.Helicopter), Is.EqualTo(2));
            Assert.That(RoundsToKill(VehicleKind.Tank, VehicleKind.Tank), Is.GreaterThan(2));
            Assert.That(RoundsToKill(VehicleKind.Tank, VehicleKind.Asv), Is.GreaterThan(3));
        }

        [Test]
        public void NothingEndsTheTankOrTheAsvInOneHit()
        {
            foreach (VehicleKind gun in Roster())
            {
                Assert.That(RoundsToKill(gun, VehicleKind.Tank), Is.GreaterThan(1), $"the {gun}");
                Assert.That(RoundsToKill(gun, VehicleKind.Asv), Is.GreaterThan(1), $"the {gun}");
            }
        }

        /// <summary>
        /// Only the two weapons that cannot see what is underneath them carry a fuze delay:
        /// the helicopter fires from up to thirty metres above the fight, and the rocket has
        /// a blast big enough to be a problem at arm's length. The two guns fired from near
        /// hull height would only be made worse by one.
        /// </summary>
        [Test]
        public void OnlyTheWeaponsThatNeedAFuzeDelayHaveOne()
        {
            Assert.That(
                WeaponTuning.For(VehicleKind.Helicopter).ArmingDistance, Is.GreaterThan(0.0f));
            Assert.That(WeaponTuning.For(VehicleKind.Asv).ArmingDistance, Is.GreaterThan(0.0f));
            Assert.That(WeaponTuning.For(VehicleKind.Tank).ArmingDistance, Is.EqualTo(0.0f));
            Assert.That(WeaponTuning.For(VehicleKind.Jeep).ArmingDistance, Is.EqualTo(0.0f));
        }

        /// <summary>
        /// The helicopter is the only vehicle that fires from above what it is shooting at,
        /// so its fuze has to outlast whatever it is flying over. A building is ten metres
        /// across and the airframe itself is five.
        /// </summary>
        [Test]
        public void TheHelicoptersFuzeOutlastsWhatItIsFlyingOver()
        {
            WeaponTuning chaingun = WeaponTuning.For(VehicleKind.Helicopter);

            Assert.That(chaingun.ArmingDistance, Is.GreaterThan(5.25f), "shorter than the airframe");
            Assert.That(
                chaingun.ArmingDistance,
                Is.GreaterThanOrEqualTo(WeaponTuning.For(VehicleKind.Asv).ArmingDistance),
                "nothing fires from further above the fight than the helicopter");
        }

        /// <summary>
        /// A fuze that armed after the round ran out of reach would be a weapon that cannot
        /// hit anything at all, and nothing about firing it would say so.
        /// </summary>
        [Test]
        public void EveryWeaponHasABandItCanActuallyHitIn()
        {
            foreach (VehicleKind kind in Roster())
            {
                WeaponTuning weapon = WeaponTuning.For(kind);

                Assert.That(weapon.ArmingDistance, Is.LessThan(weapon.Range), kind.ToString());
                Assert.That(
                    weapon.EffectiveRange,
                    Is.GreaterThan(weapon.Range * 0.5f),
                    $"the {kind} spends more than half its reach arming");
            }
        }

        [Test]
        public void EveryWeaponHasWorkableNumbers()
        {
            foreach (VehicleKind kind in Roster())
            {
                WeaponTuning weapon = WeaponTuning.For(kind);

                Assert.That(weapon.Damage, Is.GreaterThan(0.0f), kind.ToString());
                Assert.That(weapon.MuzzleSpeed, Is.GreaterThan(0.0f), kind.ToString());
                Assert.That(weapon.ShotInterval, Is.GreaterThan(0.0f), kind.ToString());
                Assert.That(weapon.Range, Is.GreaterThan(0.0f), kind.ToString());
                Assert.That(weapon.Radius, Is.GreaterThan(0.0f), kind.ToString());
            }
        }

        /// <summary>
        /// Returns how many direct hits from one vehicle's weapon another one survives.
        /// </summary>
        /// <param name="gun">Vehicle doing the shooting.</param>
        /// <param name="target">Vehicle being shot at.</param>
        /// <returns>The number of rounds it takes, counting the one that finishes it.</returns>
        private static int RoundsToKill(VehicleKind gun, VehicleKind target)
            => Mathf.CeilToInt(VehicleTuning.For(target).HitPoints / WeaponTuning.For(gun).Damage);

        private static VehicleKind[] Roster()
            => new[] { VehicleKind.Jeep, VehicleKind.Tank, VehicleKind.Asv, VehicleKind.Helicopter };
    }
}
