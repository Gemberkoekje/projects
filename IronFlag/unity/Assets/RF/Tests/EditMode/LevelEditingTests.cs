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

        [Test]
        public void MakingATowerRealDemotesTheOthersOnItsSideAndLeavesTheOtherSideAlone()
        {
            LevelDefinition level = LevelEdits.Starter("Test Map");
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
