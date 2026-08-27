using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Levels;
using IronFlag.Vehicles;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// Every failure branch <see cref="LevelValidation.Problems"/> can report, checked against
    /// a level built to trip exactly one of them.
    /// </summary>
    /// <remarks>
    /// <see cref="LevelDesignTests"/> only ever runs <see cref="LevelValidation.Problems"/>
    /// against the one shipped map, which is built to pass every rule - so none of the rules
    /// this class exists to enforce were ever actually exercised: a rule whose condition,
    /// message or even presence broke would have kept every existing test green. Each test here
    /// starts from <see cref="PlayableLevel"/>, a small level built to satisfy every rule on
    /// its own, breaks exactly one of them, and checks that the one message it should produce
    /// is the one that shows up.
    /// </remarks>
    public sealed class LevelValidationTests
    {
        /// <summary>
        /// Builds a small level that satisfies every rule in <see cref="LevelValidation"/> on
        /// its own, so each test can break exactly one thing about it.
        /// </summary>
        /// <returns>A fresh, fully playable level.</returns>
        private static LevelDefinition PlayableLevel()
            => new LevelDefinition
            {
                Name = "Test Island",
                Description = "A minimal level built to pass every LevelValidation rule.",
                Bounds = new LevelBounds { HalfExtent = 100.0f, WaterDepth = 0.7f },
                Land = new[]
                {
                    new LevelLand { Name = "Island", MinX = -60, MaxX = 60, MinZ = -60, MaxZ = 60 },
                },
                Bunkers = new[]
                {
                    new LevelBunker
                    {
                        Team = nameof(Team.Green),
                        Position = new Vector3(0.0f, 0.0f, -40.0f),
                    },
                    new LevelBunker
                    {
                        Team = nameof(Team.Brown),
                        Position = new Vector3(0.0f, 0.0f, 40.0f),
                        YawDegrees = 180.0f,
                    },
                },
                Towers = new[]
                {
                    new LevelTower
                    {
                        Team = nameof(Team.Green),
                        HoldsTheFlag = true,
                        Position = new Vector3(-20.0f, 0.0f, -50.0f),
                    },
                    new LevelTower
                    {
                        Team = nameof(Team.Green),
                        HoldsTheFlag = false,
                        Position = new Vector3(20.0f, 0.0f, -50.0f),
                    },
                    new LevelTower
                    {
                        Team = nameof(Team.Brown),
                        HoldsTheFlag = false,
                        Position = new Vector3(-20.0f, 0.0f, 50.0f),
                    },
                    new LevelTower
                    {
                        Team = nameof(Team.Brown),
                        HoldsTheFlag = true,
                        Position = new Vector3(20.0f, 0.0f, 50.0f),
                    },
                },
                Structures = new[]
                {
                    new LevelStructure
                    {
                        Kind = nameof(StructureKind.DepotFuel),
                        Position = new Vector3(10.0f, 0.0f, -45.0f),
                        FuelRate = 0.12f,
                    },
                    new LevelStructure
                    {
                        Kind = nameof(StructureKind.DepotAmmo),
                        Position = new Vector3(10.0f, 0.0f, 45.0f),
                        AmmoRate = 0.12f,
                    },
                },
            };

        /// <summary>
        /// Returns the baseline's structures plus one turret on a given side.
        /// </summary>
        /// <param name="level">The level to extend.</param>
        /// <param name="side">Side to write into the turret's Side field, by name.</param>
        /// <returns>A new array; the level is not modified.</returns>
        /// <remarks>
        /// Placed well inside the island and away from the depots, so the only thing it can
        /// be a problem for is the rule under test.
        /// </remarks>
        private static LevelStructure[] WithTurret(LevelDefinition level, string side)
        {
            var grown = new LevelStructure[level.Structures.Length + 1];
            level.Structures.CopyTo(grown, 0);
            grown[grown.Length - 1] = new LevelStructure
            {
                Kind = nameof(StructureKind.Turret),
                Side = side,
                Position = new Vector3(-20.0f, 0.0f, -20.0f),
            };

            return grown;
        }

        /// <summary>
        /// Guards every other test in this class: if the baseline itself is not clean,
        /// nothing built by breaking one thing about it means anything.
        /// </summary>
        [Test]
        public void TheBaselineLevelHasNoProblems()
        {
            var problems = LevelValidation.Problems(PlayableLevel());
            Assert.That(problems, Is.Empty, string.Join("; ", problems));
        }

        /// <summary>
        /// A turret with nobody to defend is an emplacement that stands there doing nothing,
        /// which on the map looks exactly like one that is working.
        /// </summary>
        [Test]
        public void ATurretOnNoSideIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.Structures = WithTurret(level, nameof(Team.None));

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("on no side"));
        }

        /// <summary>
        /// And the rule runs the other way: only a turret or a door may belong to anybody.
        /// A green building is a level saying something the game has no meaning for - and
        /// worse, one its own side could not shoot down.
        /// </summary>
        [Test]
        public void AnythingElseGivenToASideIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            LevelStructure[] added = WithTurret(level, nameof(Team.Green));
            added[added.Length - 1].Kind = nameof(StructureKind.Tree);
            level.Structures = added;

            Assert.That(
                LevelValidation.Problems(level),
                Has.Some.Contains("only a turret or a door"));
        }

        /// <summary>
        /// A gate is refused on the same two counts as a turret, and by the same rule
        /// rather than by a second one that agrees with it.
        /// </summary>
        /// <remarks>
        /// The door is the second kind to take a side, so this is the test that would have
        /// caught the rule being spelled out again somewhere instead of being asked of
        /// <see cref="StructureTuning.BelongsToASide"/>. A gate on no side opens for
        /// nobody, which on the map is indistinguishable from an enemy's gate.
        /// </remarks>
        [Test]
        public void ADoorOnNoSideIsRejectedAndOneOnASideIsNot()
        {
            LevelDefinition level = PlayableLevel();
            LevelStructure[] added = WithTurret(level, nameof(Team.None));
            added[added.Length - 1].Kind = nameof(StructureKind.Door);
            level.Structures = added;

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("on no side"));

            added[added.Length - 1].Side = nameof(Team.Green);
            var problems = LevelValidation.Problems(level);
            Assert.That(problems, Is.Empty, string.Join("; ", problems));
        }

        /// <summary>
        /// A turret with a side is ordinary scenery as far as every other rule is concerned,
        /// which is what keeps the two checks above from being a trap.
        /// </summary>
        [Test]
        public void ATurretOnASideIsFine()
        {
            LevelDefinition level = PlayableLevel();
            level.Structures = WithTurret(level, nameof(Team.Green));

            var problems = LevelValidation.Problems(level);
            Assert.That(problems, Is.Empty, string.Join("; ", problems));
        }

        [Test]
        public void ANullLevelIsAProblem()
        {
            var problems = LevelValidation.Problems(null);
            Assert.That(problems, Has.Some.Contains("no level"));
        }

        [Test]
        public void AnUnnamedLevelIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.Name = string.Empty;

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("no name"));
        }

        [Test]
        public void ALevelWithNoBoundsIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.Bounds = null;

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("no bounds"));
        }

        [Test]
        public void ALevelWithNoExtentIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.Bounds.HalfExtent = 0.0f;

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("no extent"));
        }

        [Test]
        public void ALevelWithNoWaterDepthIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.Bounds.WaterDepth = 0.0f;

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("level with the land"));
        }

        [Test]
        public void ALevelWithNoLandAtAllIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.Land = Array.Empty<LevelLand>();

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("all sea"));
        }

        [Test]
        public void APieceOfLandWithNoAreaIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.Land = new[] { new LevelLand { Name = "Flat", MinX = 5, MaxX = 5, MinZ = -10, MaxZ = 10 } };

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("has no area"));
        }

        [Test]
        public void LandRunningOffTheMapEdgeIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.Land = new[]
            {
                new LevelLand { Name = "Overboard", MinX = -60, MaxX = 200, MinZ = -60, MaxZ = 60 },
            };

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("off the edge of the world"));
        }

        /// <summary>
        /// A surface nobody recognises still builds - it comes out grass, because a piece of
        /// land has to be made of something - so this is the only place the typo is said out
        /// loud, and it has to quote the word.
        /// </summary>
        [Test]
        public void ALandSurfaceNobodyRecognisesIsNamed()
        {
            LevelDefinition level = PlayableLevel();
            level.Land[0].Surface = "Gravel";

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("Gravel"));
        }

        /// <summary>
        /// A shape nobody recognises still builds - it comes out a rectangle, because a
        /// piece of land has to be cut to something - so this is the only place that typo is
        /// said out loud, and it has to quote the word.
        /// </summary>
        [Test]
        public void ALandShapeNobodyRecognisesIsNamed()
        {
            LevelDefinition level = PlayableLevel();
            level.Land[0].Shape = "Trapezium";

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("Trapezium"));
        }

        /// <summary>
        /// A piece of land painted with one of the two waters is refused, because it is a
        /// lake that does not drown you and a hole in the island that does not look like one.
        /// </summary>
        /// <remarks>
        /// Drowning goes by how low a vehicle is rather than by what it is standing on, so a
        /// rectangle of water at ground level is a stretch of sea somebody drives straight
        /// across - while <see cref="SurfaceField"/>, which does go by the surface, counts it
        /// as not-land and will happily cut a map in two through the middle of it.
        /// </remarks>
        [Test]
        public void LandPaintedWithWaterIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.Land[0].Surface = nameof(SurfaceKind.ShallowWater);

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("which is water"));
        }

        /// <summary>
        /// A rectangle written before surfaces existed is not a mistake, and must not be
        /// reported as one.
        /// </summary>
        [Test]
        public void LandThatNamesNoSurfaceIsAccepted()
        {
            LevelDefinition level = PlayableLevel();
            level.Land[0].Surface = string.Empty;

            Assert.That(LevelValidation.Problems(level), Is.Empty);
        }

        /// <summary>
        /// Taking a bunker off a match does not leave a match with a bunker missing: it
        /// leaves a one-player map, and the level is judged as one from that moment.
        /// </summary>
        /// <remarks>
        /// This test used to assert the opposite - that the second bunker was owed - and the
        /// change is the whole of what one-player mode did to the level format. The fault it
        /// finds now is the one that is genuinely left: green kept the towers it had as a
        /// side in a match, and on a one-player map nothing can ever come for them. What a
        /// map with <em>no</em> bunker at all does is
        /// <see cref="ALevelWithNoBunkerAtAllIsStillRejected"/>.
        /// </remarks>
        [Test]
        public void RemovingABunkerMakesItAOnePlayerMap()
        {
            LevelDefinition level = PlayableLevel();
            level.Bunkers = new[] { level.BunkerFor(Team.Green) };

            Assert.That(level.IsSolo, Is.True);

            var problems = LevelValidation.Problems(level);
            Assert.That(problems, Has.None.Contains("no bunker"));
            Assert.That(problems, Has.Some.Contains("is the only side playing"));
        }

        [Test]
        public void ABunkerInTheSeaIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.BunkerFor(Team.Green).Position = new Vector3(0.0f, 0.0f, -90.0f);

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("not on dry land with room around it"));
        }

        [Test]
        public void ABunkerThatResuppliesNothingIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.BunkerFor(Team.Green).SupplyRadius = 0.0f;

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("resupplies nothing"));
        }

        [Test]
        public void AnExtraBunkerIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            var extra = new List<LevelBunker>(level.Bunkers)
            {
                new LevelBunker { Team = nameof(Team.Green), Position = new Vector3(30.0f, 0.0f, -40.0f) },
            };
            level.Bunkers = extra.ToArray();

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("more than it has sides"));
        }

        [Test]
        public void ASideWithOnlyOneTowerIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            var kept = new List<LevelTower>();
            bool droppedOne = false;
            foreach (LevelTower tower in level.Towers)
            {
                if (!droppedOne && tower.Side == Team.Green && !tower.HoldsTheFlag)
                {
                    droppedOne = true;
                    continue;
                }

                kept.Add(tower);
            }

            level.Towers = kept.ToArray();

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("a decoy needs two"));
        }

        [Test]
        public void ASideWithTwoRealTowersIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            foreach (LevelTower tower in level.Towers)
            {
                if (tower.Side == Team.Green)
                {
                    tower.HoldsTheFlag = true;
                }
            }

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("real towers; it needs exactly one"));
        }

        [Test]
        public void ATowerInTheSeaIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.Towers[0].Position = new Vector3(-20.0f, 0.0f, -90.0f);

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("tower is not on dry land"));
        }

        [Test]
        public void TowersCloseEnoughForOneBlastAreRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.Towers[0].Position = new Vector3(-2.0f, 0.0f, -50.0f);
            level.Towers[1].Position = new Vector3(2.0f, 0.0f, -50.0f);

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("widest blast in the game"));
        }

        [Test]
        public void AnUnknownStructureKindIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.Structures = new[]
            {
                new LevelStructure { Kind = "Warehouse", Position = new Vector3(0.0f, 0.0f, 0.0f) },
            };

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("is not a kind of structure this game has"));
        }

        [Test]
        public void AFlagTowerListedAsSceneryIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            var scenery = new List<LevelStructure>(level.Structures)
            {
                new LevelStructure
                {
                    Kind = nameof(StructureKind.FlagTower),
                    Position = new Vector3(0.0f, 0.0f, 0.0f),
                },
            };
            level.Structures = scenery.ToArray();

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("is listed as scenery"));
        }

        [Test]
        public void ABridgeOnDryLandIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            var withBridge = new List<LevelStructure>(level.Structures)
            {
                new LevelStructure
                {
                    Kind = nameof(StructureKind.Bridge),
                    Position = new Vector3(0.0f, -1.2f, 0.0f),
                },
            };
            level.Structures = withBridge.ToArray();

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("spans dry land"));
        }

        [Test]
        public void ANonBridgeStructureInTheSeaIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            var withTree = new List<LevelStructure>(level.Structures)
            {
                new LevelStructure
                {
                    Kind = nameof(StructureKind.Tree),
                    Position = new Vector3(0.0f, 0.0f, 90.0f),
                },
            };
            level.Structures = withTree.ToArray();

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("stands in the sea"));
        }

        [Test]
        public void AMapWithNoFuelDepotIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.Structures = new[] { level.Structures[1] };

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("nowhere on the map to refuel"));
        }

        [Test]
        public void AMapWithNoAmmoDepotIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.Structures = new[] { level.Structures[0] };

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("nowhere on the map to rearm"));
        }

        /// <summary>
        /// A level that gives nobody a jeep is unwinnable from its first frame, and there is
        /// nothing on the map to look at that would say so.
        /// </summary>
        [Test]
        public void ALevelWithNoJeepsIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.Reserve.Set(VehicleKind.Jeep, 0);

            Assert.That(
                LevelValidation.Problems(level),
                Has.Some.Contains("no jeeps"));
        }

        /// <summary>
        /// The rest of the roster may be empty. A map that gives a side jeeps and nothing
        /// else is a design, not a mistake - it is still winnable, which is the only thing
        /// this rule is about.
        /// </summary>
        [Test]
        public void ALevelWithNothingButJeepsIsAccepted()
        {
            LevelDefinition level = PlayableLevel();
            level.Reserve.Set(VehicleKind.Tank, 0);
            level.Reserve.Set(VehicleKind.Asv, 0);
            level.Reserve.Set(VehicleKind.Helicopter, 0);

            var problems = LevelValidation.Problems(level);
            Assert.That(problems, Is.Empty, string.Join("; ", problems));
        }

        /// <summary>
        /// A count below zero can only arrive by hand, through the file - the editor and
        /// <see cref="LevelReserve.Set"/> both clamp - and it has to be named rather than
        /// quietly read as none.
        /// </summary>
        [Test]
        public void ANegativeStockOfVehiclesIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.Reserve.Tanks = -2;

            Assert.That(
                LevelValidation.Problems(level),
                Has.Some.Contains("not a number of vehicles"));
        }

        /// <summary>
        /// A map written before the reserve existed is not a broken map.
        /// </summary>
        [Test]
        public void ALevelWithNoReserveBlockAtAllIsAccepted()
        {
            LevelDefinition level = PlayableLevel();
            level.Reserve = null;

            var problems = LevelValidation.Problems(level);
            Assert.That(problems, Is.Empty, string.Join("; ", problems));
        }

        /// <summary>
        /// Turns the baseline into a one-player map: green keeps its bunker and loses its
        /// towers, brown keeps its towers and loses its bunker.
        /// </summary>
        /// <returns>A fresh, fully playable one-player level.</returns>
        /// <remarks>
        /// Built by subtraction from the map a match is played on, because that is honestly
        /// what the mode is - the same objective with a human missing from one side - and a
        /// separate baseline would be a second opinion about what a level is.
        /// </remarks>
        private static LevelDefinition SoloLevel()
        {
            LevelDefinition level = PlayableLevel();

            level.Bunkers = new[] { level.BunkerFor(Team.Green) };

            var enemy = new List<LevelTower>(level.TowersFor(Team.Brown));
            level.Towers = enemy.ToArray();

            return level;
        }

        /// <summary>
        /// Guards every solo test below, the same way the baseline test guards the rest.
        /// </summary>
        [Test]
        public void AOnePlayerLevelHasNoProblems()
        {
            LevelDefinition level = SoloLevel();

            Assert.That(level.IsSolo, Is.True);

            var problems = LevelValidation.Problems(level);
            Assert.That(problems, Is.Empty, string.Join("; ", problems));
        }

        /// <summary>
        /// The missing bunker is the mode, not a fault - which is the whole difference
        /// between this and what validation said before one-player maps existed.
        /// </summary>
        [Test]
        public void AOnePlayerLevelIsNotAskedForASecondBunker()
        {
            Assert.That(LevelValidation.Problems(SoloLevel()), Has.None.Contains("has no bunker"));
        }

        /// <summary>
        /// A map with no bunker at all is still broken, and is reported as the match it was
        /// most likely on its way to being rather than being waved through as solo.
        /// </summary>
        [Test]
        public void ALevelWithNoBunkerAtAllIsStillRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.Bunkers = Array.Empty<LevelBunker>();

            Assert.That(level.IsSolo, Is.False);
            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("has no bunker"));
        }

        /// <summary>
        /// Two bunkers on the same side still counts as one played side to
        /// <see cref="LevelDefinition.IsSolo"/>, so it is caught on its own rather than
        /// folded into the missing-bunker check above - otherwise a map like this would be
        /// waved through as a clean one-player map while quietly orphaning its second
        /// bunker at build time.
        /// </summary>
        [Test]
        public void TwoBunkersOnOneSideAreRejected()
        {
            LevelDefinition level = PlayableLevel();
            LevelBunker green = level.BunkerFor(Team.Green);
            level.Bunkers = new[]
            {
                green,
                new LevelBunker { Team = nameof(Team.Green), Position = new Vector3(-30.0f, 0.0f, -40.0f) },
            };

            Assert.That(level.IsSolo, Is.True, "two bunkers on one side still reads as one played side");
            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("only one is allowed"));
        }

        /// <summary>
        /// A flag on the solo player's own side is an objective with no opponent: nothing on
        /// the map can ever come for it.
        /// </summary>
        [Test]
        public void TowersOnTheSoloPlayersOwnSideAreRejected()
        {
            LevelDefinition level = SoloLevel();
            var towers = new List<LevelTower>(level.Towers)
            {
                new LevelTower
                {
                    Team = nameof(Team.Green),
                    HoldsTheFlag = true,
                    Position = new Vector3(-20.0f, 0.0f, -50.0f),
                },
            };
            level.Towers = towers.ToArray();

            Assert.That(
                LevelValidation.Problems(level),
                Has.Some.Contains("is the only side playing"));
        }

        /// <summary>
        /// The enemy still owes a decoy. One tower is a flag whose position is known before
        /// the first shot, which is the mode with its own mechanic taken out.
        /// </summary>
        [Test]
        public void AOnePlayerLevelWithOneEnemyTowerIsRejected()
        {
            LevelDefinition level = SoloLevel();
            level.Towers = new[] { level.TowersFor(Team.Brown)[1] };

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("a decoy needs two"));
        }

        /// <summary>
        /// An enemy tower across the water is a tower that might be holding the flag and
        /// cannot be driven to - the one-player shape of the crossing rule.
        /// </summary>
        [Test]
        public void AnUnreachableEnemyTowerIsRejected()
        {
            LevelDefinition level = SoloLevel();
            level.Land = new[]
            {
                new LevelLand { Name = "South", MinX = -60, MaxX = 60, MinZ = -60, MaxZ = -10 },
                new LevelLand { Name = "North", MinX = -60, MaxX = 60, MinZ = 10, MaxZ = 60 },
            };

            Assert.That(
                LevelValidation.Problems(level),
                Has.Some.Contains("cannot be reached from the Green bunker by land"));
        }

        [Test]
        public void DisconnectedBunkersAreRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.Land = new[]
            {
                new LevelLand { Name = "South", MinX = -60, MaxX = 60, MinZ = -60, MaxZ = -10 },
                new LevelLand { Name = "North", MinX = -60, MaxX = 60, MinZ = 10, MaxZ = 60 },
            };

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("not joined by dry land"));
        }
    }
}
