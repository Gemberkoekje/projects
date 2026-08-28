using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using IronFlag.Audio;
using IronFlag.Combat;
using IronFlag.Core;
using IronFlag.Destruction;
using IronFlag.Editor.Gameplay;
using IronFlag.Levels;
using IronFlag.Objective;
using IronFlag.Players;
using IronFlag.Supply;
using IronFlag.UI;
using IronFlag.Vehicles;

namespace IronFlag.Tests.EditMode
{
    /// <summary>
    /// Checks that the generated sandbox is a two-player game rather than a scene full of
    /// objects that happen to compile.
    /// </summary>
    /// <remarks>
    /// The scene is generated, so nothing here is defending it against being edited by hand
    /// - it is defending against the generator quietly producing something unplayable, which
    /// is what a missing prefab or a camera that never got its viewport looks like. The
    /// controls asset has its own tests.
    /// </remarks>
    public sealed class SandboxWiringTests
    {
        [Test]
        public void BothPlayersHaveACameraAndAFullRoster()
        {
            using (var sandbox = new OpenSandbox())
            {
                List<PlayerVehicleDriver> drivers = sandbox.All<PlayerVehicleDriver>();

                Assert.That(
                    drivers.Count,
                    Is.EqualTo(VehicleSandboxScene.PlayerCount),
                    "the sandbox does not seat two players");

                foreach (PlayerVehicleDriver driver in drivers)
                {
                    Assert.That(driver.CameraRig, Is.Not.Null, $"{driver.name} has no camera");
                    Assert.That(driver.Roster.Count, Is.EqualTo(4), $"{driver.name} is short of vehicles");
                    Assert.That(
                        driver.Roster[0].Kind,
                        Is.EqualTo(VehicleKind.Jeep),
                        $"{driver.name}'s roster does not start on the jeep");
                }

                Assert.That(
                    sandbox.All<VehicleController>().Count,
                    Is.EqualTo(4 * VehicleSandboxScene.PlayerCount),
                    "vehicles are missing");
            }
        }

        [Test]
        public void TheTwoPlayersDriveDifferentVehicles()
        {
            using (var sandbox = new OpenSandbox())
            {
                List<PlayerVehicleDriver> drivers = sandbox.All<PlayerVehicleDriver>();
                var claimed = new HashSet<VehicleController>();

                foreach (PlayerVehicleDriver driver in drivers)
                {
                    foreach (VehicleController vehicle in driver.Roster)
                    {
                        Assert.That(
                            claimed.Add(vehicle),
                            Is.True,
                            $"{vehicle.name} is in more than one player's roster");
                    }
                }
            }
        }

        [Test]
        public void EachPlayerIsOnTheirOwnSide()
        {
            using (var sandbox = new OpenSandbox())
            {
                var sides = new HashSet<Team>();

                foreach (PlayerVehicleDriver driver in sandbox.All<PlayerVehicleDriver>())
                {
                    var side = Team.None;
                    foreach (VehicleController vehicle in driver.Roster)
                    {
                        var paint = vehicle.GetComponent<VehicleTeamPaint>();
                        Assert.That(paint, Is.Not.Null, $"{vehicle.name} has no team paint");
                        Assert.That(paint.Team, Is.Not.EqualTo(Team.None), vehicle.name);

                        if (side == Team.None)
                        {
                            side = paint.Team;
                        }

                        Assert.That(paint.Team, Is.EqualTo(side), $"{driver.name} is on two sides");
                    }

                    Assert.That(sides.Add(side), Is.True, "both players are on the same side");
                }
            }
        }

        [Test]
        public void TheScreenIsSplitBetweenTheTwoCameras()
        {
            using (var sandbox = new OpenSandbox())
            {
                var viewports = new List<Rect>();
                foreach (TopDownCameraRig rig in sandbox.All<TopDownCameraRig>())
                {
                    viewports.Add(rig.Viewport);
                }

                Assert.That(viewports.Count, Is.EqualTo(VehicleSandboxScene.PlayerCount));
                Assert.That(
                    viewports,
                    Has.Member(SplitScreenLayout.ViewportFor(0, VehicleSandboxScene.PlayerCount)),
                    "nobody has the upper half");
                Assert.That(
                    viewports,
                    Has.Member(SplitScreenLayout.ViewportFor(1, VehicleSandboxScene.PlayerCount)),
                    "nobody has the lower half");
            }
        }

        [Test]
        public void ThereIsOneSessionHoldingBothPlayers()
        {
            using (var sandbox = new OpenSandbox())
            {
                List<LocalMultiplayer> sessions = sandbox.All<LocalMultiplayer>();

                Assert.That(sessions.Count, Is.EqualTo(1), "there is not exactly one session");
                Assert.That(
                    sessions[0].Players.Count,
                    Is.EqualTo(VehicleSandboxScene.PlayerCount),
                    "the session is not holding both players");

                foreach (PlayerVehicleDriver player in sessions[0].Players)
                {
                    Assert.That(player, Is.Not.Null, "the session is holding a missing player");
                }
            }
        }

        [Test]
        public void ThereIsExactlyOnePairOfEars()
        {
            using (var sandbox = new OpenSandbox())
            {
                Assert.That(
                    sandbox.All<AudioListener>().Count,
                    Is.EqualTo(1),
                    "a split screen still only has one set of speakers");
            }
        }

        /// <summary>
        /// The scene can make a noise, and making one did not cost it its single pair of
        /// ears. A director is not a listener - it is the thing holding the clips - and the
        /// distinction is the whole reason adding sound to a split-screen game did not need
        /// a second listener. See <c>AudioMixdown</c>.
        /// </summary>
        [Test]
        public void TheMatchHasSomethingToMakeANoiseWith()
        {
            using (var sandbox = new OpenSandbox())
            {
                Assert.That(
                    sandbox.All<AudioDirector>().Count,
                    Is.EqualTo(1),
                    "there is not exactly one thing in the scene that plays sounds");

                Assert.That(
                    sandbox.All<MusicPlayer>().Count,
                    Is.EqualTo(1),
                    "there is not exactly one thing in the scene that plays music");

                Assert.That(
                    sandbox.All<MatchMusic>().Count,
                    Is.EqualTo(1),
                    "nothing is deciding what the match sounds like");

                foreach (AudioDirector director in sandbox.All<AudioDirector>())
                {
                    Assert.That(
                        director.Catalog,
                        Is.Not.Null,
                        "the director has no catalog, so the match would be silent");

                    Assert.That(director.Catalog.Problems(), Is.Empty);
                }
            }
        }

        /// <summary>
        /// Every vehicle on the field has an engine to be heard running, and the helicopter's
        /// is the one that is loud standing still - which is the only difference between a
        /// rotor and an engine in this game.
        /// </summary>
        [Test]
        public void EveryVehicleHasAnEngineToBeHeardRunning()
        {
            using (var sandbox = new OpenSandbox())
            {
                foreach (VehicleController vehicle in sandbox.All<VehicleController>())
                {
                    var engine = vehicle.GetComponent<EngineAudio>();

                    Assert.That(engine, Is.Not.Null, $"the {vehicle.Kind} drives in silence");
                    Assert.That(
                        engine.Loop,
                        Is.Not.Null,
                        $"the {vehicle.Kind}'s engine has nothing to play through");

                    bool rotor = vehicle.Kind == VehicleKind.Helicopter;
                    Assert.That(
                        engine.IdleShare,
                        rotor ? Is.GreaterThan(0.5f) : Is.LessThan(0.5f),
                        $"the {vehicle.Kind} idles at the wrong level for what it is");
                }
            }
        }

        [Test]
        public void EachSideHasABunkerToComeBackTo()
        {
            using (var sandbox = new OpenSandbox())
            {
                List<TeamBunker> bunkers = sandbox.All<TeamBunker>();

                Assert.That(
                    bunkers.Count,
                    Is.EqualTo(VehicleSandboxScene.PlayerCount),
                    "the sandbox is short of bunkers");

                var sides = new HashSet<Team>();
                foreach (TeamBunker bunker in bunkers)
                {
                    Assert.That(bunker.Team, Is.Not.EqualTo(Team.None), $"{bunker.name} is on no side");
                    Assert.That(sides.Add(bunker.Team), Is.True, $"two bunkers for {bunker.Team}");
                }
            }
        }

        /// <summary>
        /// A lift behind the bunker would deploy a vehicle into the back wall, and one on
        /// the wrong half of the map would deploy it into the enemy start line.
        /// </summary>
        [Test]
        public void EveryBunkerDeploysOntoItsOwnHalfOfTheMap()
        {
            using (var sandbox = new OpenSandbox())
            {
                foreach (TeamBunker bunker in sandbox.All<TeamBunker>())
                {
                    float home = bunker.transform.position.z;

                    Assert.That(
                        bunker.IsModelled,
                        Is.True,
                        $"{bunker.name} has no lift or pad; rebuild the bunker in Blender");

                    foreach (VehicleKind kind in VehiclePrefabBuilder.Roster())
                    {
                        Vector3 at = bunker.DeployPointFor(kind);

                        Assert.That(
                            Mathf.Abs(at.z),
                            Is.LessThanOrEqualTo(Mathf.Abs(home) + 6.0f),
                            $"{bunker.name} deploys the {kind} well behind itself");
                        Assert.That(
                            Mathf.Sign(at.z),
                            Is.EqualTo(Mathf.Sign(home)),
                            $"{bunker.name} deploys the {kind} on the wrong side of the map");
                    }

                    Assert.That(
                        bunker.HelipadPoint.y,
                        Is.GreaterThan(2.0f),
                        $"{bunker.name} launches the helicopter from the ground");
                }
            }
        }

        /// <summary>
        /// Every bunker has a hall under it with a bay per roster slot, a lamp in each, and a
        /// car in its shaft.
        /// </summary>
        /// <remarks>
        /// All of it is found in the model by name, so a rename in Blender or a missing
        /// rebuild has one symptom: a vehicle waiting at the top of the shaft instead of in a
        /// room, in a picture with nothing in it. Nothing throws and nothing warns twice.
        /// </remarks>
        [Test]
        public void EveryBunkerHasAHallWithABayPerVehicle()
        {
            using (var sandbox = new OpenSandbox())
            {
                List<TeamBunker> bunkers = sandbox.All<TeamBunker>();
                Assert.That(bunkers, Is.Not.Empty, "the sandbox has no bunkers at all");

                foreach (TeamBunker bunker in bunkers)
                {
                    Assert.That(
                        bunker.HasHall,
                        Is.True,
                        $"{bunker.name} has nothing under it; rebuild the level catalog");
                    Assert.That(
                        bunker.BayCount,
                        Is.EqualTo(VehicleRoster.Kinds.Length),
                        $"{bunker.name} is short of bays; rebuild the bunker hall in Blender");
                    Assert.That(
                        bunker.Car,
                        Is.Not.Null,
                        $"{bunker.name} has no lift car, so vehicles would surface standing on air");

                    for (int slot = 0; slot < bunker.BayCount; slot++)
                    {
                        Transform deck = bunker.BayNode(slot);
                        Assert.That(deck, Is.Not.Null, $"{bunker.name} has no bay {slot}");
                        Assert.That(
                            deck.Find($"{TeamBunker.LampNodePrefix}{slot}"),
                            Is.Not.Null,
                            $"{bunker.name} bay {slot} has no lamp, so nothing in it is lit");
                        Assert.That(
                            deck.GetComponentInChildren<Light>(true),
                            Is.Not.Null,
                            $"{bunker.name} bay {slot} throws no light");
                    }
                }
            }
        }

        /// <summary>
        /// The hall is scenery: it has no colliders, it is switched off, and it is entirely
        /// under both the ground and the sea.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The colliders are the trap. Every other model this project places gets a
        /// <c>MeshCollider</c> per mesh, and doing that here would buy several hundred
        /// triangles of physics per bunker for a room nothing can reach - it is below the
        /// island's own collider, and every round in this game resolves on a plane that
        /// starts at ground level.
        /// </para>
        /// <para>
        /// The sea is the other one, and it is the reason the hall hangs as deep as it does.
        /// A level's sea is a box as wide as the whole map, drawn under the island as well as
        /// around it, so a hall that reached up into it would have a sheet of water drawn
        /// across the middle of the select view.
        /// </para>
        /// </remarks>
        [Test]
        public void TheHallIsSceneryUnderTheGroundAndUnderTheSea()
        {
            using (var sandbox = new OpenSandbox())
            {
                float seabed = float.MaxValue;
                foreach (WaterLine water in sandbox.All<WaterLine>())
                {
                    Bounds slab = water.GetComponent<Renderer>().bounds;
                    seabed = Mathf.Min(seabed, slab.min.y);
                }

                foreach (TeamBunker bunker in sandbox.All<TeamBunker>())
                {
                    GameObject hall = bunker.Hall;
                    Assert.That(hall, Is.Not.Null, $"{bunker.name} has no hall");
                    Assert.That(
                        hall.activeSelf,
                        Is.False,
                        $"{bunker.name} leaves its hall drawn for everybody on the map");
                    Assert.That(
                        hall.GetComponentsInChildren<Collider>(true),
                        Is.Empty,
                        $"{bunker.name} put physics on a room nothing can reach");
                    Assert.That(
                        bunker.SkylineHeight,
                        Is.LessThan(seabed),
                        $"{bunker.name}'s hall reaches into the sea slab");

                    foreach (Renderer piece in hall.GetComponentsInChildren<Renderer>(true))
                    {
                        Assert.That(
                            piece.bounds.max.y,
                            Is.LessThan(0.0f),
                            $"{piece.name} pokes up through the ground");
                    }
                }
            }
        }

        /// <summary>
        /// The bays are in roster order, two columns of two, with the heavy pair upstairs -
        /// and roster order reads left to right in the picture.
        /// </summary>
        /// <remarks>
        /// The reading direction is the part worth asserting, and it is the part that is easy
        /// to get backwards: glTFast negates x on import, so the model's +x is the game's -x,
        /// and the select camera looks back along the bunker's heading, so <em>its</em> right
        /// is the bunker's -x. The two flips do not cancel. Get it wrong and the console
        /// strip reads jeep-to-helicopter while the bays read helicopter-to-jeep.
        /// </remarks>
        [Test]
        public void TheBaysReadInRosterOrderFromTheLeftOfThePicture()
        {
            using (var sandbox = new OpenSandbox())
            {
                foreach (TeamBunker bunker in sandbox.All<TeamBunker>())
                {
                    if (!bunker.HasHall)
                    {
                        continue;
                    }

                    var across = new List<float>();
                    var up = new List<float>();
                    for (int slot = 0; slot < bunker.BayCount; slot++)
                    {
                        Vector3 local = bunker.transform.InverseTransformPoint(bunker.BayFor(slot));
                        across.Add(local.x);
                        up.Add(local.y);
                    }

                    // The select camera's right-hand vector is the bunker's -x, so a bay
                    // further left in the picture is a bay further along +x.
                    Assert.That(
                        across[0], Is.GreaterThan(0.0f), $"{bunker.name} puts the jeep on the right");
                    Assert.That(
                        across[1], Is.GreaterThan(0.0f), $"{bunker.name} puts the tank on the right");
                    Assert.That(
                        across[2], Is.LessThan(0.0f), $"{bunker.name} puts the ASV on the left");
                    Assert.That(
                        across[3], Is.LessThan(0.0f), $"{bunker.name} puts the helicopter on the left");

                    // The two that weigh the most or leave from the roof are the two upstairs.
                    Assert.That(up[1], Is.GreaterThan(up[0]), $"{bunker.name} has the tank downstairs");
                    Assert.That(
                        up[3], Is.GreaterThan(up[2]), $"{bunker.name} has the helicopter downstairs");
                    Assert.That(
                        up[0], Is.EqualTo(up[2]).Within(0.01f), $"{bunker.name}'s lower deck is not level");
                    Assert.That(
                        up[1], Is.EqualTo(up[3]).Within(0.01f), $"{bunker.name}'s upper deck is not level");
                }
            }
        }

        /// <summary>
        /// Every bunker holds its own side a stock of vehicles, both sides get the same one,
        /// and both start the match with something that could win it.
        /// </summary>
        /// <remarks>
        /// The numbers themselves are not asserted, on purpose: this scene is baked from
        /// whichever copy of the map <see cref="LevelLibrary.PathFor"/> finds, and a player
        /// who has saved their own <c>iron-channel</c> is entitled to a different allotment
        /// in it. What may never differ is the two sides from each other - a map that gave
        /// one side more jeeps than the other would be unfair in a way nothing on screen
        /// would show.
        /// </remarks>
        [Test]
        public void EveryBunkerHoldsItsOwnSideAReserveOfVehicles()
        {
            using (var sandbox = new OpenSandbox())
            {
                var stocked = new Dictionary<VehicleKind, int>();

                foreach (TeamBunker bunker in sandbox.All<TeamBunker>())
                {
                    var reserve = bunker.GetComponent<TeamReserve>();

                    Assert.That(reserve, Is.Not.Null, $"{bunker.name} hands out unlimited vehicles");
                    Assert.That(
                        reserve.Team,
                        Is.EqualTo(bunker.Team),
                        $"{bunker.name} is holding the other side's vehicles");
                    Assert.That(
                        reserve.IsBeaten,
                        Is.False,
                        $"{bunker.Team} starts the match with nothing that can carry a flag");

                    foreach (VehicleKind kind in VehiclePrefabBuilder.Roster())
                    {
                        if (stocked.TryGetValue(kind, out int first))
                        {
                            Assert.That(
                                reserve.Remaining(kind),
                                Is.EqualTo(first),
                                $"the two sides are given different numbers of {kind}s");
                        }
                        else
                        {
                            stocked[kind] = reserve.Remaining(kind);
                        }
                    }
                }

                Assert.That(stocked, Is.Not.Empty, "the sandbox has no bunkers at all");
            }
        }

        /// <summary>
        /// Home ground: the only place that fills both pools, and the only one that will
        /// serve the helicopter.
        /// </summary>
        [Test]
        public void EveryBunkerResuppliesItsOwnSideAndTheHelicopter()
        {
            using (var sandbox = new OpenSandbox())
            {
                foreach (TeamBunker bunker in sandbox.All<TeamBunker>())
                {
                    var point = bunker.GetComponent<SupplyPoint>();

                    Assert.That(point, Is.Not.Null, $"{bunker.name} cannot resupply anybody");
                    Assert.That(point.Team, Is.EqualTo(bunker.Team), $"{bunker.name} serves the wrong side");
                    Assert.That(point.FuelRate, Is.GreaterThan(0.0f), $"{bunker.name} has no fuel");
                    Assert.That(point.AmmoRate, Is.GreaterThan(0.0f), $"{bunker.name} has no ammunition");
                    Assert.That(
                        point.ServesAircraft,
                        Is.True,
                        $"{bunker.name} would leave the helicopter nowhere to rearm");
                    Assert.That(
                        point.Radius,
                        Is.GreaterThan(Vector3.Distance(bunker.transform.position, bunker.LiftPoint)),
                        $"{bunker.name} does not reach its own lift");
                }
            }
        }

        /// <summary>
        /// The design document has everything on the map destructible except the two things
        /// a match is played around, so scenery that cannot be shot is scenery in the way.
        /// </summary>
        [Test]
        public void EveryPieceOfSceneryCanBeShotDown()
        {
            using (var sandbox = new OpenSandbox())
            {
                List<Destructible> scenery = sandbox.All<Destructible>();

                Assert.That(scenery.Count, Is.GreaterThan(8), "the sandbox has almost nothing to shoot");

                foreach (Destructible structure in scenery)
                {
                    Assert.That(
                        structure.Kind,
                        Is.Not.EqualTo(StructureKind.None),
                        $"{structure.name} was never told what it is");
                    Assert.That(
                        structure.Tuning.HitPoints,
                        Is.EqualTo(StructureTuning.For(structure.Kind).HitPoints),
                        $"{structure.name} is running on numbers the table does not have");
                    Assert.That(
                        structure.transform.Find(Destructible.IntactNodeName),
                        Is.Not.Null,
                        $"{structure.name} was placed without its models");
                }
            }
        }

        /// <summary>
        /// "Destroyable/contestable" is the design document's phrase for the depots, and it
        /// only means anything if the thing the point is bolted to can actually be knocked
        /// down.
        /// </summary>
        [Test]
        public void EveryDepotStandsOnSomethingThatCanBeDestroyed()
        {
            using (var sandbox = new OpenSandbox())
            {
                foreach (SupplyPoint point in sandbox.All<SupplyPoint>())
                {
                    if (point.GetComponent<TeamBunker>() != null)
                    {
                        continue;
                    }

                    Assert.That(
                        point.GetComponentInParent<Destructible>(),
                        Is.Not.Null,
                        $"{point.name} is a depot nobody can take away");
                }
            }
        }

        /// <summary>
        /// The one exception, and the reason it is one: a bunker is where a side lives, and
        /// a bunker that could be shot out from under its own roster would end a match by
        /// deleting a player rather than by beating them.
        /// </summary>
        /// <remarks>
        /// The flag towers used to be the second exception and are not any more. Shooting
        /// one open is how a raider learns which of the pair is real and the only way to get
        /// a flag off it, so the objective is now something you shoot <em>at</em> rather
        /// than something you can delete - see <see cref="FlagTower"/>.
        /// </remarks>
        [Test]
        public void TheBunkersCannotBeKnockedDown()
        {
            using (var sandbox = new OpenSandbox())
            {
                foreach (TeamBunker bunker in sandbox.All<TeamBunker>())
                {
                    Assert.That(
                        bunker.GetComponentInChildren<Destructible>(true),
                        Is.Null,
                        $"{bunker.name} can be shot out from under its own side");
                }
            }
        }

        /// <summary>
        /// Every tower on the map can be shot open, because otherwise nobody could ever
        /// find a flag.
        /// </summary>
        [Test]
        public void EveryFlagTowerCanBeShotOpen()
        {
            using (var sandbox = new OpenSandbox())
            {
                List<FlagTower> towers = sandbox.All<FlagTower>();
                Assert.That(towers, Is.Not.Empty, "the map has no towers");

                foreach (FlagTower tower in towers)
                {
                    var shell = tower.GetComponent<Destructible>();
                    Assert.That(shell, Is.Not.Null, $"{tower.name} cannot be shot at");
                    Assert.That(
                        shell.Kind,
                        Is.EqualTo(StructureKind.FlagTower),
                        $"{tower.name} is configured as something else");
                    Assert.That(
                        shell.HasDamagedState,
                        Is.True,
                        $"{tower.name} goes straight from standing to rubble, so there is no "
                        + "half-open state to stop at");
                }
            }
        }

        /// <summary>
        /// The map the design document asks for: two towers a side, one of which is real.
        /// </summary>
        /// <remarks>
        /// A side with two real towers has two flags and cannot lose one; a side with none
        /// cannot be beaten at all. Either is a map that quietly is not a game, and neither
        /// is visible by looking at the scene.
        /// </remarks>
        [Test]
        public void EachSideHasARealFlagTowerAndADecoy()
        {
            using (var sandbox = new OpenSandbox())
            {
                List<FlagTower> towers = sandbox.All<FlagTower>();
                Assert.That(
                    towers.Count,
                    Is.EqualTo(VehicleSandboxScene.PlayerCount * 2),
                    "the map does not have two towers a side");

                foreach (Team side in new[] { Team.Green, Team.Brown })
                {
                    var mine = new List<FlagTower>();
                    foreach (FlagTower tower in towers)
                    {
                        if (tower.Team == side)
                        {
                            mine.Add(tower);
                        }
                    }

                    Assert.That(mine.Count, Is.EqualTo(2), $"{side} does not have two towers");

                    int real = 0;
                    foreach (FlagTower tower in mine)
                    {
                        Assert.That(tower.IsOpen, Is.False, $"{tower.name} starts already open");
                        if (tower.HoldsTheFlag)
                        {
                            real++;
                        }
                    }

                    Assert.That(real, Is.EqualTo(1), $"{side} has {real} real towers rather than one");
                }
            }
        }

        /// <summary>
        /// A raider has to open both towers to be certain, which is what the decoy is for.
        /// </summary>
        /// <remarks>
        /// The failure this catches is a layout one: two towers close enough together to sit
        /// inside a single blast would both open to one round, the decoy would cost nothing
        /// to see through, and nothing about the scene would look wrong.
        /// </remarks>
        [Test]
        public void ASidesTwoTowersCannotBothBeOpenedByOneRound()
        {
            using (var sandbox = new OpenSandbox())
            {
                foreach (Team side in new[] { Team.Green, Team.Brown })
                {
                    var mine = new List<FlagTower>();
                    foreach (FlagTower tower in sandbox.All<FlagTower>())
                    {
                        if (tower.Team == side)
                        {
                            mine.Add(tower);
                        }
                    }

                    Assert.That(mine.Count, Is.EqualTo(2), $"{side} does not have two towers");
                    Assert.That(
                        Vector3.Distance(mine[0].transform.position, mine[1].transform.position),
                        Is.GreaterThan(WeaponTuning.For(VehicleKind.Asv).SplashRadius * 2.0f),
                        $"{side}: one rocket would open both towers");
                }
            }
        }

        /// <summary>
        /// One flag a side, on that side's own real tower.
        /// </summary>
        [Test]
        public void EachSideHasOneFlagOnItsRealTower()
        {
            using (var sandbox = new OpenSandbox())
            {
                List<Flag> flags = sandbox.All<Flag>();
                Assert.That(
                    flags.Count,
                    Is.EqualTo(VehicleSandboxScene.PlayerCount),
                    "the map does not have one flag a side");

                var sides = new HashSet<Team>();
                foreach (Flag flag in flags)
                {
                    Assert.That(flag.Team, Is.Not.EqualTo(Team.None), $"{flag.name} belongs to nobody");
                    Assert.That(sides.Add(flag.Team), Is.True, $"{flag.Team} has two flags");

                    Assert.That(flag.Home, Is.Not.Null, $"{flag.name} has no tower");
                    Assert.That(
                        flag.Home.HoldsTheFlag,
                        Is.True,
                        $"{flag.name} is standing on a decoy");
                    Assert.That(
                        flag.Home.Team,
                        Is.EqualTo(flag.Team),
                        $"{flag.name} is standing in the other side's base");
                    Assert.That(
                        flag.State,
                        Is.EqualTo(FlagState.AtTower),
                        $"{flag.name} does not start on its tower");
                }
            }
        }

        /// <summary>
        /// The mirror: neither side has a shorter run than the other.
        /// </summary>
        /// <remarks>
        /// Measured from each bunker to the flag it has to fetch, because that distance is
        /// the whole length of the design document's core loop. Two sides on a mirrored map
        /// whose objectives are different distances away is the one asymmetry a player would
        /// feel and never be able to name.
        /// </remarks>
        [Test]
        public void BothSidesHaveTheSameDistanceToRun()
        {
            using (var sandbox = new OpenSandbox())
            {
                var runs = new List<float>();
                foreach (TeamBunker bunker in sandbox.All<TeamBunker>())
                {
                    Flag target = null;
                    foreach (Flag flag in sandbox.All<Flag>())
                    {
                        if (flag.Team != bunker.Team)
                        {
                            target = flag;
                        }
                    }

                    Assert.That(target, Is.Not.Null, $"{bunker.Team} has nothing to go and fetch");
                    runs.Add(Vector3.Distance(
                        bunker.transform.position, target.transform.position));
                }

                Assert.That(runs.Count, Is.EqualTo(VehicleSandboxScene.PlayerCount));
                Assert.That(
                    runs[0],
                    Is.EqualTo(runs[1]).Within(0.01f),
                    "one side has a shorter raid than the other");
            }
        }

        /// <summary>
        /// The scene has exactly one thing that decides who won.
        /// </summary>
        [Test]
        public void TheSandboxHasOneMatchToWin()
        {
            using (var sandbox = new OpenSandbox())
            {
                List<Match> matches = sandbox.All<Match>();

                Assert.That(matches.Count, Is.EqualTo(1), "the sandbox has no single match");
                Assert.That(matches[0].IsOver, Is.False, "the saved scene has already been won");
                Assert.That(matches[0].Winner, Is.EqualTo(Team.None));
            }
        }

        /// <summary>
        /// Killing the carrier does not win the flag back by itself.
        /// </summary>
        /// <remarks>
        /// The countdown on a dropped flag has to outlast what it costs the raiding side to
        /// come again, which is a repair plus the ride out of the bunker. Both of those are
        /// stamped onto the real prefabs, so this reads them off the scene rather than
        /// restating them.
        /// </remarks>
        [Test]
        public void ADroppedFlagOutlastsTheRaidersTripBackToTheBunker()
        {
            using (var sandbox = new OpenSandbox())
            {
                foreach (VehicleController vehicle in sandbox.All<VehicleController>())
                {
                    if (vehicle.Kind != VehicleKind.Jeep)
                    {
                        continue;
                    }

                    var bay = vehicle.GetComponent<VehicleBay>();
                    Assert.That(bay, Is.Not.Null, $"{vehicle.name} has no bunker bay");
                    Assert.That(
                        FlagRules.ReturnSeconds,
                        Is.GreaterThan(bay.RepairSeconds + bay.DeploySeconds),
                        "a dropped flag goes home before the raider can even leave the bunker");
                }
            }
        }

        /// <summary>
        /// A depot carries one commodity, belongs to nobody, and cannot be used from the air.
        /// </summary>
        [Test]
        public void EveryDepotHandsOutOneThingToWhoeverParksOnIt()
        {
            using (var sandbox = new OpenSandbox())
            {
                var depots = new List<SupplyPoint>();
                foreach (SupplyPoint point in sandbox.All<SupplyPoint>())
                {
                    if (point.GetComponent<TeamBunker>() == null)
                    {
                        depots.Add(point);
                    }
                }

                Assert.That(depots.Count, Is.EqualTo(4), "the map is short of depots");

                foreach (SupplyPoint depot in depots)
                {
                    Assert.That(depot.Team, Is.EqualTo(Team.None), $"{depot.name} is not contestable");
                    Assert.That(
                        depot.ServesAircraft,
                        Is.False,
                        $"{depot.name} would let the helicopter rearm in the field");
                    Assert.That(
                        (depot.FuelRate > 0.0f) != (depot.AmmoRate > 0.0f),
                        Is.True,
                        $"{depot.name} hands out both or neither");
                }
            }
        }

        /// <summary>
        /// A HUD is a sheet of geometry a metre in front of a camera, so the wrong culling
        /// mask puts one player fuel gauge into the other player windscreen.
        /// </summary>
        /// <remarks>
        /// The camera a HUD hangs off is the one stacked on that player's view, not the view
        /// itself - see <see cref="ViewStack"/>. The world camera runs the volume profile and
        /// this one does not, which is the whole reason it exists, and it also has to be the
        /// one camera drawing that player's layer or the HUD is drawn twice.
        /// </remarks>
        [Test]
        public void EachPlayerHasAHudOnTheirOwnCameraAndTheirOwnLayer()
        {
            using (var sandbox = new OpenSandbox())
            {
                List<PlayerHud> huds = sandbox.All<PlayerHud>();

                Assert.That(huds.Count, Is.EqualTo(VehicleSandboxScene.PlayerCount), "a player has no HUD");

                var layers = new HashSet<int>();
                foreach (PlayerHud hud in huds)
                {
                    var canvas = hud.GetComponent<Canvas>();

                    Assert.That(hud.Player, Is.Not.Null, $"{hud.name} reports on nobody");
                    Assert.That(
                        canvas.renderMode,
                        Is.EqualTo(RenderMode.ScreenSpaceCamera),
                        $"{hud.name} is not bound to a viewport");
                    Camera world = hud.Player.CameraRig.View;
                    Camera drawnBy = ViewStack.Existing(world);

                    Assert.That(drawnBy, Is.Not.Null, $"{hud.name} has no camera of its own");
                    Assert.That(
                        canvas.worldCamera,
                        Is.EqualTo(drawnBy),
                        $"{hud.name} is drawn on the wrong camera");
                    Assert.That(
                        drawnBy.GetUniversalAdditionalCameraData().renderPostProcessing,
                        Is.False,
                        $"{hud.name} would be tone-mapped along with the world");
                    Assert.That(
                        layers.Add(hud.gameObject.layer),
                        Is.True,
                        $"{hud.name} shares a layer with another HUD");
                }

                foreach (PlayerHud hud in huds)
                {
                    foreach (PlayerHud other in huds)
                    {
                        int world = hud.Player.CameraRig.View.cullingMask;
                        int drawnBy = ViewStack.Existing(hud.Player.CameraRig.View).cullingMask;

                        Assert.That(
                            world & (1 << other.gameObject.layer),
                            Is.Zero,
                            $"{hud.name} world camera would draw {other.name} through the grade");
                        Assert.That(
                            (drawnBy & (1 << other.gameObject.layer)) != 0,
                            Is.EqualTo(hud == other),
                            $"{hud.name} camera and {other.name} disagree about who sees what");
                    }
                }
            }
        }

        /// <summary>
        /// A generated HUD is built out of the project's own pieces, in the project's own
        /// face - which is the half of the look that cannot be seen in a still.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The font check is the point of this test. <see cref="HudPalette"/> falls back to
        /// Unity's built-in face rather than failing when the asset is missing, so a broken
        /// import, a renamed file or a label built by hand instead of through the palette all
        /// have the same symptom: the game silently goes back to being set in Arial. Nothing
        /// throws and no still looks obviously wrong.
        /// </para>
        /// <para>
        /// It walks every <see cref="Text"/> on the canvas rather than checking the palette,
        /// so it also catches the other half - somebody adding a label with
        /// <c>new GameObject(..., typeof(Text))</c> and never setting a font at all.
        /// </para>
        /// </remarks>
        [Test]
        public void EveryHudIsBuiltFromTheProjectsOwnPiecesAndSetInItsOwnFace()
        {
            using (var sandbox = new OpenSandbox())
            {
                foreach (PlayerHud hud in sandbox.All<PlayerHud>())
                {
                    // The saved scene carries an empty canvas on purpose - see PlayerHud.Build
                    // - so there is nothing to look at until it is filled in.
                    hud.Build();

                    Assert.That(
                        hud.GetComponentsInChildren<HudPlate>(true).Length,
                        Is.EqualTo(4),
                        $"{hud.name} is missing one of its four panels");
                    // Three panels wear corner marks, and so does every cell of the select
                    // console - which is where the marks stopped being decoration: they are
                    // the only thing on that panel saying which vehicle is chosen, because
                    // the rest of the answer is a lit bay out in the world.
                    Assert.That(
                        hud.GetComponentsInChildren<HudBracket>(true).Length,
                        Is.EqualTo(3 + VehicleRoster.Kinds.Length),
                        $"{hud.name} has lost a set of corner marks");

                    // Three gauges and two flags. The fourth bar carries a mark of nothing on
                    // purpose - see PlayerHud.BuildStatusPanel - so the count is of the marks
                    // that actually draw rather than of the objects.
                    int drawn = 0;
                    foreach (HudGlyph mark in hud.GetComponentsInChildren<HudGlyph>(true))
                    {
                        if (mark.Kind != HudGlyphKind.None)
                        {
                            drawn++;
                        }
                    }

                    Assert.That(drawn, Is.EqualTo(5), $"{hud.name} has lost one of its marks");

                    foreach (Text line in hud.GetComponentsInChildren<Text>(true))
                    {
                        Assert.That(line.font, Is.Not.Null, $"{line.name} has no font");
                        Assert.That(
                            line.font.name,
                            Is.EqualTo(HudPalette.BodyFaceName)
                                .Or.EqualTo(HudPalette.DisplayFaceName),
                            $"{line.name} fell back to the built-in face");
                    }
                }
            }
        }

        /// <summary>
        /// The core loop starts in the bunker, so nothing is parked out on the map.
        /// </summary>
        [Test]
        public void EveryVehicleStartsAtItsOwnBunker()
        {
            using (var sandbox = new OpenSandbox())
            {
                List<TeamBunker> bunkers = sandbox.All<TeamBunker>();

                foreach (PlayerVehicleDriver driver in sandbox.All<PlayerVehicleDriver>())
                {
                    TeamBunker home = null;
                    foreach (TeamBunker bunker in bunkers)
                    {
                        if (bunker.Team == driver.Team)
                        {
                            home = bunker;
                        }
                    }

                    Assert.That(home, Is.Not.Null, $"{driver.name} has no bunker");

                    foreach (VehicleController vehicle in driver.Roster)
                    {
                        Assert.That(
                            Vector3.Distance(vehicle.transform.position, home.transform.position),
                            Is.LessThan(20.0f),
                            $"{vehicle.name} starts out on the map rather than at home");
                        Assert.That(
                            vehicle.GetComponent<VehicleBay>(),
                            Is.Not.Null,
                            $"{vehicle.name} has no bay to wait in");
                    }
                }
            }
        }

        /// <summary>
        /// The scene is a shell around a map it reads from a file, and the file is readable.
        /// </summary>
        [Test]
        public void TheSceneLoadsItsMapFromAFile()
        {
            using (var sandbox = new OpenSandbox())
            {
                List<LevelLoader> loaders = sandbox.All<LevelLoader>();
                Assert.That(loaders.Count, Is.EqualTo(1), "the scene has no one level loader");

                LevelLoader loader = loaders[0];
                Assert.That(loader.Catalog, Is.Not.Null, "the loader has nothing to build a map out of");
                Assert.That(
                    loader.Catalog.Problems(),
                    Is.Empty,
                    "the level catalog is missing something a map is made of");

                Assert.That(
                    LevelFile.TryRead(
                        LevelLibrary.PathFor(loader.LevelName), out _, out string problem),
                    Is.True,
                    problem);
            }
        }

        /// <summary>
        /// The map baked into the saved scene is the map in the file, prop for prop.
        /// </summary>
        /// <remarks>
        /// The bake exists so the scene is worth opening and the still is worth rendering,
        /// and it is thrown away on the first frame of play. That makes it exactly the kind
        /// of copy that drifts: somebody edits the JSON, never presses the menu item, and
        /// every screenshot after that is of a map nobody is playing.
        /// </remarks>
        [Test]
        public void TheBakedMapMatchesTheLevelFile()
        {
            LevelDefinition level = TheLevel();

            using (var sandbox = new OpenSandbox())
            {
                Assert.That(
                    sandbox.All<TeamBunker>().Count,
                    Is.EqualTo(level.Bunkers.Length),
                    "the baked map has the wrong number of bunkers");
                Assert.That(
                    sandbox.All<FlagTower>().Count,
                    Is.EqualTo(level.Towers.Length),
                    "the baked map has the wrong number of towers");
                // Towers are destructibles too now, and they are counted by the line above:
                // what this one is about is the scenery a level file scatters.
                int scenery = 0;
                foreach (Destructible structure in sandbox.All<Destructible>())
                {
                    if (structure.GetComponent<FlagTower>() == null)
                    {
                        scenery++;
                    }
                }

                Assert.That(
                    scenery,
                    Is.EqualTo(level.Structures.Length),
                    "the baked map has the wrong number of destructibles");
                Assert.That(sandbox.All<Flag>().Count, Is.EqualTo(2), "there is not one flag a side");
            }
        }

        /// <summary>
        /// The sea is where the level file puts it, and it is the only one.
        /// </summary>
        [Test]
        public void TheMapHasOneSeaAtTheHeightTheFileGivesIt()
        {
            LevelDefinition level = TheLevel();

            using (var sandbox = new OpenSandbox())
            {
                List<WaterLine> seas = sandbox.All<WaterLine>();
                Assert.That(seas.Count, Is.EqualTo(1), "the map does not have exactly one sea");
                Assert.That(
                    seas[0].Surface,
                    Is.EqualTo(level.Bounds.WaterLevel).Within(0.001f),
                    "the sea is not at the height the level file gives it");
                Assert.That(
                    seas[0].DrownDepth,
                    Is.GreaterThan(seas[0].Surface),
                    "you drown below the water rather than in it");
            }
        }

        /// <summary>
        /// The match is what is being played, not part of what it is played on.
        /// </summary>
        /// <remarks>
        /// If it hung off the map, loading a level would destroy it and take
        /// <see cref="Match.IsFinished"/> with it - so an editor reloading a map mid-session
        /// would silently restart the match, and a level file would be describing a rule.
        /// </remarks>
        [Test]
        public void TheMatchIsNotPartOfTheMap()
        {
            using (var sandbox = new OpenSandbox())
            {
                List<Match> matches = sandbox.All<Match>();
                Assert.That(matches.Count, Is.EqualTo(1), "the scene does not hold exactly one match");

                LevelLoader loader = sandbox.All<LevelLoader>()[0];
                Assert.That(
                    matches[0].transform.IsChildOf(loader.transform),
                    Is.False,
                    "the match hangs off the map, so loading a level would restart it");
            }
        }

        /// <summary>
        /// Nobody starts in the sea.
        /// </summary>
        /// <remarks>
        /// The roster is parked against the bunker the <em>level</em> places, so a level that
        /// moves a bunker to the coast parks four vehicles in the water - and they would
        /// drown on the first frame, before their pilot had touched anything.
        /// </remarks>
        [Test]
        public void EveryVehicleStartsOnDryLand()
        {
            LevelDefinition level = TheLevel();

            using (var sandbox = new OpenSandbox())
            {
                foreach (VehicleController vehicle in sandbox.All<VehicleController>())
                {
                    Assert.That(
                        level.IsOnLand(vehicle.transform.position, 1.0f),
                        Is.True,
                        $"{vehicle.name} is parked in the sea at {vehicle.transform.position}");
                }
            }
        }

        /// <summary>
        /// Reads the level the scene is built around.
        /// </summary>
        /// <returns>The level.</returns>
        private static LevelDefinition TheLevel()
        {
            Assert.That(
                LevelFile.TryRead(
                    LevelLibrary.PathFor(VehicleSandboxScene.LevelName),
                    out LevelDefinition level,
                    out string problem),
                Is.True,
                problem);

            return level;
        }

        /// <summary>
        /// Opens the generated sandbox alongside the test scene, and closes it again.
        /// </summary>
        private sealed class OpenSandbox : System.IDisposable
        {
            private readonly Scene scene;

            public OpenSandbox()
                => scene = EditorSceneManager.OpenScene(
                    VehicleSandboxScene.ScenePath, OpenSceneMode.Additive);

            public List<T> All<T>()
                where T : Component
            {
                var found = new List<T>();
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    found.AddRange(root.GetComponentsInChildren<T>(true));
                }

                return found;
            }

            public void Dispose() => EditorSceneManager.CloseScene(scene, true);
        }
    }
}
