using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Levels;

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
        /// Guards every other test in this class: if the baseline itself is not clean,
        /// nothing built by breaking one thing about it means anything.
        /// </summary>
        [Test]
        public void TheBaselineLevelHasNoProblems()
        {
            var problems = LevelValidation.Problems(PlayableLevel());
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

        [Test]
        public void AMissingBunkerIsRejected()
        {
            LevelDefinition level = PlayableLevel();
            level.Bunkers = new[] { level.BunkerFor(Team.Green) };

            Assert.That(LevelValidation.Problems(level), Has.Some.Contains("no bunker"));
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
