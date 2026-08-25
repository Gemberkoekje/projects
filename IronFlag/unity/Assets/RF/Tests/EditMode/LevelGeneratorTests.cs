using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Editing;
using IronFlag.Levels;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// The map generator, checked without an editor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="LevelGenerator"/> is a static function that turns a seed into a
    /// <see cref="LevelDefinition"/> - no scene, no canvas, no camera - so this is where the
    /// feature is actually tested rather than in whatever presses the button.
    /// </para>
    /// <para>
    /// The rule these are really about is that a generated map is <em>playable by
    /// construction</em>. It is easy to write a generator that produces something plausible
    /// and occasionally produces an island cut in two, and the failure is invisible in a level
    /// file: it is a flood fill that stops. So the sweep below draws every combination of
    /// layout, size and symmetry across a run of seeds and asks
    /// <see cref="LevelValidation"/> - the same rules the editor's own Problems panel shows -
    /// whether each one is a map.
    /// </para>
    /// </remarks>
    public sealed class LevelGeneratorTests
    {
        /// <summary>How many seeds each sweep draws.</summary>
        /// <remarks>
        /// Enough that a one-in-twenty layout bug shows up rather than hiding behind a lucky
        /// seed, and few enough that the whole file is a few seconds. Each map costs one
        /// rasterisation of itself, which is what makes this the expensive suite in the
        /// project rather than a free one.
        /// </remarks>
        private const int Seeds = 12;

        /// <summary>
        /// The whole point of the feature: what comes out is a map, on every layout, at every
        /// size, either symmetry, over a run of seeds.
        /// </summary>
        [Test]
        public void EveryGeneratedMapIsPlayable()
        {
            var broken = new List<string>();

            foreach (MapLayout layout in Layouts())
            {
                foreach (MapDifficulty difficulty in Difficulties())
                {
                    foreach (MapSymmetry symmetry in Symmetries())
                    {
                        for (int seed = 1; seed <= Seeds; seed++)
                        {
                            LevelDefinition map = LevelGenerator.Generate(new MapOptions
                            {
                                Seed = seed * 7919,
                                Layout = layout,
                                Difficulty = difficulty,
                                Symmetry = symmetry,
                            });

                            List<string> problems = LevelValidation.Problems(map);
                            if (problems.Count > 0)
                            {
                                broken.Add(
                                    $"{layout}/{difficulty}/{symmetry} seed {seed * 7919}: "
                                    + string.Join(" ", problems));
                            }
                        }
                    }
                }
            }

            Assert.That(broken, Is.Empty, string.Join("\n", broken));
        }

        /// <summary>
        /// The rule that is hardest to keep and easiest to lose: the two bunkers are joined by
        /// dry land with no bridge in it, so dropping every crossing cannot make the flag
        /// uncapturable.
        /// </summary>
        /// <remarks>
        /// Checked separately from the sweep above even though validation already covers it,
        /// because this is the one that a change to the layouts would break silently: a map
        /// whose halves stopped overlapping is still a perfectly good-looking level file.
        /// </remarks>
        [Test]
        public void TheTwoBunkersAreAlwaysJoinedByLand()
        {
            foreach (MapLayout layout in Layouts())
            {
                for (int seed = 1; seed <= Seeds; seed++)
                {
                    LevelDefinition map = LevelGenerator.Generate(new MapOptions
                    {
                        Seed = seed * 104729,
                        Layout = layout,
                        Symmetry = MapSymmetry.Asymmetrical,
                    });

                    Vector3 green = map.BunkerPosition(Team.Green);
                    Vector3 brown = map.BunkerPosition(Team.Brown);

                    Assert.That(
                        LevelValidation.IsConnected(map, green, brown),
                        Is.True,
                        $"{layout} seed {seed * 104729} has no land route between its bunkers");
                }
            }
        }

        /// <summary>
        /// One seed is one map, forever. Everything the generator draws is a function of the
        /// seed and nothing else, which is what makes a seed worth writing down.
        /// </summary>
        [Test]
        public void TheSameSeedDrawsTheSameMap()
        {
            var options = new MapOptions
            {
                Seed = 1995,
                Difficulty = MapDifficulty.Hard,
                Layout = MapLayout.Channel,
            };

            string first = LevelFile.ToJson(LevelGenerator.Generate(options));
            string again = LevelFile.ToJson(LevelGenerator.Generate(options));

            Assert.That(again, Is.EqualTo(first));
        }

        /// <summary>
        /// And two seeds are two maps, so the button is worth pressing twice.
        /// </summary>
        [Test]
        public void ADifferentSeedDrawsADifferentMap()
        {
            string one = LevelFile.ToJson(LevelGenerator.Generate(new MapOptions { Seed = 1 }));
            string two = LevelFile.ToJson(LevelGenerator.Generate(new MapOptions { Seed = 2 }));

            Assert.That(two, Is.Not.EqualTo(one));
        }

        /// <summary>
        /// Naming a map does not redraw it. The name is rolled whether or not it is wanted, so
        /// that supplying one cannot shift every draw after it.
        /// </summary>
        [Test]
        public void NamingAMapDoesNotChangeIt()
        {
            LevelDefinition rolled = LevelGenerator.Generate(new MapOptions { Seed = 77 });
            LevelDefinition called = LevelGenerator.Generate(
                new MapOptions { Seed = 77, Name = "Somewhere Else" });

            Assert.That(called.Name, Is.EqualTo("Somewhere Else"));
            Assert.That(called.Land.Length, Is.EqualTo(rolled.Land.Length));
            Assert.That(called.Structures.Length, Is.EqualTo(rolled.Structures.Length));
            Assert.That(
                called.BunkerPosition(Team.Green),
                Is.EqualTo(rolled.BunkerPosition(Team.Green)));
        }

        /// <summary>
        /// A mirrored map is <em>rotated</em> half a turn about the origin rather than
        /// reflected across a line. A reflection would give one side a left-handed base and
        /// the other a right-handed one, and the two runs to the enemy flag would not be the
        /// same shape - which is the one thing this setting promises.
        /// </summary>
        [Test]
        public void AMirroredMapIsRotatedRatherThanReflected()
        {
            LevelDefinition map = LevelGenerator.Generate(new MapOptions
            {
                Seed = 4242,
                Symmetry = MapSymmetry.Mirrored,
                Layout = MapLayout.Island,
            });

            LevelBunker green = map.BunkerFor(Team.Green);
            LevelBunker brown = map.BunkerFor(Team.Brown);

            Assert.That(brown.Position.x, Is.EqualTo(-green.Position.x).Within(0.001f));
            Assert.That(brown.Position.z, Is.EqualTo(-green.Position.z).Within(0.001f));

            List<LevelTower> greens = map.TowersFor(Team.Green);
            List<LevelTower> browns = map.TowersFor(Team.Brown);
            Assert.That(browns.Count, Is.EqualTo(greens.Count));

            for (int tower = 0; tower < greens.Count; tower++)
            {
                Assert.That(browns[tower].Position.x, Is.EqualTo(-greens[tower].Position.x).Within(0.001f));
                Assert.That(browns[tower].Position.z, Is.EqualTo(-greens[tower].Position.z).Within(0.001f));
                Assert.That(
                    browns[tower].HoldsTheFlag,
                    Is.EqualTo(greens[tower].HoldsTheFlag),
                    "the mirrored flag is on the tower opposite the real one");
            }
        }

        /// <summary>
        /// A mirrored turret belongs to the other side. Copying the side across instead would
        /// hand one player both sets of emplacements on a map that still looked symmetrical -
        /// the worst kind of asymmetry, because it is invisible.
        /// </summary>
        [Test]
        public void AMirroredMapGivesEachSideItsOwnEmplacements()
        {
            LevelDefinition map = LevelGenerator.Generate(new MapOptions
            {
                Seed = 31337,
                Symmetry = MapSymmetry.Mirrored,
                Difficulty = MapDifficulty.Hard,
            });

            int green = 0;
            int brown = 0;
            foreach (LevelStructure structure in map.Structures)
            {
                if (structure.Structure != StructureKind.Turret)
                {
                    continue;
                }

                if (structure.Team == Team.Green)
                {
                    green++;
                }
                else if (structure.Team == Team.Brown)
                {
                    brown++;
                }
            }

            Assert.That(green, Is.GreaterThan(0));
            Assert.That(brown, Is.EqualTo(green));
        }

        /// <summary>
        /// An asymmetrical map really is asymmetrical: the two halves are drawn from separate
        /// streams and do not come out as each other's opposite numbers.
        /// </summary>
        [Test]
        public void AnAsymmetricalMapHasTwoDifferentHalves()
        {
            int matched = 0;

            for (int seed = 1; seed <= Seeds; seed++)
            {
                LevelDefinition map = LevelGenerator.Generate(new MapOptions
                {
                    Seed = seed * 65537,
                    Symmetry = MapSymmetry.Asymmetrical,
                });

                LevelBunker green = map.BunkerFor(Team.Green);
                LevelBunker brown = map.BunkerFor(Team.Brown);

                if (Vector3.Distance(brown.Position, -green.Position) < 0.5f)
                {
                    matched++;
                }
            }

            Assert.That(
                matched, Is.LessThan(Seeds),
                "every asymmetrical map came out as a mirrored one");
        }

        /// <summary>
        /// Difficulty is size and emplacements, and it is nothing else - a turret's health and
        /// reach are global constants rather than fields of a level file, so a harder map is
        /// more ground and more guns rather than tougher guns.
        /// </summary>
        [Test]
        public void HarderMapsAreBiggerAndBetterDefended()
        {
            LevelDefinition easy = LevelGenerator.Generate(
                new MapOptions { Seed = 11, Difficulty = MapDifficulty.Easy });
            LevelDefinition hard = LevelGenerator.Generate(
                new MapOptions { Seed = 11, Difficulty = MapDifficulty.Hard });

            Assert.That(hard.Bounds.HalfExtent, Is.GreaterThan(easy.Bounds.HalfExtent));
            Assert.That(
                hard.CountOf(StructureKind.Turret),
                Is.GreaterThan(easy.CountOf(StructureKind.Turret)));
        }

        /// <summary>
        /// Every generated map has somewhere to refuel and somewhere to rearm away from home,
        /// which is a rule rather than a courtesy: without both, a map is one long drive with
        /// no way back.
        /// </summary>
        [Test]
        public void EveryGeneratedMapCanBeResupplied()
        {
            foreach (MapLayout layout in Layouts())
            {
                LevelDefinition map = LevelGenerator.Generate(
                    new MapOptions { Seed = 909, Layout = layout });

                Assert.That(map.CountOf(StructureKind.DepotFuel), Is.GreaterThan(0), $"{layout}");
                Assert.That(map.CountOf(StructureKind.DepotAmmo), Is.GreaterThan(0), $"{layout}");
            }
        }

        /// <summary>
        /// A channel map's bridges stand over water. A bridge on dry land is a ramp to nowhere,
        /// and it is the one prop on the map that is meant to be off it.
        /// </summary>
        [Test]
        public void ChannelBridgesStandOverWater()
        {
            for (int seed = 1; seed <= Seeds; seed++)
            {
                LevelDefinition map = LevelGenerator.Generate(new MapOptions
                {
                    Seed = seed * 2003,
                    Layout = MapLayout.Channel,
                });

                int bridges = 0;
                foreach (LevelStructure structure in map.Structures)
                {
                    if (structure.Structure != StructureKind.Bridge)
                    {
                        continue;
                    }

                    bridges++;
                    Assert.That(
                        map.IsOnLand(structure.Position, LevelValidation.ShoreMargin),
                        Is.False,
                        $"seed {seed * 2003} has a bridge over dry land");
                }

                Assert.That(bridges, Is.GreaterThan(0), "a channel map has crossings");
            }
        }

        /// <summary>
        /// A solo map is the shape the 1-player item asks for: one bunker, no green towers, and
        /// an enemy that is several flag towers with exactly one flag between them.
        /// </summary>
        [Test]
        public void ASoloMapHasOneBunkerAndAFieldOfEnemyTowers()
        {
            LevelDefinition map = LevelGenerator.Generate(new MapOptions
            {
                Seed = 5150,
                Players = 1,
                Difficulty = MapDifficulty.Hard,
            });

            Assert.That(map.BunkerFor(Team.Green), Is.Not.Null);
            Assert.That(map.BunkerFor(Team.Brown), Is.Null, "a solo map has one bunker");
            Assert.That(map.TowersFor(Team.Green), Is.Empty, "there is nobody to take green's flag");

            List<LevelTower> enemy = map.TowersFor(Team.Brown);
            Assert.That(enemy.Count, Is.GreaterThan(2), "more decoys than a match has");

            int real = 0;
            foreach (LevelTower tower in enemy)
            {
                if (tower.HoldsTheFlag)
                {
                    real++;
                }
            }

            Assert.That(real, Is.EqualTo(1));
        }

        /// <summary>
        /// A solo map's flag is not always on the tower that happened to be placed first.
        /// <see cref="LevelEdits.AddTower"/> promotes a side's first tower, which is the right
        /// rule for somebody building a map by hand and a tell in a generated one.
        /// </summary>
        [Test]
        public void ASoloMapDoesNotAlwaysHideTheFlagInTheSamePlace()
        {
            var seen = new HashSet<int>();

            for (int seed = 1; seed <= Seeds * 2; seed++)
            {
                LevelDefinition map = LevelGenerator.Generate(new MapOptions
                {
                    Seed = seed * 811,
                    Players = 1,
                });

                List<LevelTower> enemy = map.TowersFor(Team.Brown);
                for (int tower = 0; tower < enemy.Count; tower++)
                {
                    if (enemy[tower].HoldsTheFlag)
                    {
                        seen.Add(tower);
                    }
                }
            }

            Assert.That(seen.Count, Is.GreaterThan(1), "the flag was always on the same tower");
        }

        /// <summary>
        /// Every enemy tower on a solo map can be driven to from the one bunker. There is no
        /// second bunker for <see cref="LevelValidation"/>'s crossing rule to measure against,
        /// so this is the rule that replaces it - see <see cref="LevelGenerator.Faults"/>.
        /// </summary>
        [Test]
        public void EverySoloTowerCanBeReachedFromTheBunker()
        {
            for (int seed = 1; seed <= Seeds; seed++)
            {
                var options = new MapOptions { Seed = seed * 3571, Players = 1 };
                LevelDefinition map = LevelGenerator.Generate(options);

                Assert.That(
                    LevelGenerator.Faults(map, options),
                    Is.Empty,
                    $"solo seed {seed * 3571}");
            }
        }

        /// <summary>
        /// Nothing a generated map places stands in the sea, which is measured against the
        /// realised coastline rather than against the rectangles somebody drew - a wandering
        /// coast is exactly what puts a tree in the water behind a generator's back.
        /// </summary>
        [Test]
        public void NothingStandsInTheSea()
        {
            foreach (MapLayout layout in Layouts())
            {
                for (int seed = 1; seed <= Seeds; seed++)
                {
                    LevelDefinition map = LevelGenerator.Generate(new MapOptions
                    {
                        Seed = seed * 5449,
                        Layout = layout,
                        Difficulty = MapDifficulty.Hard,
                    });

                    foreach (LevelStructure structure in map.Structures)
                    {
                        if (structure.Structure == StructureKind.Bridge)
                        {
                            continue;
                        }

                        Assert.That(
                            map.IsOnLand(structure.Position, LevelValidation.ShoreMargin),
                            Is.True,
                            $"{layout} seed {seed * 5449}: a {structure.Kind} is in the sea");
                    }
                }
            }
        }

        /// <summary>
        /// A side's two towers are never close enough for one round to open both, which is the
        /// rule that makes a decoy cost something to see through.
        /// </summary>
        [Test]
        public void TowersAreNeverInsideOneBlastOfEachOther()
        {
            float needed = LevelGenerator.SpacingNeeded();

            foreach (MapLayout layout in Layouts())
            {
                for (int seed = 1; seed <= Seeds; seed++)
                {
                    LevelDefinition map = LevelGenerator.Generate(new MapOptions
                    {
                        Seed = seed * 6151,
                        Layout = layout,
                    });

                    foreach (Team side in Teams.Playing)
                    {
                        List<LevelTower> towers = map.TowersFor(side);
                        for (int a = 0; a < towers.Count; a++)
                        {
                            for (int b = a + 1; b < towers.Count; b++)
                            {
                                Assert.That(
                                    Vector3.Distance(towers[a].Position, towers[b].Position),
                                    Is.GreaterThan(needed),
                                    $"{layout} seed {seed * 6151}");
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// No piece of land runs off the edge of the world, including the room a natural coast
        /// needs for the three metres its wobble can add to it.
        /// </summary>
        [Test]
        public void NoLandRunsOffTheEdgeOfTheWorld()
        {
            foreach (MapDifficulty difficulty in Difficulties())
            {
                for (int seed = 1; seed <= Seeds; seed++)
                {
                    LevelDefinition map = LevelGenerator.Generate(new MapOptions
                    {
                        Seed = seed * 8191,
                        Difficulty = difficulty,
                    });

                    float extent = map.Bounds.HalfExtent;
                    foreach (LevelLand piece in map.Land)
                    {
                        float room = SurfaceTuning.For(piece.Ground).NaturalEdge
                            ? SurfaceNoise.Amplitude
                            : 0.0f;
                        float limit = extent - room + 0.001f;

                        Assert.That(Mathf.Abs(piece.MinX), Is.LessThanOrEqualTo(limit), piece.Name);
                        Assert.That(Mathf.Abs(piece.MaxX), Is.LessThanOrEqualTo(limit), piece.Name);
                        Assert.That(Mathf.Abs(piece.MinZ), Is.LessThanOrEqualTo(limit), piece.Name);
                        Assert.That(Mathf.Abs(piece.MaxZ), Is.LessThanOrEqualTo(limit), piece.Name);
                    }
                }
            }
        }

        /// <summary>
        /// A generated map is a level file like any other: it survives being written out and
        /// read back, which is what the editor's Save does to it the moment anybody keeps one.
        /// </summary>
        [Test]
        public void AGeneratedMapSurvivesTheFileFormat()
        {
            LevelDefinition map = LevelGenerator.Generate(new MapOptions
            {
                Seed = 606,
                Layout = MapLayout.Lagoon,
                Symmetry = MapSymmetry.Asymmetrical,
            });

            LevelDefinition read = JsonUtility.FromJson<LevelDefinition>(LevelFile.ToJson(map));

            Assert.That(read.SchemaVersion, Is.EqualTo(LevelDefinition.Schema));
            Assert.That(read.Seed, Is.EqualTo(map.Seed));
            Assert.That(read.Land.Length, Is.EqualTo(map.Land.Length));
            Assert.That(read.Structures.Length, Is.EqualTo(map.Structures.Length));
            Assert.That(LevelValidation.Problems(read), Is.Empty);
        }

        /// <summary>
        /// The dice are hashed rather than random, which is the whole basis of a seed meaning
        /// anything: two streams from one seed agree number for number.
        /// </summary>
        [Test]
        public void TheDiceAreTheSameDiceEveryTime()
        {
            var one = new Dice(12345);
            var two = new Dice(12345);

            for (int roll = 0; roll < 64; roll++)
            {
                Assert.That(two.Unit(), Is.EqualTo(one.Unit()));
            }
        }

        /// <summary>
        /// A branch does not disturb the stream it came off, which is what keeps one half of an
        /// asymmetrical map from redrawing the other when it changes.
        /// </summary>
        [Test]
        public void BranchingLeavesTheParentStreamAlone()
        {
            var alone = new Dice(999);
            float first = alone.Unit();
            float second = alone.Unit();

            var branched = new Dice(999);
            float again = branched.Unit();
            Dice child = branched.Branch(1);
            for (int roll = 0; roll < 20; roll++)
            {
                child.Unit();
            }

            Assert.That(again, Is.EqualTo(first));
            Assert.That(branched.Unit(), Is.EqualTo(second));
            Assert.That(branched.Branch(1).Unit(), Is.Not.EqualTo(branched.Branch(2).Unit()));
        }

        /// <summary>
        /// Every roll lands inside the range it was asked for, including at the ends.
        /// </summary>
        [Test]
        public void RollsStayInsideTheirRange()
        {
            var dice = new Dice(-77);

            for (int roll = 0; roll < 500; roll++)
            {
                Assert.That(dice.Unit(), Is.InRange(0.0f, 1.0f));
                Assert.That(dice.Between(-4.0f, 9.0f), Is.InRange(-4.0f, 9.0f));
                Assert.That(dice.Spread(3.0f), Is.InRange(-3.0f, 3.0f));
                Assert.That(dice.Upto(5), Is.InRange(0, 4));
                Assert.That(dice.Upto(1), Is.EqualTo(0));
                Assert.That(dice.Upto(0), Is.EqualTo(0));
            }
        }

        /// <summary>
        /// Unset options mean something rather than nothing, and settling them does not write
        /// back into what the dialogue is still showing.
        /// </summary>
        [Test]
        public void UnsetOptionsAreFilledInWithoutDisturbingTheOriginal()
        {
            var asked = new MapOptions
            {
                Difficulty = MapDifficulty.None,
                Symmetry = MapSymmetry.None,
                Layout = MapLayout.None,
                Players = 7,
                Name = "  Trimmed  ",
            };

            MapOptions settled = asked.Settled();

            Assert.That(settled.Difficulty, Is.EqualTo(MapDifficulty.Medium));
            Assert.That(settled.Symmetry, Is.EqualTo(MapSymmetry.Mirrored));
            Assert.That(settled.Layout, Is.EqualTo(MapLayout.None), "an unset layout is rolled");
            Assert.That(settled.Players, Is.EqualTo(MapOptions.MostPlayers));
            Assert.That(settled.Name, Is.EqualTo("Trimmed"));

            Assert.That(asked.Difficulty, Is.EqualTo(MapDifficulty.None));
            Assert.That(asked.Players, Is.EqualTo(7));
        }

        /// <summary>
        /// Asking for nothing at all still draws a map, because the generator is also what a
        /// test or a tool reaches for when it just wants somewhere to drive.
        /// </summary>
        [Test]
        public void GeneratingWithNoOptionsStillDrawsAMap()
        {
            LevelDefinition map = LevelGenerator.Generate(null);

            Assert.That(map, Is.Not.Null);
            Assert.That(LevelValidation.Problems(map), Is.Empty);
        }

        private static IEnumerable<MapLayout> Layouts()
        {
            yield return MapLayout.Island;
            yield return MapLayout.Channel;
            yield return MapLayout.Lagoon;
        }

        private static IEnumerable<MapDifficulty> Difficulties()
        {
            yield return MapDifficulty.Easy;
            yield return MapDifficulty.Medium;
            yield return MapDifficulty.Hard;
        }

        private static IEnumerable<MapSymmetry> Symmetries()
        {
            yield return MapSymmetry.Mirrored;
            yield return MapSymmetry.Asymmetrical;
        }
    }
}
