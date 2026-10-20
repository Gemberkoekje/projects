using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Editing;
using IronFlag.Levels;
using IronFlag.Vehicles;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// Everything the level editor does to a map, checked without an editor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The editor is a mouse, some panels and a camera wrapped around
    /// <see cref="LevelEdits"/>, <see cref="LevelPick"/> and <see cref="EditHistory"/> - and
    /// all three of those are plain functions on a <see cref="LevelDefinition"/>. So this is
    /// where the feature is actually tested: no scene, no canvas, no pointer, and no chance
    /// of a rule being right in the tests and wrong in the panel that calls it.
    /// </para>
    /// <para>
    /// Several of these are about traps a person hand-editing JSON has to remember and an
    /// editor should not make them: that a bridge is placed sunk to its deck, that a depot
    /// with no rate is scenery wearing a depot's model, and that a side must have exactly one
    /// real tower. Every one of those produces a map that looks completely normal.
    /// </para>
    /// </remarks>
    public sealed class LevelEditingTests
    {
        /// <summary>
        /// A turret is placed facing the side it was built to shoot at, and every one of a
        /// side's faces the same way. Placing them all square to the map instead would
        /// point half of them at their own back line - a gun the map paid for and nobody
        /// aimed, and one that looks exactly like a working one.
        /// </summary>
        [Test]
        public void AnEmplacementIsPlacedFacingTheOtherSide()
        {
            LevelDefinition level = LevelEdits.Starter("Test Map");

            int green = LevelEdits.AddStructure(
                level, StructureKind.Turret, new Vector3(-20.0f, 0.0f, -30.0f), Team.Green);
            int brown = LevelEdits.AddStructure(
                level, StructureKind.Turret, new Vector3(20.0f, 0.0f, 30.0f), Team.Brown);
            int elsewhere = LevelEdits.AddStructure(
                level, StructureKind.Turret, new Vector3(-4.0f, 0.0f, 12.0f), Team.Green);
            int tree = LevelEdits.AddStructure(
                level, StructureKind.Tree, new Vector3(6.0f, 0.0f, 6.0f), Team.None);

            Assert.That(
                level.Structures[green].YawDegrees,
                Is.EqualTo(0.0f).Within(0.001f),
                "green sits at negative Z, so its guns look up the map");
            Assert.That(
                level.Structures[brown].YawDegrees,
                Is.EqualTo(180.0f).Within(0.001f),
                "a brown emplacement is facing its own bunker");
            Assert.That(
                level.Structures[elsewhere].YawDegrees,
                Is.EqualTo(level.Structures[green].YawDegrees).Within(0.001f),
                "two of a side's emplacements came out on different headings");
            Assert.That(
                level.Structures[tree].YawDegrees,
                Is.EqualTo(0.0f).Within(0.001f),
                "a tree was given a heading, so the rule is not about turrets");
        }

        /// <summary>
        /// A mirrored turret is the other side's turret. Copying the side across instead
        /// would give one player both emplacements on a map that still looked symmetrical,
        /// which is the worst kind of asymmetry: the invisible kind.
        /// </summary>
        [Test]
        public void MirroringATurretHandsItToTheOtherSide()
        {
            LevelDefinition level = LevelEdits.Starter("Test Map");
            int placed = LevelEdits.AddStructure(
                level, StructureKind.Turret, new Vector3(-20.0f, 0.0f, -30.0f), Team.Green);
            level.Structures[placed].YawDegrees = 40.0f;

            EditSelection copy = LevelEdits.Mirror(
                level, new EditSelection(EditTarget.Structure, placed));

            Assert.That(copy.Target, Is.EqualTo(EditTarget.Structure));
            LevelStructure mirrored = level.Structures[copy.Index];
            Assert.That(mirrored.Team, Is.EqualTo(Team.Brown), "both turrets belong to Green");
            Assert.That(mirrored.Position.x, Is.EqualTo(20.0f).Within(0.001f));
            Assert.That(mirrored.Position.z, Is.EqualTo(30.0f).Within(0.001f));
            Assert.That(mirrored.YawDegrees, Is.EqualTo(220.0f).Within(0.001f));
        }

        /// <summary>
        /// And a mirrored tree stays on no side, so the same rule covers both without the
        /// mirror tool having to know which kinds care.
        /// </summary>
        [Test]
        public void MirroringSceneryLeavesItOnNoSide()
        {
            LevelDefinition level = LevelEdits.Starter("Test Map");
            int placed = LevelEdits.AddStructure(
                level, StructureKind.Tree, new Vector3(-20.0f, 0.0f, -30.0f), Team.Green);

            Assert.That(
                level.Structures[placed].Team,
                Is.EqualTo(Team.None),
                "a tree kept the side the palette had selected");

            EditSelection copy = LevelEdits.Mirror(
                level, new EditSelection(EditTarget.Structure, placed));

            Assert.That(level.Structures[copy.Index].Team, Is.EqualTo(Team.None));
        }

        /// <summary>
        /// The whole feature, in one test. Mirroring makes a pair, and from then on the two
        /// move as one thing - so a map drawn in halves stays drawn in halves while somebody
        /// works on it rather than only at the moment they last pressed mirror.
        /// </summary>
        [Test]
        public void MovingSomethingPairedMovesItsOppositeNumber()
        {
            var level = new LevelDefinition();
            int placed = LevelEdits.AddStructure(
                level, StructureKind.Tree, new Vector3(10.0f, 0.0f, -20.0f));
            var picked = new EditSelection(EditTarget.Structure, placed);

            EditSelection twin = LevelEdits.Mirror(level, picked);
            Assert.That(twin.IsSomething, Is.True);

            LevelEdits.MoveTo(level, picked, new Vector3(30.0f, 0.0f, -5.0f));

            Assert.That(level.Structures[placed].Position.x, Is.EqualTo(30.0f).Within(0.001f));
            Assert.That(level.Structures[placed].Position.z, Is.EqualTo(-5.0f).Within(0.001f));
            Assert.That(
                level.Structures[twin.Index].Position.x,
                Is.EqualTo(-30.0f).Within(0.001f),
                "the twin stayed where the first mirror put it");
            Assert.That(
                level.Structures[twin.Index].Position.z, Is.EqualTo(5.0f).Within(0.001f));
        }

        /// <summary>
        /// The bug the pairing was built for. Mirror, move the original, press mirror again:
        /// what used to happen is that the first copy stayed where it was and a third prop
        /// appeared, so the gesture that looked like "put this back in step" was the one that
        /// broke the map. There is nothing to press twice now, because it never went out of
        /// step.
        /// </summary>
        [Test]
        public void MirroringSomethingThatAlreadyHasATwinMakesNothing()
        {
            var level = new LevelDefinition();
            int placed = LevelEdits.AddStructure(
                level, StructureKind.Tree, new Vector3(10.0f, 0.0f, -20.0f));
            var picked = new EditSelection(EditTarget.Structure, placed);

            LevelEdits.Mirror(level, picked);
            LevelEdits.MoveTo(level, picked, new Vector3(30.0f, 0.0f, -5.0f));

            Assert.That(
                LevelEdits.Mirror(level, picked).IsSomething,
                Is.False,
                "mirroring a thing that already has an opposite number made a second one");
            Assert.That(level.Structures.Length, Is.EqualTo(2));
        }

        /// <summary>
        /// A twin faces half a turn round from the thing it belongs to. Two twins on the same
        /// compass heading would be one facing the field and one facing its own back wall.
        /// </summary>
        [Test]
        public void TurningSomethingPairedTurnsItsTwinTheOtherWayRound()
        {
            var level = new LevelDefinition();
            int placed = LevelEdits.AddStructure(
                level, StructureKind.Turret, new Vector3(10.0f, 0.0f, -20.0f), Team.Green);
            var picked = new EditSelection(EditTarget.Structure, placed);
            EditSelection twin = LevelEdits.Mirror(level, picked);

            LevelEdits.SetYaw(level, picked, 30.0f);

            Assert.That(level.Structures[placed].YawDegrees, Is.EqualTo(30.0f).Within(0.001f));
            Assert.That(
                level.Structures[twin.Index].YawDegrees, Is.EqualTo(210.0f).Within(0.001f));
        }

        /// <summary>
        /// Land is paired by its four edges rather than by a position, so a coastline pulled
        /// about on one side is the same coastline on the other. Turning a box about the
        /// origin turns its west edge into its east one, which is the arithmetic this is
        /// really checking.
        /// </summary>
        [Test]
        public void ResizingPairedLandResizesItsOppositeNumber()
        {
            var level = new LevelDefinition();
            int drawn = LevelEdits.AddLand(
                level, new Vector3(10.0f, 0.0f, 10.0f), new Vector3(30.0f, 0.0f, 40.0f), "Shore");
            EditSelection twin = LevelEdits.Mirror(
                level, new EditSelection(EditTarget.Land, drawn));

            LevelEdits.SetLandEdges(level, drawn, 10.0f, 50.0f, 10.0f, 40.0f);

            LevelLand across = level.Land[twin.Index];
            Assert.That(across.MinX, Is.EqualTo(-50.0f).Within(0.001f));
            Assert.That(across.MaxX, Is.EqualTo(-10.0f).Within(0.001f));
            Assert.That(across.MinZ, Is.EqualTo(-40.0f).Within(0.001f));
            Assert.That(across.MaxZ, Is.EqualTo(-10.0f).Within(0.001f));
        }

        /// <summary>
        /// A pair goes as one thing. Deleting only the end that was clicked would leave a map
        /// that was symmetrical a moment ago with one unanswered prop on it, and nothing
        /// would complain.
        /// </summary>
        [Test]
        public void DeletingSomethingPairedTakesItsOppositeNumberToo()
        {
            var level = new LevelDefinition();
            LevelEdits.AddStructure(level, StructureKind.BuildingA, new Vector3(1.0f, 0.0f, 1.0f));
            int placed = LevelEdits.AddStructure(
                level, StructureKind.Tree, new Vector3(10.0f, 0.0f, -20.0f));
            var picked = new EditSelection(EditTarget.Structure, placed);
            LevelEdits.Mirror(level, picked);
            LevelEdits.AddStructure(level, StructureKind.BuildingB, new Vector3(2.0f, 0.0f, 2.0f));

            Assert.That(LevelEdits.Remove(level, picked), Is.True);

            Assert.That(level.Structures.Length, Is.EqualTo(2), "one end of the pair survived");
            foreach (LevelStructure left in level.Structures)
            {
                Assert.That(
                    left.Structure,
                    Is.Not.EqualTo(StructureKind.Tree),
                    "the wrong props were taken off the map");
            }
        }

        /// <summary>
        /// The per-item off switch, which is the reason a pair is held on each thing rather
        /// than as one setting on the map: a map drawn in halves is still allowed the one
        /// causeway that is deliberately on one side only.
        /// </summary>
        [Test]
        public void UnpairingLetsBothEndsMoveOnTheirOwn()
        {
            var level = new LevelDefinition();
            int placed = LevelEdits.AddStructure(
                level, StructureKind.Tree, new Vector3(10.0f, 0.0f, -20.0f));
            var picked = new EditSelection(EditTarget.Structure, placed);
            EditSelection twin = LevelEdits.Mirror(level, picked);

            Assert.That(LevelEdits.Unpair(level, picked), Is.True);
            LevelEdits.MoveTo(level, picked, new Vector3(30.0f, 0.0f, -5.0f));

            Assert.That(
                level.Structures[twin.Index].Position.x,
                Is.EqualTo(-10.0f).Within(0.001f),
                "the twin followed something it had been unpaired from");
            Assert.That(
                LevelEdits.PairOf(level, twin),
                Is.Zero,
                "the far end still claimed to be in a pair");
        }

        /// <summary>
        /// Both ends let go, so mirroring either one afterwards makes a fresh pair rather
        /// than being refused by a number left behind on the twin.
        /// </summary>
        [Test]
        public void SomethingUnpairedCanBeMirroredAgain()
        {
            var level = new LevelDefinition();
            int placed = LevelEdits.AddStructure(
                level, StructureKind.Tree, new Vector3(10.0f, 0.0f, -20.0f));
            var picked = new EditSelection(EditTarget.Structure, placed);

            LevelEdits.Mirror(level, picked);
            LevelEdits.Unpair(level, picked);

            Assert.That(LevelEdits.Mirror(level, picked).IsSomething, Is.True);
            Assert.That(level.Structures.Length, Is.EqualTo(3));
        }

        /// <summary>
        /// A pair is held as a number and not as an index, and this is the reason: deleting
        /// anything shifts every index after it, and a stored index would then point at the
        /// wrong prop with nothing to say so.
        /// </summary>
        [Test]
        public void APairSurvivesTheThingsInFrontOfItBeingDeleted()
        {
            var level = new LevelDefinition();
            int first = LevelEdits.AddStructure(
                level, StructureKind.BuildingA, new Vector3(1.0f, 0.0f, 1.0f));
            int placed = LevelEdits.AddStructure(
                level, StructureKind.Tree, new Vector3(10.0f, 0.0f, -20.0f));
            LevelEdits.Mirror(level, new EditSelection(EditTarget.Structure, placed));

            LevelEdits.Remove(level, new EditSelection(EditTarget.Structure, first));

            var moved = new EditSelection(EditTarget.Structure, placed - 1);
            LevelEdits.MoveTo(level, moved, new Vector3(30.0f, 0.0f, -5.0f));

            EditSelection twin = LevelEdits.TwinOf(level, moved);
            Assert.That(twin.IsSomething, Is.True, "the pair was lost with the prop in front of it");
            Assert.That(
                level.Structures[twin.Index].Position.x, Is.EqualTo(-30.0f).Within(0.001f));
        }

        /// <summary>
        /// The twin of a green thing is a brown one, by definition of this game's mirror -
        /// so handing one end of a pair to a side hands the other end to the other side.
        /// Copying the side across would leave one player holding both emplacements of a
        /// pair on a map that still looked symmetrical.
        /// </summary>
        [Test]
        public void ChangingAPairedThingsSideGivesItsTwinTheOtherOne()
        {
            var level = new LevelDefinition();
            int placed = LevelEdits.AddStructure(
                level, StructureKind.Turret, new Vector3(10.0f, 0.0f, -20.0f), Team.Green);
            var picked = new EditSelection(EditTarget.Structure, placed);
            EditSelection twin = LevelEdits.Mirror(level, picked);

            Assert.That(LevelEdits.SetSide(level, picked, Team.Brown), Is.True);

            Assert.That(level.Structures[placed].Team, Is.EqualTo(Team.Brown));
            Assert.That(level.Structures[twin.Index].Team, Is.EqualTo(Team.Green));
        }

        /// <summary>
        /// A side must have exactly one real tower, and on a map built in pairs the two flags
        /// belong on towers that are each other's. Promoting one end only leaves the other
        /// side's flag on a pyramid somewhere else, on a map that still looks symmetrical.
        /// </summary>
        [Test]
        public void PuttingTheFlagOnAPairedTowerPutsTheOtherFlagOnItsTwin()
        {
            var level = new LevelDefinition();
            int real = LevelEdits.AddTower(level, Team.Green, new Vector3(-18.0f, 0.0f, -56.0f));
            int decoy = LevelEdits.AddTower(level, Team.Green, new Vector3(18.0f, 0.0f, -56.0f));
            LevelEdits.Mirror(level, new EditSelection(EditTarget.Tower, real));
            EditSelection twin = LevelEdits.Mirror(
                level, new EditSelection(EditTarget.Tower, decoy));

            Assert.That(LevelEdits.MakeRealTower(level, decoy), Is.True);

            Assert.That(level.Towers[decoy].HoldsTheFlag, Is.True);
            Assert.That(
                level.Towers[twin.Index].HoldsTheFlag,
                Is.True,
                "the brown flag stayed on the tower that is no longer green's opposite number");
            Assert.That(level.Towers[real].HoldsTheFlag, Is.False);
        }

        /// <summary>
        /// What a map written before pairs existed needs, and the only way the shipped maps
        /// get any: everything on them already stands exactly opposite its twin, so this
        /// records a relationship that is visibly there. It moves nothing.
        /// </summary>
        [Test]
        public void PairingTheHalvesFindsEveryTwinOnASymmetricalMap()
        {
            LevelDefinition level = LevelEdits.Starter("Test Map");
            foreach (LevelTower tower in level.Towers)
            {
                tower.Pair = 0;
            }

            foreach (LevelBunker bunker in level.Bunkers)
            {
                bunker.Pair = 0;
            }

            Vector3 before = level.Towers[0].Position;

            Assert.That(LevelEdits.PairTheHalves(level), Is.EqualTo(3), "two tower pairs and a bunker pair");
            Assert.That(level.Towers[0].Position, Is.EqualTo(before), "pairing moved something");
            Assert.That(
                LevelEdits.PairTheHalves(level),
                Is.Zero,
                "running it twice paired things that were already paired");
        }

        /// <summary>
        /// And nothing on a map that is not symmetrical, because a pass that guesses wrong
        /// welds two props together for the rest of the map's life.
        /// </summary>
        [Test]
        public void PairingTheHalvesLeavesAnUnsymmetricalMapAlone()
        {
            var level = new LevelDefinition();
            LevelEdits.AddStructure(level, StructureKind.Tree, new Vector3(10.0f, 0.0f, -20.0f));
            LevelEdits.AddStructure(level, StructureKind.Tree, new Vector3(-4.0f, 0.0f, 33.0f));
            LevelEdits.AddLand(
                level, new Vector3(-60.0f, 0.0f, -60.0f), new Vector3(-10.0f, 0.0f, 5.0f), "Shore");

            Assert.That(LevelEdits.PairTheHalves(level), Is.Zero);
        }

        /// <summary>
        /// Standing in the right place is not enough. A tree where a turret's opposite number
        /// would be is not that turret's opposite number, and pairing them would make moving
        /// the turret move a tree.
        /// </summary>
        [Test]
        public void PairingTheHalvesNeverMarriesTwoDifferentThings()
        {
            var level = new LevelDefinition();
            LevelEdits.AddStructure(
                level, StructureKind.Turret, new Vector3(10.0f, 0.0f, -20.0f), Team.Green);
            LevelEdits.AddStructure(level, StructureKind.Tree, new Vector3(-10.0f, 0.0f, 20.0f));

            Assert.That(LevelEdits.PairTheHalves(level), Is.Zero);
        }

        /// <summary>
        /// Nor is being the same kind in the right place, when both belong to one player: a
        /// pair is one side's thing and the other side's, and two green emplacements at
        /// opposite ends of the map are one player with both of them.
        /// </summary>
        [Test]
        public void PairingTheHalvesNeverMarriesTwoThingsBelongingToOneSide()
        {
            var level = new LevelDefinition();
            LevelEdits.AddStructure(
                level, StructureKind.Turret, new Vector3(10.0f, 0.0f, -20.0f), Team.Green);
            LevelEdits.AddStructure(
                level, StructureKind.Turret, new Vector3(-10.0f, 0.0f, 20.0f), Team.Green);

            Assert.That(LevelEdits.PairTheHalves(level), Is.Zero);
        }

        /// <summary>
        /// A new map opens on one that is already paired up, because it is already built in
        /// pairs - so dragging the green bunker on a map nobody has touched moves brown's.
        /// The two depots are deliberately not a pair: they stand at opposite points, but one
        /// is a fuel dump and the other an ammunition dump.
        /// </summary>
        [Test]
        public void ANewMapIsBuiltInPairsAndSaysSo()
        {
            LevelDefinition level = LevelEdits.Starter("Test Map");

            var green = new EditSelection(EditTarget.Bunker, 0);
            EditSelection brown = LevelEdits.TwinOf(level, green);
            Assert.That(brown.IsSomething, Is.True, "a new map's two bunkers are not a pair");

            LevelEdits.MoveTo(level, green, new Vector3(6.0f, 0.0f, -40.0f));
            Assert.That(level.Bunkers[brown.Index].Position.x, Is.EqualTo(-6.0f).Within(0.001f));
            Assert.That(level.Bunkers[brown.Index].Position.z, Is.EqualTo(40.0f).Within(0.001f));

            for (int tower = 0; tower < level.Towers.Length; tower++)
            {
                Assert.That(
                    LevelEdits.TwinOf(level, new EditSelection(EditTarget.Tower, tower)).IsSomething,
                    Is.True,
                    $"tower {tower} has no opposite number");
            }

            Assert.That(
                LevelEdits.TwinOf(level, new EditSelection(EditTarget.Structure, 0)).IsSomething,
                Is.False,
                "the fuel dump was married to the ammunition dump");
        }

        /// <summary>
        /// Pairs are part of the file rather than something the editor holds while it is
        /// open, so a map saved half-edited comes back knowing which of its props are each
        /// other's.
        /// </summary>
        [Test]
        public void PairsSurviveBeingWrittenOutAndReadBack()
        {
            LevelDefinition level = LevelEdits.Starter("Test Map");
            LevelDefinition reread = LevelEdits.Copy(level);

            var green = new EditSelection(EditTarget.Bunker, 0);
            Assert.That(LevelEdits.PairOf(reread, green), Is.EqualTo(LevelEdits.PairOf(level, green)));
            Assert.That(LevelEdits.TwinOf(reread, green).IsSomething, Is.True);
        }

        /// <summary>
        /// A pair number that ended up on one thing only - a hand-edited file, or half a pair
        /// dropped by a build that had never heard of them - reads as unpaired rather than as
        /// a broken map. Pairing is an editing annotation and no rule of play looks at it.
        /// </summary>
        [Test]
        public void HalfAPairIsSimplyNotAPair()
        {
            var level = new LevelDefinition();
            int placed = LevelEdits.AddStructure(
                level, StructureKind.Tree, new Vector3(10.0f, 0.0f, -20.0f));
            level.Structures[placed].Pair = 9;
            var picked = new EditSelection(EditTarget.Structure, placed);

            Assert.That(LevelEdits.TwinOf(level, picked).IsSomething, Is.False);
            Assert.That(LevelEdits.Mirror(level, picked).IsSomething, Is.True, "it could not be mirrored");
        }


        [Test]
        public void ANewMapIsPlayableBeforeAnybodyTouchesIt()
        {
            LevelDefinition made = LevelEdits.Starter("Test Map");

            Assert.That(
                LevelValidation.Problems(made),
                Is.Empty,
                "a new map opens the editor on a list of things that are already wrong");
        }

        [Test]
        public void ACopySharesNothingWithWhatItWasCopiedFrom()
        {
            LevelDefinition original = LevelEdits.Starter("Test Map");
            LevelDefinition copy = LevelEdits.Copy(original);

            copy.Name = "Changed";
            copy.Land[0].MinX = -999.0f;
            copy.Towers[0].HoldsTheFlag = false;

            Assert.That(original.Name, Is.EqualTo("Test Map"));
            Assert.That(original.Land[0].MinX, Is.Not.EqualTo(-999.0f));
            Assert.That(original.Towers[0].HoldsTheFlag, Is.True);
        }

        /// <summary>
        /// The gotcha M7 wrote down and nothing in a level file can catch: a bridge placed at
        /// ground level is a metre-high step that reads as a perfectly good bridge.
        /// </summary>
        [Test]
        public void APlacedBridgeIsSunkToItsDeck()
        {
            LevelDefinition level = LevelEdits.Starter("Test Map");
            int placed = LevelEdits.AddStructure(
                level, StructureKind.Bridge, new Vector3(10.0f, 0.0f, 4.0f));

            Assert.That(placed, Is.GreaterThanOrEqualTo(0));
            Assert.That(
                level.Structures[placed].Position.y,
                Is.EqualTo(LevelEdits.BridgeSink).Within(0.0001f),
                "a bridge placed in the editor would stand a metre proud of the water");
        }

        [Test]
        public void DraggingABridgeLeavesItSunk()
        {
            LevelDefinition level = LevelEdits.Starter("Test Map");
            int placed = LevelEdits.AddStructure(level, StructureKind.Bridge, Vector3.zero);
            var moved = new EditSelection(EditTarget.Structure, placed);

            LevelEdits.MoveTo(level, moved, new Vector3(20.0f, 7.0f, -8.0f));

            Assert.That(
                level.Structures[placed].Position,
                Is.EqualTo(new Vector3(20.0f, LevelEdits.BridgeSink, -8.0f)),
                "dragging a bridge lifted it out of the channel it spans");
        }

        /// <summary>
        /// A depot placed with no rate is a model of a depot: it looks exactly like the real
        /// thing on the map and refuels nobody.
        /// </summary>
        [Test]
        public void APlacedDepotActuallySuppliesSomething()
        {
            LevelDefinition level = LevelEdits.Starter("Test Map");

            int fuel = LevelEdits.AddStructure(level, StructureKind.DepotFuel, Vector3.zero);
            int ammo = LevelEdits.AddStructure(level, StructureKind.DepotAmmo, Vector3.zero);

            Assert.That(level.Structures[fuel].FuelRate, Is.GreaterThan(0.0f));
            Assert.That(level.Structures[fuel].AmmoRate, Is.EqualTo(0.0f));
            Assert.That(level.Structures[ammo].AmmoRate, Is.GreaterThan(0.0f));
            Assert.That(level.Structures[ammo].FuelRate, Is.EqualTo(0.0f));
        }

        [Test]
        public void APlacedTreeSuppliesNothing()
        {
            LevelDefinition level = LevelEdits.Starter("Test Map");
            int placed = LevelEdits.AddStructure(level, StructureKind.Tree, Vector3.zero);

            Assert.That(level.Structures[placed].Supplies, Is.False);
        }

        /// <summary>
        /// A side's first tower is the real one, so a half-built map is legal at every step
        /// rather than only once somebody remembers to promote one.
        /// </summary>
        [Test]
        public void TheFirstTowerASideGetsIsTheRealOne()
        {
            var level = new LevelDefinition();

            int first = LevelEdits.AddTower(level, Team.Green, new Vector3(-10.0f, 0.0f, 0.0f));
            int second = LevelEdits.AddTower(level, Team.Green, new Vector3(10.0f, 0.0f, 0.0f));

            Assert.That(level.Towers[first].HoldsTheFlag, Is.True);
            Assert.That(level.Towers[second].HoldsTheFlag, Is.False);
        }

        /// <summary>
        /// The demotion is scoped to the promoted tower's own side rather than sweeping the
        /// map, which is what makes a side able to have exactly one real tower without the
        /// other side losing its flag every time somebody moves theirs.
        /// </summary>
        /// <remarks>
        /// The pairs are dropped first, deliberately. A new map is built in pairs and a
        /// paired tower's promotion <em>does</em> carry across to its opposite number - that
        /// is <see cref="PuttingTheFlagOnAPairedTowerPutsTheOtherFlagOnItsTwin"/>, and it is
        /// what keeps the two flags on towers that are each other's. This is the rule
        /// underneath it, which four towers answering for themselves is the only way to see.
        /// </remarks>
        [Test]
        public void MakingATowerRealDemotesTheOthersOnItsSideAndLeavesTheOtherSideAlone()
        {
            LevelDefinition level = LevelEdits.Starter("Test Map");
            foreach (LevelTower unpaired in level.Towers)
            {
                unpaired.Pair = 0;
            }

            List<LevelTower> green = level.TowersFor(Team.Green);
            List<LevelTower> brown = level.TowersFor(Team.Brown);

            int decoy = System.Array.IndexOf(level.Towers, green[1]);
            Assert.That(LevelEdits.MakeRealTower(level, decoy), Is.True);

            Assert.That(green[0].HoldsTheFlag, Is.False);
            Assert.That(green[1].HoldsTheFlag, Is.True);
            Assert.That(
                brown[0].HoldsTheFlag,
                Is.True,
                "promoting a green tower moved the brown flag");
            Assert.That(LevelValidation.Problems(level), Is.Empty);
        }

        /// <summary>
        /// Placing a second bunker for a side is a <em>move</em> of the one it has, so on a
        /// map built in pairs it moves the other side's too. The bunker tool would otherwise
        /// be the one gesture on the map that quietly breaks a pair - and on a two-bunker map
        /// that is the whole symmetry gone in one click.
        /// </summary>
        [Test]
        public void PlacingASecondBunkerForAPairedSideMovesBothOfThem()
        {
            LevelDefinition level = LevelEdits.Starter("Test Map");
            var green = new EditSelection(EditTarget.Bunker, 0);
            EditSelection brown = LevelEdits.TwinOf(level, green);
            Assert.That(brown.IsSomething, Is.True);

            LevelEdits.AddBunker(level, level.Bunkers[0].Side, new Vector3(20.0f, 0.0f, -30.0f));

            Assert.That(
                level.Bunkers[green.Index].Position,
                Is.EqualTo(new Vector3(20.0f, 0.0f, -30.0f)));
            Assert.That(
                level.Bunkers[brown.Index].Position,
                Is.EqualTo(new Vector3(-20.0f, 0.0f, 30.0f)),
                "the other side's bunker stayed where it was");
        }

        /// <summary>
        /// A side has one bunker, because it is where that side deploys, repairs, refuels and
        /// wins - and two of them would be two of all four.
        /// </summary>
        [Test]
        public void PlacingASecondBunkerForASideMovesTheOneItHas()
        {
            LevelDefinition level = LevelEdits.Starter("Test Map");
            int before = level.Bunkers.Length;

            int placed = LevelEdits.AddBunker(level, Team.Green, new Vector3(20.0f, 0.0f, -30.0f));

            Assert.That(level.Bunkers.Length, Is.EqualTo(before), "the side has two bunkers now");
            Assert.That(level.Bunkers[placed].Side, Is.EqualTo(Team.Green));
            Assert.That(
                level.Bunkers[placed].Position, Is.EqualTo(new Vector3(20.0f, 0.0f, -30.0f)));
        }

        /// <summary>
        /// The most expensive default in the format to get wrong: a bunker's yaw is which way
        /// its lift bay points, so one facing away from the field deploys its vehicles into
        /// its own back wall.
        /// </summary>
        [Test]
        public void ANewBunkerFacesTheMiddleOfTheMap()
        {
            var level = new LevelDefinition();
            int placed = LevelEdits.AddBunker(level, Team.Green, new Vector3(0.0f, 0.0f, -50.0f));

            Vector3 facing = Quaternion.Euler(0.0f, level.Bunkers[placed].YawDegrees, 0.0f)
                * Vector3.forward;

            Assert.That(
                facing.z,
                Is.GreaterThan(0.9f),
                "a bunker at the south end of the map is not pointing north up it");
        }

        [Test]
        public void ARectangleOfLandIsSortedAndASliverIsRefused()
        {
            var level = new LevelDefinition();

            int drawn = LevelEdits.AddLand(
                level, new Vector3(20.0f, 0.0f, 30.0f), new Vector3(-10.0f, 0.0f, -5.0f), "Shore");

            Assert.That(drawn, Is.EqualTo(0));
            Assert.That(level.Land[0].MinX, Is.EqualTo(-10.0f));
            Assert.That(level.Land[0].MaxX, Is.EqualTo(20.0f));
            Assert.That(level.Land[0].MinZ, Is.EqualTo(-5.0f));
            Assert.That(level.Land[0].MaxZ, Is.EqualTo(30.0f));

            Assert.That(
                LevelEdits.AddLand(level, Vector3.zero, new Vector3(0.4f, 0.0f, 40.0f), "Sliver"),
                Is.LessThan(0),
                "a rectangle nobody can select was drawn anyway");
            Assert.That(level.Land.Length, Is.EqualTo(1));
        }

        [Test]
        public void TypingOneEdgePastItsOppositeTurnsTheRectangleInsideOutRatherThanBreakingIt()
        {
            var level = new LevelDefinition();
            LevelEdits.AddLand(level, new Vector3(-10.0f, 0.0f, -10.0f), new Vector3(10.0f, 0.0f, 10.0f), "Shore");

            Assert.That(
                LevelEdits.SetLandEdges(level, 0, 30.0f, 10.0f, -10.0f, 10.0f),
                Is.True);

            Assert.That(level.Land[0].MinX, Is.EqualTo(10.0f));
            Assert.That(level.Land[0].MaxX, Is.EqualTo(30.0f));
            Assert.That(level.Land[0].IsDrawn, Is.True);
        }

        /// <summary>
        /// Mirroring is a rotation about the origin rather than a reflection, which is what
        /// makes both sides' runs the same shape rather than mirror images of each other.
        /// </summary>
        [Test]
        public void MirroringAPropRotatesItHalfATurnAboutTheOrigin()
        {
            LevelDefinition level = LevelEdits.Starter("Test Map");
            int placed = LevelEdits.AddStructure(
                level, StructureKind.BuildingA, new Vector3(12.0f, 0.0f, -30.0f));
            LevelEdits.SetYaw(level, new EditSelection(EditTarget.Structure, placed), 20.0f);

            EditSelection twin = LevelEdits.Mirror(
                level, new EditSelection(EditTarget.Structure, placed));

            Assert.That(twin.IsSomething, Is.True);
            LevelStructure made = level.Structures[twin.Index];
            Assert.That(made.Position, Is.EqualTo(new Vector3(-12.0f, 0.0f, 30.0f)));
            Assert.That(made.YawDegrees, Is.EqualTo(200.0f).Within(0.001f));
            Assert.That(made.Structure, Is.EqualTo(StructureKind.BuildingA));
        }

        /// <summary>
        /// The thing at the opposite end of a symmetrical map is the <em>other</em> side's
        /// version of it, so mirroring green's real tower produces brown's real tower.
        /// </summary>
        [Test]
        public void MirroringATowerGivesItToTheOtherSideAndKeepsItReal()
        {
            var level = new LevelDefinition();
            int green = LevelEdits.AddTower(level, Team.Green, new Vector3(-18.0f, 0.0f, -56.0f));

            EditSelection twin = LevelEdits.Mirror(level, new EditSelection(EditTarget.Tower, green));

            Assert.That(twin.IsSomething, Is.True);
            LevelTower made = level.Towers[twin.Index];
            Assert.That(made.Side, Is.EqualTo(Team.Brown));
            Assert.That(made.HoldsTheFlag, Is.True);
            Assert.That(made.Position, Is.EqualTo(new Vector3(18.0f, 0.0f, 56.0f)));
            Assert.That(level.Towers[green].HoldsTheFlag, Is.True, "the green flag moved");
        }

        /// <summary>
        /// <see cref="LevelEdits.AddTower"/> auto-promotes a side's very first tower to real,
        /// which is right when mirroring the real one and wrong when mirroring a decoy onto a
        /// side that has none yet - the mirrored tower has to end up a decoy either way,
        /// because that heuristic has no idea which kind of tower it was asked to place.
        /// </summary>
        [Test]
        public void MirroringADecoyOntoASideWithNoTowersYetLeavesItADecoy()
        {
            var level = new LevelDefinition();
            int decoy = LevelEdits.AddTower(level, Team.Green, new Vector3(18.0f, 0.0f, -56.0f));
            level.Towers[decoy].HoldsTheFlag = false;

            EditSelection twin = LevelEdits.Mirror(level, new EditSelection(EditTarget.Tower, decoy));

            Assert.That(twin.IsSomething, Is.True);
            Assert.That(
                level.Towers[twin.Index].HoldsTheFlag,
                Is.False,
                "mirroring a decoy onto an empty side put the flag on it anyway");
        }

        [Test]
        public void MirroringABunkerGivesTheOtherSideOneFacingBack()
        {
            var level = new LevelDefinition();
            int green = LevelEdits.AddBunker(level, Team.Green, new Vector3(0.0f, 0.0f, -70.0f));
            level.Bunkers[green].YawDegrees = 0.0f;

            EditSelection twin = LevelEdits.Mirror(level, new EditSelection(EditTarget.Bunker, green));

            Assert.That(twin.IsSomething, Is.True);
            LevelBunker made = level.Bunkers[twin.Index];
            Assert.That(made.Side, Is.EqualTo(Team.Brown));
            Assert.That(made.Position, Is.EqualTo(new Vector3(0.0f, 0.0f, 70.0f)));
            Assert.That(made.YawDegrees, Is.EqualTo(180.0f).Within(0.001f));
        }

        /// <summary>
        /// A whole map built by mirroring one half of it passes every rule, which is the
        /// claim the button is making.
        /// </summary>
        [Test]
        public void AMapBuiltByMirroringOneHalfIsPlayable()
        {
            var level = new LevelDefinition
            {
                Name = "Mirrored",
                Bounds = new LevelBounds(),
            };

            LevelEdits.AddLand(
                level, new Vector3(-60.0f, 0.0f, -60.0f), new Vector3(60.0f, 0.0f, 60.0f), "Island");
            int bunker = LevelEdits.AddBunker(level, Team.Green, new Vector3(0.0f, 0.0f, -44.0f));
            int real = LevelEdits.AddTower(level, Team.Green, new Vector3(-18.0f, 0.0f, -30.0f));
            int decoy = LevelEdits.AddTower(level, Team.Green, new Vector3(18.0f, 0.0f, -30.0f));
            int fuel = LevelEdits.AddStructure(
                level, StructureKind.DepotFuel, new Vector3(-34.0f, 0.0f, 0.0f));
            int ammo = LevelEdits.AddStructure(
                level, StructureKind.DepotAmmo, new Vector3(34.0f, 0.0f, 0.0f));

            LevelEdits.Mirror(level, new EditSelection(EditTarget.Bunker, bunker));
            LevelEdits.Mirror(level, new EditSelection(EditTarget.Tower, real));
            LevelEdits.Mirror(level, new EditSelection(EditTarget.Tower, decoy));
            LevelEdits.Mirror(level, new EditSelection(EditTarget.Structure, fuel));
            LevelEdits.Mirror(level, new EditSelection(EditTarget.Structure, ammo));

            Assert.That(LevelValidation.Problems(level), Is.Empty);
        }

        [Test]
        public void RemovingSomethingTakesOutTheOneThatWasNamed()
        {
            LevelDefinition level = LevelEdits.Starter("Test Map");
            LevelEdits.AddStructure(level, StructureKind.Tree, new Vector3(1.0f, 0.0f, 1.0f));
            int second = LevelEdits.AddStructure(
                level, StructureKind.BuildingA, new Vector3(2.0f, 0.0f, 2.0f));
            LevelEdits.AddStructure(level, StructureKind.Tree, new Vector3(3.0f, 0.0f, 3.0f));

            Assert.That(
                LevelEdits.Remove(level, new EditSelection(EditTarget.Structure, second)), Is.True);

            foreach (LevelStructure structure in level.Structures)
            {
                Assert.That(structure.Structure, Is.Not.EqualTo(StructureKind.BuildingA));
            }
        }

        [Test]
        public void ASelectionStopsHoldingOnceWhatItNamedIsGone()
        {
            LevelDefinition level = LevelEdits.Starter("Test Map");
            var last = new EditSelection(EditTarget.Structure, level.Structures.Length - 1);

            Assert.That(LevelEdits.Holds(level, last), Is.True);
            LevelEdits.Remove(level, last);
            Assert.That(LevelEdits.Holds(level, last), Is.False);
        }

        [Test]
        public void ATowerIsNeverOfferedAsAPropToScatter()
        {
            Assert.That(
                LevelEdits.Palette(),
                Has.No.Member(StructureKind.FlagTower),
                "a tower placed as scenery is a fourth pyramid belonging to nobody");
            Assert.That(LevelEdits.Palette(), Has.No.Member(StructureKind.None));
        }

        [Test]
        public void SnappingRoundsOntoTheGridAndLeavesHeightAlone()
        {
            Assert.That(LevelEdits.Snap(7.4f, 2.0f), Is.EqualTo(8.0f));
            Assert.That(LevelEdits.Snap(7.4f, 0.0f), Is.EqualTo(7.4f));

            Vector3 snapped = LevelEdits.Snap(new Vector3(1.4f, -1.2f, -2.6f), 1.0f);
            Assert.That(snapped, Is.EqualTo(new Vector3(1.0f, -1.2f, -3.0f)));
        }

        /// <summary>
        /// A level name becomes a path, so a name with a slash in it is a write that fails -
        /// or worse, one that succeeds somewhere else.
        /// </summary>
        [Test]
        public void ATypedFileNameIsTidiedIntoSomethingThatCanBeAFile()
        {
            Assert.That(LevelEditorSession.Tidy("Iron Channel"), Is.EqualTo("iron-channel"));
            Assert.That(LevelEditorSession.Tidy("  My/Map: 2  "), Is.EqualTo("my-map-2"));
            Assert.That(LevelEditorSession.Tidy("***"), Is.Empty);
            Assert.That(LevelEditorSession.Tidy(null), Is.Empty);
        }

        /// <summary>
        /// A prop standing on a shore is picked before the shore under it, or nothing on a map
        /// could be selected without first moving the ground.
        /// </summary>
        [Test]
        public void WhatIsStandingOnTheGroundIsPickedBeforeTheGround()
        {
            LevelDefinition level = LevelEdits.Starter("Test Map");
            int tree = LevelEdits.AddStructure(
                level, StructureKind.Tree, new Vector3(4.0f, 0.0f, 4.0f));

            EditSelection picked = LevelPick.At(level, new Vector3(4.2f, 0.0f, 4.1f));

            Assert.That(picked.Target, Is.EqualTo(EditTarget.Structure));
            Assert.That(picked.Index, Is.EqualTo(tree));
        }

        [Test]
        public void EmptySeaPicksNothing()
        {
            LevelDefinition level = LevelEdits.Starter("Test Map");
            float far = level.Bounds.HalfExtent - 1.0f;

            Assert.That(
                LevelPick.At(level, new Vector3(far, 0.0f, far)),
                Is.EqualTo(EditSelection.Nothing));
        }

        /// <summary>
        /// The build mode cannot find the ground at all, and that is the whole fix for the
        /// editor's worst gesture: on a finished map the island covers every point worth
        /// building on, so an unfiltered pick answered "the shore" to every click meant to
        /// place something - and the click became a drag of the coastline.
        /// </summary>
        /// <remarks>
        /// A filter rather than a re-ordering, and the difference is the point. Props already
        /// beat land and it did not help, because an order only settles which of two things
        /// wins a click both of them are under: the loser is unreachable everywhere the
        /// winner is, and here the loser was the thing being placed.
        /// </remarks>
        [Test]
        public void TheBuildModeCannotFindTheGround()
        {
            var level = new LevelDefinition { Bounds = new LevelBounds() };
            LevelEdits.AddLand(
                level, new Vector3(-40.0f, 0.0f, -40.0f), new Vector3(40.0f, 0.0f, 40.0f), "Shore");
            int tree = LevelEdits.AddStructure(
                level, StructureKind.Tree, new Vector3(4.0f, 0.0f, 4.0f));

            var bare = new Vector3(20.0f, 0.0f, 20.0f);
            Assert.That(
                LevelPick.At(level, bare).Target,
                Is.EqualTo(EditTarget.Land),
                "the shore is not where this test thinks it is");
            Assert.That(
                LevelPick.At(level, bare, EditorMode.Build),
                Is.EqualTo(EditSelection.Nothing),
                "a click on bare shore found the shore, so it would have dragged it");

            Assert.That(
                LevelPick.At(level, new Vector3(4.2f, 0.0f, 4.1f), EditorMode.Build),
                Is.EqualTo(new EditSelection(EditTarget.Structure, tree)),
                "the build mode stopped finding the things it is for");
        }

        /// <summary>
        /// The land mode cannot find what is standing on the ground, so a coastline can be
        /// dragged out from under a row of trees without picking one of them up on the way.
        /// </summary>
        [Test]
        public void TheLandModeCannotFindWhatIsStandingOnIt()
        {
            var level = new LevelDefinition { Bounds = new LevelBounds() };
            int shore = LevelEdits.AddLand(
                level, new Vector3(-40.0f, 0.0f, -40.0f), new Vector3(40.0f, 0.0f, 40.0f), "Shore");
            LevelEdits.AddStructure(level, StructureKind.Tree, new Vector3(4.0f, 0.0f, 4.0f));

            var under = new Vector3(4.2f, 0.0f, 4.1f);
            Assert.That(
                LevelPick.At(level, under).Target,
                Is.EqualTo(EditTarget.Structure),
                "the tree is not where this test thinks it is");

            Assert.That(
                LevelPick.At(level, under, EditorMode.Land),
                Is.EqualTo(new EditSelection(EditTarget.Land, shore)),
                "the tree was picked up while shaping land");
        }

        /// <summary>
        /// Every tool belongs to exactly one mode, and every mode opens on a tool of its own.
        /// Those two facts together are what let the mode be read off the tool in hand rather
        /// than stored in a second field that can come to disagree with the first.
        /// </summary>
        [Test]
        public void EveryToolHasAModeAndEveryModeOpensOnOneOfItsOwnTools()
        {
            foreach (EditTool tool in System.Enum.GetValues(typeof(EditTool)))
            {
                Assert.That(
                    EditorModes.Of(tool) == EditorMode.None,
                    Is.EqualTo(tool == EditTool.None),
                    $"{tool} belongs to no mode, or the empty tool was given one");
            }

            foreach (EditorMode mode in System.Enum.GetValues(typeof(EditorMode)))
            {
                if (mode == EditorMode.None)
                {
                    continue;
                }

                Assert.That(
                    EditorModes.Of(EditorModes.DefaultToolOf(mode)),
                    Is.EqualTo(mode),
                    $"the {mode} mode opens on a tool that belongs to the other one");
                Assert.That(
                    EditorModes.Other(mode),
                    Is.Not.EqualTo(mode),
                    $"switching away from {mode} arrives back at it");
            }
        }

        /// <summary>
        /// A mode reaches only its own half of the map, and in both directions - which is what
        /// stops Delete taking out a coastline while turrets are being placed.
        /// </summary>
        [Test]
        public void AModeReachesOnlyItsOwnHalfOfTheMap()
        {
            Assert.That(EditorModes.Reaches(EditorMode.Land, EditTarget.Land), Is.True);
            Assert.That(EditorModes.Reaches(EditorMode.Land, EditTarget.Structure), Is.False);
            Assert.That(EditorModes.Reaches(EditorMode.Land, EditTarget.Tower), Is.False);
            Assert.That(EditorModes.Reaches(EditorMode.Land, EditTarget.Bunker), Is.False);

            Assert.That(EditorModes.Reaches(EditorMode.Build, EditTarget.Land), Is.False);
            Assert.That(EditorModes.Reaches(EditorMode.Build, EditTarget.Structure), Is.True);
            Assert.That(EditorModes.Reaches(EditorMode.Build, EditTarget.Tower), Is.True);
            Assert.That(EditorModes.Reaches(EditorMode.Build, EditTarget.Bunker), Is.True);

            // Nothing selected is legal in either, and is the state a mode change leaves
            // behind when it lets go of a selection the new mode cannot hold.
            Assert.That(EditorModes.Reaches(EditorMode.Land, EditTarget.None), Is.True);
            Assert.That(EditorModes.Reaches(EditorMode.Build, EditTarget.None), Is.True);
        }

        /// <summary>
        /// Rectangles overlap on purpose - a headland is a small one laid over a long shore -
        /// so the small one on top is what a click on it means.
        /// </summary>
        /// <remarks>
        /// The headland is added <em>before</em> the shore here, deliberately the reverse of
        /// how a map is normally drawn. A regression that dropped the real area comparison in
        /// <see cref="LevelPick.LandAt"/> and simply kept the last rectangle in array order
        /// that contained the point would, on a headland added second, still happen to answer
        /// correctly by coincidence - array order and area order would agree. Reversing them
        /// makes the two disagree, so only the actual "smallest area wins" logic passes.
        /// </remarks>
        [Test]
        public void TheSmallestRectangleCoveringAPointIsTheOnePickedRegardlessOfDrawOrder()
        {
            var level = new LevelDefinition { Bounds = new LevelBounds() };
            int headland = LevelEdits.AddLand(
                level, new Vector3(-8.0f, 0.0f, -8.0f), new Vector3(8.0f, 0.0f, 8.0f), "Headland");
            LevelEdits.AddLand(
                level, new Vector3(-80.0f, 0.0f, -80.0f), new Vector3(80.0f, 0.0f, 80.0f), "Shore");

            EditSelection picked = LevelPick.At(level, new Vector3(1.0f, 0.0f, 1.0f));

            Assert.That(picked.Target, Is.EqualTo(EditTarget.Land));
            Assert.That(
                picked.Index,
                Is.EqualTo(headland),
                "the last-drawn (larger) rectangle won instead of the smallest one");
        }

        [Test]
        public void ACornerOfARectangleIsGrabbedFromOutsideItAsWellAsInside()
        {
            var level = new LevelDefinition { Bounds = new LevelBounds() };
            LevelEdits.AddLand(
                level, new Vector3(-20.0f, 0.0f, -20.0f), new Vector3(20.0f, 0.0f, 20.0f), "Shore");

            Assert.That(
                LevelPick.GripOn(level, 0, new Vector3(-21.0f, 0.0f, -21.0f), 3.0f),
                Is.EqualTo(LandGrip.SouthWest));
            Assert.That(
                LevelPick.GripOn(level, 0, new Vector3(19.5f, 0.0f, 19.5f), 3.0f),
                Is.EqualTo(LandGrip.NorthEast));
            Assert.That(
                LevelPick.GripOn(level, 0, Vector3.zero, 3.0f),
                Is.EqualTo(LandGrip.Body));
            Assert.That(
                LevelPick.GripOn(level, 0, new Vector3(60.0f, 0.0f, 0.0f), 3.0f),
                Is.EqualTo(LandGrip.None));
        }

        [Test]
        public void UndoAndRedoWalkBackAndForwardThroughWholeMaps()
        {
            var history = new EditHistory();
            LevelDefinition level = LevelEdits.Starter("Test Map");
            history.Reset(level);

            Assert.That(history.CanUndo, Is.False);
            Assert.That(history.CanRedo, Is.False);

            LevelEdits.AddStructure(level, StructureKind.Tree, Vector3.zero);
            Assert.That(history.Record(level), Is.True);
            Assert.That(history.CanUndo, Is.True);

            LevelDefinition back = history.Undo();
            Assert.That(back, Is.Not.Null);
            Assert.That(back.Structures.Length, Is.EqualTo(level.Structures.Length - 1));

            LevelDefinition forward = history.Redo();
            Assert.That(forward, Is.Not.Null);
            Assert.That(forward.Structures.Length, Is.EqualTo(level.Structures.Length));
        }

        /// <summary>
        /// A drag that ends where it began is not an edit, and an undo stack full of them is
        /// one where undo appears not to work.
        /// </summary>
        [Test]
        public void RecordingAMapThatHasNotChangedDoesNotCostAnUndoStep()
        {
            var history = new EditHistory();
            LevelDefinition level = LevelEdits.Starter("Test Map");
            history.Reset(level);

            Assert.That(history.Record(level), Is.False);
            Assert.That(history.CanUndo, Is.False);
            Assert.That(history.Count, Is.EqualTo(1));
        }

        [Test]
        public void MakingAChangeAfterUndoingThrowsAwayTheFutureThatDidNotHappen()
        {
            var history = new EditHistory();
            LevelDefinition level = LevelEdits.Starter("Test Map");
            history.Reset(level);

            LevelEdits.AddStructure(level, StructureKind.Tree, Vector3.zero);
            history.Record(level);

            LevelDefinition back = history.Undo();
            Assert.That(history.CanRedo, Is.True);

            LevelEdits.AddStructure(back, StructureKind.BuildingA, Vector3.zero);
            history.Record(back);

            Assert.That(history.CanRedo, Is.False);
        }

        [Test]
        public void TheHistoryStopsGrowingAtItsDepth()
        {
            var history = new EditHistory();
            LevelDefinition level = LevelEdits.Starter("Test Map");
            history.Reset(level);

            for (int step = 0; step < EditHistory.Depth * 2; step++)
            {
                LevelEdits.AddStructure(
                    level, StructureKind.Tree, new Vector3(step, 0.0f, 0.0f));
                history.Record(level);
            }

            Assert.That(history.Count, Is.EqualTo(EditHistory.Depth));
        }

        /// <summary>
        /// What the editor writes has to be readable by the game, which reads a level file the
        /// only way there is: through <see cref="LevelFile"/>.
        /// </summary>
        [Test]
        public void AMapMadeInTheEditorSurvivesBeingWrittenAndReadBack()
        {
            LevelDefinition made = LevelEdits.Starter("Round Trip");
            LevelEdits.AddStructure(made, StructureKind.Bridge, new Vector3(3.0f, 0.0f, 0.0f));

            Assert.That(
                LevelFile.TryParse(
                    LevelFile.ToJson(made), "test", out LevelDefinition read, out string problem),
                Is.True,
                problem);

            Assert.That(read.Name, Is.EqualTo(made.Name));
            Assert.That(read.Structures.Length, Is.EqualTo(made.Structures.Length));
            Assert.That(
                LevelFile.ToJson(read),
                Is.EqualTo(LevelFile.ToJson(made)),
                "a map made in the editor does not come back off disk the same map");
        }

        /// <summary>
        /// The inspector has room for every row the map itself wants.
        /// </summary>
        /// <remarks>
        /// The panel with nothing selected is the longest one there is now: a title, a
        /// description, three numbers about the world and one per vehicle in the reserve.
        /// A row past the end is dropped in silence rather than reported - see
        /// <see cref="EditorInspector.RowCount"/> - so a fifth vehicle would take the
        /// helicopter count off the panel and nothing anywhere would say so.
        /// </remarks>
        [Test]
        public void TheInspectorHasRoomForEveryRowTheMapWants()
        {
            int wanted = 5 + VehicleRoster.Kinds.Length;

            Assert.That(
                EditorInspector.RowCount,
                Is.GreaterThanOrEqualTo(wanted),
                "the map panel is longer than the inspector can show");
        }

        /// <summary>
        /// <c>float.TryParse</c> accepts the literal strings "NaN" and "Infinity" as valid
        /// numbers regardless of the style flags passed to it, and neither survives a clamp:
        /// <c>Mathf.Max</c> compares with <c>&gt;</c>, and every comparison against NaN is
        /// false, so a NaN slips past every safety check in the inspector's setters and every
        /// guard in <see cref="LevelValidation"/> - which is what let a NaN half-extent be
        /// reported as a fully playable map and get written straight into the level file.
        /// </summary>
        [Test]
        public void NumberParsingRejectsNaNAndInfinity()
        {
            Assert.That(EditorInspector.TryParseNumber("12.5", out float ok), Is.True);
            Assert.That(ok, Is.EqualTo(12.5f).Within(0.0001f));

            Assert.That(EditorInspector.TryParseNumber("NaN", out _), Is.False);
            Assert.That(EditorInspector.TryParseNumber("Infinity", out _), Is.False);
            Assert.That(EditorInspector.TryParseNumber("-Infinity", out _), Is.False);
            Assert.That(EditorInspector.TryParseNumber("not a number", out _), Is.False);
            Assert.That(EditorInspector.TryParseNumber(string.Empty, out _), Is.False);
        }

        /// <summary>
        /// The pan/zoom/frame limit has to move when the world does, or a bigger half-extent
        /// typed into the inspector leaves the newly available part of the map behind a pan
        /// limit still sized for the old, smaller one.
        /// </summary>
        [Test]
        public void SettingTheWorldExtentMovesThePanLimit()
        {
            var host = new GameObject("Test Camera", typeof(Camera), typeof(EditorCameraRig));

            try
            {
                var rig = host.GetComponent<EditorCameraRig>();
                rig.Configure(50.0f);

                rig.LookAt(new Vector3(1000.0f, 0.0f, 0.0f));
                float clampedSmall = rig.Focus.x;
                Assert.That(clampedSmall, Is.LessThan(500.0f), "a 50 m world was not clamping at all");

                rig.SetWorldExtent(2000.0f);
                rig.LookAt(new Vector3(1000.0f, 0.0f, 0.0f));

                Assert.That(
                    rig.Focus.x,
                    Is.EqualTo(1000.0f).Within(0.01f),
                    "the pan limit did not grow with the world");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TheHandoffForgetsEverythingWhenItIsCleared()
        {
            LevelHandoff.Playtest("some-map");
            Assert.That(LevelHandoff.Level, Is.EqualTo("some-map"));
            Assert.That(LevelHandoff.FromEditor, Is.True);
            Assert.That(LevelHandoff.LevelOr("other"), Is.EqualTo("some-map"));

            LevelHandoff.Edit("some-map");
            Assert.That(
                LevelHandoff.FromEditor,
                Is.False,
                "going back to the editor left a way back to a playtest that has ended");

            LevelHandoff.Clear();
            Assert.That(LevelHandoff.IsAsked, Is.False);
            Assert.That(LevelHandoff.LevelOr("other"), Is.EqualTo("other"));
        }

        [TearDown]
        public void ForgetAnyHandoff() => LevelHandoff.Clear();
    }
}
