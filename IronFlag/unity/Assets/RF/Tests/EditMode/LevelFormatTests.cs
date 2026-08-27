using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Levels;
using IronFlag.Vehicles;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// The level file format: what survives a round trip, what a broken file does, and the
    /// two folders a level can come from.
    /// </summary>
    /// <remarks>
    /// This is the milestone's contract. Everything after M7 - more maps, a level editor,
    /// a map somebody edited in Notepad - stands on the claim that a level written out and
    /// read back is the same level, and that a file which is wrong says so instead of
    /// loading something subtly different.
    /// </remarks>
    public sealed class LevelFormatTests
    {
        private readonly List<string> written = new List<string>();

        /// <summary>
        /// Deletes anything a test dropped in the player's level folder.
        /// </summary>
        /// <remarks>
        /// These tests write into <see cref="LevelLibrary.UserFolder"/>, which is a real
        /// folder on the machine running them and outlives the editor. A leftover file there
        /// would shadow a shipped map for every later run - which is exactly the mechanism
        /// being tested, and exactly why it has to be cleaned up.
        /// </remarks>
        [TearDown]
        public void CleanUp()
        {
            foreach (string path in written)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            written.Clear();
        }

        /// <summary>
        /// A level written out and read back is the same level, down to the last prop.
        /// </summary>
        [Test]
        public void ALevelSurvivesBeingWrittenOutAndReadBack()
        {
            LevelDefinition original = SmallLevel();

            string json = LevelFile.ToJson(original);
            Assert.That(
                LevelFile.TryParse(json, "test", out LevelDefinition copy, out string problem),
                Is.True,
                problem);

            Assert.That(copy.Name, Is.EqualTo(original.Name));
            Assert.That(copy.SchemaVersion, Is.EqualTo(LevelDefinition.Schema));
            Assert.That(copy.Bounds.HalfExtent, Is.EqualTo(original.Bounds.HalfExtent));
            Assert.That(copy.Bounds.WaterDepth, Is.EqualTo(original.Bounds.WaterDepth));
            Assert.That(copy.Land.Length, Is.EqualTo(original.Land.Length));
            Assert.That(copy.Land[0].MinZ, Is.EqualTo(original.Land[0].MinZ));
            Assert.That(copy.Bunkers.Length, Is.EqualTo(original.Bunkers.Length));
            Assert.That(copy.BunkerFor(Team.Green).Position, Is.EqualTo(new Vector3(0.0f, 0.0f, -30.0f)));
            Assert.That(copy.Towers.Length, Is.EqualTo(original.Towers.Length));
            Assert.That(copy.Towers[0].HoldsTheFlag, Is.True);
            Assert.That(copy.Structures.Length, Is.EqualTo(original.Structures.Length));
            Assert.That(copy.Structures[0].Structure, Is.EqualTo(StructureKind.Bridge));
            Assert.That(copy.Structures[0].Position.y, Is.EqualTo(-1.2f));

            foreach (VehicleKind kind in VehicleRoster.Kinds)
            {
                Assert.That(
                    copy.Reserve.For(kind),
                    Is.EqualTo(original.Reserve.For(kind)),
                    $"the level came back with a different number of {kind}s");
            }
        }

        /// <summary>
        /// Every map written before version 4 is a map played on the standard allotment,
        /// rather than one with no vehicles on it at all.
        /// </summary>
        /// <remarks>
        /// This is the whole of what makes the reserve a safe field to have added. A file
        /// that says nothing about vehicles is the common case - every map on disk before
        /// this feature, and every map anybody writes by hand from an older example - and
        /// the failure it must not have is the quiet one: a side that starts a match with
        /// nothing to drive and no message anywhere saying why.
        /// </remarks>
        [Test]
        public void ALevelFileWithNoReserveGetsTheStandardAllotment()
        {
            const string older = "{\"SchemaVersion\": 3, \"Name\": \"Older\"}";

            Assert.That(
                LevelFile.TryParse(older, "older", out LevelDefinition level, out string problem),
                Is.True,
                problem);

            Assert.That(level.Reserve, Is.Not.Null, "an older map came back with no reserve at all");
            Assert.That(level.Reserve.For(VehicleKind.Jeep), Is.EqualTo(LevelReserve.DefaultJeeps));
            Assert.That(level.Reserve.For(VehicleKind.Tank), Is.EqualTo(LevelReserve.DefaultTanks));
            Assert.That(level.Reserve.For(VehicleKind.Asv), Is.EqualTo(LevelReserve.DefaultAsvs));
            Assert.That(
                level.Reserve.For(VehicleKind.Helicopter),
                Is.EqualTo(LevelReserve.DefaultHelicopters));
        }

        /// <summary>
        /// The reserve answers for every vehicle in the roster, and refuses a stock below
        /// zero rather than carrying one.
        /// </summary>
        /// <remarks>
        /// The completeness half of this is the point: a fifth vehicle added to
        /// <see cref="VehicleRoster.Kinds"/> and not to <see cref="LevelReserve"/> would be
        /// a vehicle every level silently gives you none of, and the panel would say
        /// NONE LEFT from the first frame of every match.
        /// </remarks>
        [Test]
        public void TheReserveHoldsAStockOfEveryVehicleInTheRoster()
        {
            var stock = new LevelReserve();

            foreach (VehicleKind kind in VehicleRoster.Kinds)
            {
                Assert.That(stock.For(kind), Is.GreaterThan(0), $"a side starts with no {kind}");

                stock.Set(kind, 5);
                Assert.That(stock.For(kind), Is.EqualTo(5), $"{kind} cannot be stocked");

                stock.Set(kind, -3);
                Assert.That(stock.For(kind), Is.Zero, $"{kind} took a stock below zero");
            }

            Assert.That(stock.For(VehicleKind.None), Is.Zero, "no vehicle at all was stocked");
        }

        /// <summary>
        /// The standard allotment is deeper in jeeps than in anything else, on purpose.
        /// </summary>
        /// <remarks>
        /// The jeep is the only vehicle that can win and the one that dies to two hits, and
        /// running out of them is losing the match. If this ever reads the other way round,
        /// the game's whole shape has changed and it was not changed here.
        /// </remarks>
        [Test]
        public void TheStandardAllotmentIsDeepestInJeeps()
        {
            Assert.That(LevelReserve.DefaultJeeps, Is.GreaterThan(LevelReserve.DefaultTanks));
            Assert.That(LevelReserve.DefaultJeeps, Is.GreaterThan(LevelReserve.DefaultAsvs));
            Assert.That(LevelReserve.DefaultJeeps, Is.GreaterThan(LevelReserve.DefaultHelicopters));
        }

        /// <summary>
        /// The thing a level file may never contain: an enum written as the number behind it.
        /// </summary>
        /// <remarks>
        /// A file that said <c>"Kind": 4</c> would still load today and would mean something
        /// else the day a row is inserted into <see cref="StructureKind"/> - and no diff
        /// would show it. Names are the whole reason the format is worth hand-editing.
        /// </remarks>
        [Test]
        public void EveryKindAndTeamIsWrittenAsAName()
        {
            string json = LevelFile.ToJson(SmallLevel());

            StringAssert.Contains("\"Bridge\"", json);
            StringAssert.Contains("\"Green\"", json);
            StringAssert.Contains("\"Brown\"", json);

            foreach (StructureKind kind in StructureTuning.Roster())
            {
                Assert.That(
                    json.Contains($"\"Kind\": {(int)kind}"),
                    Is.False,
                    $"{kind} was written as a number");
            }
        }

        /// <summary>
        /// A misspelled prop costs one prop, not the map - and never quietly becomes another.
        /// </summary>
        [Test]
        public void AnUnknownNameResolvesToNothingRatherThanToSomethingElse()
        {
            Assert.That(LevelNames.ToStructure("Warehouse"), Is.EqualTo(StructureKind.None));
            Assert.That(LevelNames.ToStructure(string.Empty), Is.EqualTo(StructureKind.None));
            Assert.That(LevelNames.ToTeam("Purple"), Is.EqualTo(Team.None));

            // Case is forgiven, because a person is typing this.
            Assert.That(LevelNames.ToStructure("bridge"), Is.EqualTo(StructureKind.Bridge));
            Assert.That(LevelNames.ToTeam(" green "), Is.EqualTo(Team.Green));
        }

        /// <summary>
        /// A number where a name belongs is refused, rather than accepted as an ordinal.
        /// </summary>
        /// <remarks>
        /// <see cref="System.Enum.TryParse{T}(string, bool, out T)"/> accepts digits and
        /// hands back whatever member happens to sit at that value - including members that
        /// do not exist. That is the one parse this format cannot afford.
        /// </remarks>
        [Test]
        public void ANumberIsNotAName()
        {
            Assert.That(LevelNames.ToStructure("4"), Is.EqualTo(StructureKind.None));
            Assert.That(LevelNames.ToStructure("99"), Is.EqualTo(StructureKind.None));
            Assert.That(LevelNames.ToTeam("1"), Is.EqualTo(Team.None));
        }

        /// <summary>
        /// A level from a newer build is refused whole rather than read in part.
        /// </summary>
        [Test]
        public void ALevelFromTheFutureIsRefused()
        {
            LevelDefinition ahead = SmallLevel();
            ahead.SchemaVersion = LevelDefinition.Schema + 1;

            Assert.That(
                LevelFile.TryParse(LevelFile.ToJson(ahead), "tomorrow", out _, out string problem),
                Is.False,
                "a level from a newer build loaded");
            StringAssert.Contains("tomorrow", problem);
            StringAssert.Contains($"version {LevelDefinition.Schema + 1}", problem);
        }

        /// <summary>
        /// Every way a file can fail says which file, in a sentence.
        /// </summary>
        [Test]
        public void ABrokenFileSaysWhichOneAndWhy()
        {
            Assert.That(
                LevelFile.TryRead("no/such/level.json", out _, out string missing), Is.False);
            StringAssert.Contains("no/such/level.json", missing);

            Assert.That(LevelFile.TryParse(string.Empty, "blank", out _, out string empty), Is.False);
            StringAssert.Contains("blank", empty);

            Assert.That(
                LevelFile.TryParse("this is not json", "rubbish", out _, out string bad), Is.False);
            StringAssert.Contains("rubbish", bad);
        }

        /// <summary>
        /// A level the player has edited shadows the one that shipped, which is what makes a
        /// level editor possible without writing into the install folder.
        /// </summary>
        [Test]
        public void APlayersOwnCopyOfALevelWinsOverTheShippedOne()
        {
            const string name = "iron-flag-format-test";

            Assert.That(
                LevelLibrary.PathFor(name),
                Is.EqualTo(LevelLibrary.ShippedPathFor(name)),
                "an unedited level came from somewhere other than the shipped folder");

            string mine = LevelLibrary.UserPathFor(name);
            written.Add(mine);
            Assert.That(LevelFile.TryWrite(mine, SmallLevel(), out string problem), Is.True, problem);

            Assert.That(
                LevelLibrary.PathFor(name),
                Is.EqualTo(mine),
                "the player's own copy was ignored");
            Assert.That(LevelLibrary.Names(), Contains.Item(name));
        }

        /// <summary>
        /// A level file is pretty-printed, because it lives in source control.
        /// </summary>
        [Test]
        public void ALevelFileIsWrittenToBeRead()
        {
            string json = LevelFile.ToJson(SmallLevel());

            Assert.That(json.Contains("\n"), Is.True, "the whole map is on one line");
            Assert.That(
                json.Split('\n').Length,
                Is.GreaterThan(20),
                "the map is too compressed for a diff to be worth reading");
        }

        /// <summary>
        /// Builds a small but complete level to push through the format.
        /// </summary>
        /// <returns>A level with one of everything the format carries.</returns>
        private static LevelDefinition SmallLevel()
            => new LevelDefinition
            {
                Name = "Test Water",
                Description = "Two shores and a bridge.",
                Bounds = new LevelBounds { HalfExtent = 60.0f, WaterDepth = 0.7f },
                // Deliberately not the standard allotment: a reserve left at its defaults
                // would round-trip whether or not it was written out at all.
                Reserve = new LevelReserve { Jeeps = 4, Tanks = 1, Asvs = 2, Helicopters = 0 },
                Land = new[]
                {
                    new LevelLand { Name = "South", MinX = -40, MaxX = 40, MinZ = -40, MaxZ = -6 },
                    new LevelLand { Name = "North", MinX = -40, MaxX = 40, MinZ = 6, MaxZ = 40 },
                },
                Bunkers = new[]
                {
                    new LevelBunker
                    {
                        Team = nameof(Team.Green),
                        Position = new Vector3(0.0f, 0.0f, -30.0f),
                    },
                    new LevelBunker
                    {
                        Team = nameof(Team.Brown),
                        Position = new Vector3(0.0f, 0.0f, 30.0f),
                        YawDegrees = 180.0f,
                    },
                },
                Towers = new[]
                {
                    new LevelTower
                    {
                        Team = nameof(Team.Green),
                        HoldsTheFlag = true,
                        Position = new Vector3(-15.0f, 0.0f, -25.0f),
                    },
                },
                Structures = new[]
                {
                    new LevelStructure
                    {
                        Kind = nameof(StructureKind.Bridge),
                        Name = "The bridge",
                        Position = new Vector3(0.0f, -1.2f, 0.0f),
                    },
                    new LevelStructure
                    {
                        Kind = nameof(StructureKind.DepotFuel),
                        Position = new Vector3(20.0f, 0.0f, -20.0f),
                        FuelRate = 0.12f,
                    },
                },
            };
    }
}
