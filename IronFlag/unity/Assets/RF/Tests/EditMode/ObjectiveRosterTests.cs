using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Editor.Gameplay;
using IronFlag.Objective;
using IronFlag.Vehicles;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// The flag's rulebook read against the rest of the game: who may carry it, how far you
    /// have to go to learn anything, and how high it flies.
    /// </summary>
    /// <remarks>
    /// The numbers in <see cref="FlagRules"/> are balanced the same way the vehicle, weapon
    /// and structure tables are - by being read against each other rather than tuned one at
    /// a time - so what is checked here is the relationships between them. A pickup radius
    /// larger than the reveal radius, or a mast lower than a hull, would each be a rule
    /// nobody could see was broken by looking at the number alone.
    /// </remarks>
    public sealed class ObjectiveRosterTests
    {
        /// <summary>
        /// The design document's first pillar, and the only rule in the game that says no to
        /// three quarters of the roster.
        /// </summary>
        [Test]
        public void OnlyTheJeepCarriesTheFlag()
        {
            Assert.That(FlagRules.CanCarry(VehicleKind.Jeep), Is.True, "the jeep cannot carry it");

            foreach (VehicleKind kind in new[]
            {
                VehicleKind.Tank, VehicleKind.Asv, VehicleKind.Helicopter, VehicleKind.None,
            })
            {
                Assert.That(FlagRules.CanCarry(kind), Is.False, $"the {kind} can carry the flag");
            }
        }

        /// <summary>
        /// A tower is the toughest thing on the map, because finding a flag is now the
        /// thing you spend ammunition on.
        /// </summary>
        [Test]
        public void ATowerCostsMoreToOpenThanAnythingElseCostsToFlatten()
        {
            float tower = StructureTuning.For(StructureKind.FlagTower).HitPoints;

            foreach (StructureKind kind in StructureTuning.Roster())
            {
                Assert.That(
                    StructureTuning.For(kind).HitPoints,
                    Is.LessThan(tower),
                    $"a {kind} is tougher than the objective");
            }
        }

        /// <summary>
        /// Opening one tower has to be affordable, and opening both has to be a real
        /// decision - so one tank load pays for both and not much else.
        /// </summary>
        /// <remarks>
        /// This is the whole balance of the decoy in two assertions, read off the real
        /// weapon table. Retune the cannon or the tower and this is what says so.
        /// </remarks>
        [Test]
        public void OneTankLoadPaysToOpenBothOfASidesTowers()
        {
            StructureTuning tower = StructureTuning.For(StructureKind.FlagTower);
            WeaponTuning cannon = WeaponTuning.For(VehicleKind.Tank);

            float toOpen = tower.HitPoints * (1.0f - tower.DamagedAt);
            float shells = Mathf.Ceil(toOpen / cannon.Damage);

            Assert.That(
                shells * 2.0f,
                Is.LessThanOrEqualTo(cannon.Rounds),
                "a tank cannot open both towers on one load, so finding a flag needs a rearm");
            Assert.That(
                shells * 2.0f,
                Is.GreaterThan(cannon.Rounds * 0.4f),
                "opening both towers is cheap enough that the decoy costs nothing");
        }

        /// <summary>
        /// The flag rides above the height rounds are resolved at, so shooting at a carrier
        /// is shooting at the jeep rather than at the thing on its roof.
        /// </summary>
        [Test]
        public void TheMastFliesAboveTheCombatPlaneAndBelowItsCeiling()
        {
            Assert.That(
                FlagRules.MastHeight,
                Is.GreaterThan(CombatPlane.ShootingHeight),
                "the flag rides at hull height");
            Assert.That(
                FlagRules.MastHeight,
                Is.LessThan(CombatPlane.Ceiling),
                "the flag rides above everything else in the game");
        }

        /// <summary>
        /// A flag on its tower stands higher than one on a jeep, which is what makes the
        /// tower read as where it belongs.
        /// </summary>
        [Test]
        public void AFlagStandsHigherOnItsTowerThanOnAJeep()
        {
            Assert.That(
                FlagRules.TowerFlagHeight,
                Is.GreaterThan(FlagRules.MastHeight),
                "a flag on a tower is no higher than one being driven around");
        }

        /// <summary>
        /// The flag exists as an asset, and so does every state of the tower it stands on.
        /// </summary>
        [Test]
        public void TheFlagAndEveryStateOfItsTowerAreOnDisk()
        {
            var wanted = new List<string>
            {
                $"{ObjectivePrefabBuilder.ModelFolder}/{ObjectivePrefabBuilder.FlagAsset}.glb",
            };

            foreach (DestructionState state in new[]
            {
                DestructionState.Intact,
                DestructionState.Damaged,
                DestructionState.Destroyed,
            })
            {
                wanted.Add(
                    $"{DestructiblePrefabBuilder.ModelFolder}/"
                    + $"{DestructiblePrefabBuilder.ModelNameFor(StructureKind.FlagTower, state)}.glb");
            }

            foreach (string path in wanted)
            {
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<GameObject>(path),
                    Is.Not.Null,
                    $"{path} is missing; re-run the Blender art pipeline");
            }
        }

        /// <summary>
        /// The flag prefab is one thing that can be picked up and nothing that can be driven
        /// into.
        /// </summary>
        /// <remarks>
        /// The missing collider is the assertion worth having. A flag with one would let a
        /// defender park on top of their own and physically shove a raider off it, which is
        /// a rule nobody wrote and nobody could find.
        /// </remarks>
        [Test]
        public void TheFlagPrefabCarriesAFlagAndNoCollider()
        {
            GameObject prefab = ObjectivePrefabBuilder.LoadFlag();
            Assert.That(prefab, Is.Not.Null, "the flag prefab has not been built");

            Assert.That(
                prefab.GetComponent<Flag>(),
                Is.Not.Null,
                "the flag prefab is not a flag");
            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true).Length,
                Is.Zero,
                "the flag can be bumped into");
            Assert.That(
                prefab.GetComponentsInChildren<Renderer>(true).Length,
                Is.GreaterThan(0),
                "the flag has nothing to look at");
        }

        /// <summary>
        /// The tower prefab is a tower, and it is solid.
        /// </summary>
        [Test]
        public void TheTowerPrefabCarriesATowerAndItsColliders()
        {
            GameObject prefab = ObjectivePrefabBuilder.LoadTower();
            Assert.That(prefab, Is.Not.Null, "the tower prefab has not been built");

            var tower = prefab.GetComponent<FlagTower>();
            Assert.That(tower, Is.Not.Null, "the tower prefab is not a tower");
            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true).Length,
                Is.GreaterThan(0),
                "a vehicle would drive straight through the tower");
        }

        /// <summary>
        /// Shooting the objective is the way to the objective: the tower is a destructible
        /// with three states, like everything else that can be knocked down.
        /// </summary>
        [Test]
        public void TheTowerIsSomethingThatCanBeShotOpen()
        {
            GameObject prefab = ObjectivePrefabBuilder.LoadTower();
            Assert.That(prefab, Is.Not.Null, "the tower prefab has not been built");

            var shell = prefab.GetComponent<Destructible>();
            Assert.That(shell, Is.Not.Null, "the flag tower cannot be shot at");
            Assert.That(
                shell.Kind,
                Is.EqualTo(StructureKind.FlagTower),
                "the tower is configured as something else");

            foreach (DestructionState state in States)
            {
                Assert.That(
                    prefab.transform.Find(Destructible.NodeNameFor(state)),
                    Is.Not.Null,
                    $"the tower has no {state} model, so it cannot show what it is holding");
            }
        }

        /// <summary>
        /// The flag stands on the tower's base, in the same spot, in every state - so
        /// breaking a tower open reveals it rather than moving it.
        /// </summary>
        /// <remarks>
        /// A mount that climbed the tower would mean the flag jumped the moment the first
        /// shell landed, and a flag mounted anywhere but inside would peek over the walls of
        /// a tower that is supposed to be hiding it. Both are art mistakes that no amount of
        /// looking at one state on its own would catch.
        /// </remarks>
        [Test]
        public void TheFlagStandsOnTheSameSpotInsideTheTowerInEveryState()
        {
            GameObject prefab = ObjectivePrefabBuilder.LoadTower();
            Assert.That(prefab, Is.Not.Null, "the tower prefab has not been built");

            Vector3 first = Vector3.positiveInfinity;
            foreach (DestructionState state in States)
            {
                Transform model = prefab.transform.Find(Destructible.NodeNameFor(state));
                Assert.That(model, Is.Not.Null, $"the tower has no {state} model");

                Transform mount = Find(model, FlagTower.MountNodeName);
                Assert.That(
                    mount,
                    Is.Not.Null,
                    $"the {state} tower has no {FlagTower.MountNodeName}; re-run the Blender "
                    + "art pipeline");

                if (float.IsInfinity(first.x))
                {
                    first = mount.position;
                    Assert.That(
                        first.y,
                        Is.GreaterThan(0.0f),
                        "the flag mount is underground");
                    Assert.That(
                        first.y,
                        Is.LessThan(FlagRules.MastHeight * 2.0f),
                        "the flag stands well above the tower's base rather than on it");
                    continue;
                }

                Assert.That(
                    Vector3.Distance(mount.position, first),
                    Is.LessThan(0.01f),
                    $"the {state} tower stands its flag somewhere else, so damaging a tower "
                    + "would make the flag jump");
            }
        }

        /// <summary>
        /// An intact tower encloses its flag rather than merely hiding it: the walls are
        /// wider than the flag is, at every height the flag occupies.
        /// </summary>
        /// <remarks>
        /// Belt and braces on the renderer switch. The flag is turned off inside a sealed
        /// tower, so nothing would show even if it were mounted on the roof - but a flag
        /// that only stays secret because of a boolean is one bug away from giving the decoy
        /// up, and the fix is for it to be physically inside.
        /// </remarks>
        [Test]
        public void AnIntactTowerEnclosesTheFlagItIsHiding()
        {
            GameObject tower = ObjectivePrefabBuilder.LoadTower();
            GameObject flag = ObjectivePrefabBuilder.LoadFlag();
            Assert.That(tower, Is.Not.Null, "the tower prefab has not been built");
            Assert.That(flag, Is.Not.Null, "the flag prefab has not been built");

            Transform model = tower.transform.Find(
                Destructible.NodeNameFor(DestructionState.Intact));
            Transform mount = Find(model, FlagTower.MountNodeName);
            Assert.That(mount, Is.Not.Null, "the intact tower has no flag mount");

            // The real thing, placed the way the game places it - position *and* rotation
            // off the mount - because how far the banner reaches sideways depends entirely
            // on which way it is turned.
            var placed = (GameObject)PrefabUtility.InstantiatePrefab(flag);
            try
            {
                placed.transform.SetPositionAndRotation(mount.position, mount.rotation);
                Bounds standing = Extents(placed.transform);

                Assert.That(
                    standing.max.y,
                    Is.LessThan(Extents(model).max.y),
                    "the flag is taller than the tower it is meant to be sealed inside");

                // The tower tapers, so its narrowest point around the flag is at the top of
                // it. Measured off the mesh rather than assumed, so a reshaped tower is
                // checked rather than trusted.
                float reach = Mathf.Max(
                    Mathf.Abs(standing.min.x), standing.max.x,
                    Mathf.Abs(standing.min.z), standing.max.z);

                Assert.That(
                    reach,
                    Is.LessThan(WidthAt(model, standing.max.y)),
                    "the flag pokes out through the wall of the tower that is hiding it");
            }
            finally
            {
                Object.DestroyImmediate(placed);
            }
        }

        /// <summary>
        /// Measures how wide a model is at one height.
        /// </summary>
        /// <param name="model">Model to measure.</param>
        /// <param name="height">Height to measure at, in metres.</param>
        /// <returns>The furthest any of its geometry reaches from the axis there.</returns>
        /// <remarks>
        /// Cuts the mesh with a horizontal plane rather than looking for vertices near the
        /// height. A tapered tower is four big triangles with nothing in the middle of them,
        /// so sampling vertices at the height a flag stands finds nothing at all and reports
        /// a tower no wider than a pencil.
        /// </remarks>
        private static float WidthAt(Transform model, float height)
        {
            float widest = 0.0f;

            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = filter.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                Vector3[] points = mesh.vertices;
                int[] triangles = mesh.triangles;

                for (int index = 0; index + 2 < triangles.Length; index += 3)
                {
                    Vector3 a = filter.transform.TransformPoint(points[triangles[index]]);
                    Vector3 b = filter.transform.TransformPoint(points[triangles[index + 1]]);
                    Vector3 c = filter.transform.TransformPoint(points[triangles[index + 2]]);

                    widest = Mathf.Max(
                        widest,
                        Crossing(a, b, height),
                        Crossing(b, c, height),
                        Crossing(c, a, height));
                }
            }

            return widest;
        }

        /// <summary>
        /// Returns how far from the axis an edge crosses a horizontal plane.
        /// </summary>
        /// <param name="from">One end of the edge.</param>
        /// <param name="to">The other end.</param>
        /// <param name="height">Height of the plane, in metres.</param>
        /// <returns>The crossing's distance from the axis, or zero when it does not cross.</returns>
        private static float Crossing(Vector3 from, Vector3 to, float height)
        {
            bool spans = (from.y - height) * (to.y - height) <= 0.0f;
            if (!spans || Mathf.Approximately(from.y, to.y))
            {
                return 0.0f;
            }

            Vector3 at = Vector3.Lerp(from, to, (height - from.y) / (to.y - from.y));
            return Mathf.Max(Mathf.Abs(at.x), Mathf.Abs(at.z));
        }

        /// <summary>The three destruction states, in the order a tower goes through them.</summary>
        private static readonly DestructionState[] States =
        {
            DestructionState.Intact,
            DestructionState.Damaged,
            DestructionState.Destroyed,
        };

        /// <summary>
        /// Measures the box every renderer under a root fills, in local space.
        /// </summary>
        /// <param name="root">Object to measure.</param>
        /// <returns>The combined bounds.</returns>
        private static Bounds Extents(Transform root)
        {
            var box = new Bounds(root.position, Vector3.zero);
            foreach (Renderer part in root.GetComponentsInChildren<Renderer>(true))
            {
                box.Encapsulate(part.bounds);
            }

            return box;
        }

        /// <summary>
        /// Finds a named object anywhere under a root.
        /// </summary>
        /// <param name="root">Object to search under.</param>
        /// <param name="name">Name to look for.</param>
        /// <returns>The transform, or <c>null</c>.</returns>
        private static Transform Find(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>
        /// A flag with nobody near it belongs to its side and to nobody else, and a fresh
        /// one is on its tower.
        /// </summary>
        [Test]
        public void AConfiguredFlagStartsOnItsTower()
        {
            var host = new GameObject("Tower");
            var pole = new GameObject("Flag");
            try
            {
                host.transform.position = new Vector3(12.0f, 0.0f, -6.0f);
                FlagTower tower = host.AddComponent<FlagTower>();
                tower.Configure(Team.Green, true);

                Flag flag = pole.AddComponent<Flag>();
                flag.Configure(Team.Green, tower);

                Assert.That(flag.Team, Is.EqualTo(Team.Green));
                Assert.That(flag.State, Is.EqualTo(FlagState.AtTower));
                Assert.That(flag.CarriedBy, Is.EqualTo(Team.None));
                Assert.That(flag.IsOut, Is.False);
                Assert.That(
                    (pole.transform.position - tower.FlagPoint).magnitude,
                    Is.LessThan(0.001f),
                    "the flag is not standing on its tower");
            }
            finally
            {
                Object.DestroyImmediate(pole);
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// An unscouted tower hides what it holds, and that is the whole decoy.
        /// </summary>
        [Test]
        public void AFlagInASealedTowerCannotBeSeen()
        {
            var host = new GameObject("Tower");
            var pole = new GameObject("Flag");
            try
            {
                FlagTower tower = host.AddComponent<FlagTower>();
                tower.Configure(Team.Brown, true);

                Flag flag = pole.AddComponent<Flag>();
                flag.Configure(Team.Brown, tower);

                Assert.That(tower.IsOpen, Is.False, "a fresh tower has already been broken open");
                Assert.That(flag.IsVisible, Is.False, "the flag is on show before anybody shot it");

                Assert.That(tower.Open(), Is.True, "the tower refused to be broken open");
                Assert.That(flag.IsVisible, Is.True, "the flag stayed hidden after being opened");
                Assert.That(tower.Open(), Is.False, "the tower was broken open twice");
            }
            finally
            {
                Object.DestroyImmediate(pole);
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// A match nobody has won is one nothing is waiting on, and a scene with no match at
        /// all has to answer the same way.
        /// </summary>
        [Test]
        public void AMatchStartsWithNobodyHavingWon()
        {
            Assert.That(
                Match.Current == null || !Match.Current.IsOver,
                Is.True,
                "a match somewhere has already been won");

            var host = new GameObject("Match");
            try
            {
                Match match = host.AddComponent<Match>();

                Assert.That(match.IsOver, Is.False);
                Assert.That(match.Winner, Is.EqualTo(Team.None));
                Assert.That(match.Outcome, Is.EqualTo(MatchOutcome.None));
                Assert.That(
                    match.Win(Team.None, Team.Green, MatchOutcome.FlagCaptured),
                    Is.False,
                    "nobody won the match");
                Assert.That(
                    match.Win(Team.Brown, Team.Green, MatchOutcome.None),
                    Is.False,
                    "the match was won by nothing in particular");
                Assert.That(
                    match.Win(Team.Brown, Team.Green, MatchOutcome.FlagCaptured),
                    Is.True,
                    "the match refused a winner");
                Assert.That(match.Winner, Is.EqualTo(Team.Brown));
                Assert.That(match.Beaten, Is.EqualTo(Team.Green));
                Assert.That(match.Outcome, Is.EqualTo(MatchOutcome.FlagCaptured));
                Assert.That(
                    match.Win(Team.Green, Team.Brown, MatchOutcome.OutOfJeeps),
                    Is.False,
                    "the match was won twice");
                Assert.That(match.Winner, Is.EqualTo(Team.Brown), "the first result did not stand");
                Assert.That(
                    match.Outcome,
                    Is.EqualTo(MatchOutcome.FlagCaptured),
                    "the second ending overwrote how the match was actually won");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
