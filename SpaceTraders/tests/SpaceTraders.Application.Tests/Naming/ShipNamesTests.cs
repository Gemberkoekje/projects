using FluentAssertions;
using SpaceTraders.Application.Naming;
using SpaceTraders.Application.Ports;

namespace SpaceTraders.Application.Tests.Naming;

/// <summary>
/// Slice 2.14 (D72), asked on 2026-10-04: "SHIPS are now named by the game in ascending order. Can we make custom names
/// within the API which should be type-number … Bonus points if there's a list of relevant names for each of the types, one
/// of which is picked per reset to call that type, e.g. all sattelites being called SPUTNIK-1, SPUTNIK-2 etc. There should
/// be a list of potential names for each ShipType SHIPYARD enum value. This does mean that while SIPHON DRONE and MINING
/// DRONE are both EXCAVATORs, they should get different names."
/// </summary>
public sealed class ShipNamesTests
{
    private const string Reset = "2026-10-04";

    /// <summary>The registration roles the API gives ships: a ship of a type with no list is named after its role.</summary>
    private static readonly string[] Roles =
        ["FABRICATOR", "HARVESTER", "HAULER", "INTERCEPTOR", "EXCAVATOR", "TRANSPORT", "REPAIR", "SURVEYOR", "COMMAND", "CARRIER", "PATROL", "SATELLITE", "EXPLORER", "REFINERY"];

    [Fact]
    public void EachShip_IsNamedAfterItsType_AndNumberedInTheOrderItJoinedTheFleet()
    {
        // The starting fleet, two drones and a second probe: as startup sync stores them, or as bought since.
        var names = ShipNames.For(
            [
                CommandShip("SPECTER-1"),
                Synced("SPECTER-2", "SATELLITE", "FRAME_PROBE"),
                Synced("SPECTER-3", "EXCAVATOR", "FRAME_DRONE", "MOUNT_MINING_LASER_I"),
                Bought("SPECTER-4", "SHIP_PROBE"),
                Bought("SPECTER-5", "SHIP_MINING_DRONE"),
            ],
            Reset);

        var probe = Stem(names["SPECTER-2"]);
        ShipNames.Lists["SHIP_PROBE"].Should().Contain(probe);
        names["SPECTER-2"].Should().Be($"{probe}-1");
        names["SPECTER-4"].Should().Be($"{probe}-2");

        var drone = Stem(names["SPECTER-3"]);
        ShipNames.Lists["SHIP_MINING_DRONE"].Should().Contain(drone);
        names["SPECTER-3"].Should().Be($"{drone}-1");
        names["SPECTER-5"].Should().Be($"{drone}-2");

        ShipNames.Lists["SHIP_COMMAND_FRIGATE"].Should().Contain(Stem(names["SPECTER-1"]));
        names["SPECTER-1"].Should().EndWith("-1");
    }

    [Fact]
    public void ASiphonDroneAndAMiningDrone_BothExcavators_GetDifferentNames()
    {
        var names = ShipNames.For(
            [
                Synced("SPECTER-3", "EXCAVATOR", "FRAME_DRONE", "MOUNT_MINING_LASER_I"),
                Synced("SPECTER-4", "EXCAVATOR", "FRAME_DRONE", "MOUNT_GAS_SIPHON_I"),
            ],
            Reset);

        ShipNames.Lists["SHIP_MINING_DRONE"].Should().Contain(Stem(names["SPECTER-3"]));
        ShipNames.Lists["SHIP_SIPHON_DRONE"].Should().Contain(Stem(names["SPECTER-4"]));
        names["SPECTER-3"].Should().EndWith("-1");
        names["SPECTER-4"].Should().EndWith("-1");
    }

    [Fact]
    public void TheNumbers_FollowTheOrderTheGameNumbersShipsIn_Hexadecimal()
    {
        // The game counts in hexadecimal: SPECTER-F (15) joined before SPECTER-10 (16), and SPECTER-9 before SPECTER-A.
        var names = ShipNames.For(
            [Bought("SPECTER-10", "SHIP_PROBE"), Bought("SPECTER-A", "SHIP_PROBE"), Bought("SPECTER-F", "SHIP_PROBE"), Bought("SPECTER-9", "SHIP_PROBE")],
            Reset);

        var stem = Stem(names["SPECTER-9"]);
        names.Should().Equal(new Dictionary<string, string>
        {
            ["SPECTER-9"] = $"{stem}-1",
            ["SPECTER-A"] = $"{stem}-2",
            ["SPECTER-F"] = $"{stem}-3",
            ["SPECTER-10"] = $"{stem}-4",
        });
    }

    [Fact]
    public void AnAgentSymbolWithADash_IsNumberedByItsShipsOwnNumber()
    {
        // The debug agents were called SPECTER-DEBUG; their ships SPECTER-DEBUG-1, SPECTER-DEBUG-2.
        var names = ShipNames.For([Bought("SPECTER-DEBUG-B", "SHIP_PROBE"), Synced("SPECTER-DEBUG-2", "SATELLITE", "FRAME_PROBE")], Reset);

        names["SPECTER-DEBUG-2"].Should().EndWith("-1");
        names["SPECTER-DEBUG-B"].Should().EndWith("-2");
    }

    [Fact]
    public void OneNameOfTheList_IsPickedPerReset_AndKeptThroughout()
    {
        // The same reset gives the same names, whatever order the fleet comes in: a restart keeps them.
        IReadOnlyList<ShipModel> fleet = [CommandShip("SPECTER-1"), Synced("SPECTER-2", "SATELLITE", "FRAME_PROBE"), Bought("SPECTER-3", "SHIP_MINING_DRONE")];
        ShipNames.For(fleet, Reset).Should().Equal(ShipNames.For([.. fleet.Reverse()], Reset));

        // Another reset picks again: over a few weeks of resets, the probes go by more than one name.
        var probeStems = Enumerable.Range(0, 10)
            .Select(week => new DateOnly(2026, 10, 04).AddDays(7 * week).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
            .Select(reset => Stem(ShipNames.For([Bought("SPECTER-2", "SHIP_PROBE")], reset)["SPECTER-2"]))
            .Distinct()
            .ToList();
        probeStems.Should().HaveCountGreaterThan(1).And.OnlyContain(stem => ShipNames.Lists["SHIP_PROBE"].Contains(stem));
    }

    [Theory]
    [InlineData("SHIP_COMMAND_FRIGATE", "INTREPID")]
    [InlineData("SHIP_PROBE", "MARINER")]
    [InlineData("SHIP_MINING_DRONE", "PICKAXE")]
    [InlineData("SHIP_SIPHON_DRONE", "HUMMINGBIRD")]
    [InlineData("SHIP_SURVEYOR", "ASTROLABE")]
    [InlineData("SHIP_LIGHT_SHUTTLE", "ROBIN")]
    [InlineData("SHIP_LIGHT_HAULER", "PONY")]
    public void TheNamesOfTheRunOf2026_10_04_StayAsTheyAre(string type, string name)
    {
        // Reordering a list, or changing how a name is picked, renames the fleet of the run under way at the next deploy.
        ShipNames.For([Bought("SPECTER-2", type)], "2026-10-04")["SPECTER-2"].Should().Be($"{name}-1");
    }

    [Theory]
    [InlineData("SHIP_PROBE", "SATELLITE", "FRAME_PROBE", null)]
    [InlineData("SHIP_MINING_DRONE", "EXCAVATOR", "FRAME_DRONE", "MOUNT_MINING_LASER_I")]
    [InlineData("SHIP_SIPHON_DRONE", "EXCAVATOR", "FRAME_DRONE", "MOUNT_GAS_SIPHON_I")]
    [InlineData("SHIP_SURVEYOR", "SURVEYOR", "FRAME_DRONE", "MOUNT_SURVEYOR_I")]
    [InlineData("SHIP_LIGHT_SHUTTLE", "TRANSPORT", "FRAME_SHUTTLE", null)]
    [InlineData("SHIP_LIGHT_HAULER", "HAULER", "FRAME_LIGHT_FREIGHTER", "MOUNT_SENSOR_ARRAY_I")]
    [InlineData("SHIP_HEAVY_FREIGHTER", "HAULER", "FRAME_HEAVY_FREIGHTER", null)]
    [InlineData("SHIP_BULK_FREIGHTER", "HAULER", "FRAME_BULK_FREIGHTER", null)]
    [InlineData("SHIP_ORE_HOUND", "EXCAVATOR", "FRAME_MINER", "MOUNT_MINING_LASER_II")]
    [InlineData("SHIP_EXPLORER", "EXPLORER", "FRAME_EXPLORER", null)]
    [InlineData("SHIP_INTERCEPTOR", "INTERCEPTOR", "FRAME_INTERCEPTOR", null)]
    [InlineData("SHIP_COMMAND_FRIGATE", "COMMAND", "FRAME_FRIGATE", "MOUNT_SURVEYOR_II")]
    public void ABoughtShip_KeepsItsName_OnceStartupSyncRecordsItsRoleAndEquipment(string type, string role, string frame, string? mount)
    {
        // Bought, a ship is cached by the type it was bought as, until the next start's sync caches its registration role,
        // frame and mounts instead (B25).
        ShipNames.TypeOf(Bought("SPECTER-7", type)).Should().Be(type);
        ShipNames.TypeOf(Synced("SPECTER-7", role, frame, mount is null ? [] : [mount])).Should().Be(type);
    }

    [Fact]
    public void ARefiningFreighter_IsToldFromAHeavyFreighter_ByItsRefinery()
    {
        var refining = Synced("SPECTER-7", "REFINERY", "FRAME_HEAVY_FREIGHTER") with
        {
            ModulesJson = """[{"symbol":"MODULE_CARGO_HOLD_III","capacity":120},{"symbol":"MODULE_ORE_REFINERY_I"}]""",
        };

        ShipNames.TypeOf(refining).Should().Be("SHIP_REFINING_FREIGHTER");
    }

    [Fact]
    public void AShipOfATypeWithNoList_IsNamedAfterItsRole()
    {
        // The plain version asked for: "COMMAND-1, SATTELITE-1, EXCAVATOR-1", for a frame no shipyard type has (yet).
        var names = ShipNames.For(
            [Synced("SPECTER-7", "PATROL", "FRAME_CRUISER"), Synced("SPECTER-8", "PATROL", "FRAME_CRUISER"), Synced("SPECTER-9", string.Empty, string.Empty)],
            Reset);

        names.Should().Equal(new Dictionary<string, string>
        {
            ["SPECTER-7"] = "PATROL-1",
            ["SPECTER-8"] = "PATROL-2",
            ["SPECTER-9"] = "SHIP-1",
        });
    }

    [Fact]
    public void EveryTypeAShipyardSells_HasNamesOfItsOwn()
    {
        // The API's ShipType: the domain's enum, which lists SHIP_BULK_FREIGHTER too since slice 6.33 (D112).
        var types = Enum.GetValues<SpaceTraders.Domain.Enums.ShipType>()
            .Where(type => type != SpaceTraders.Domain.Enums.ShipType.None)
            .Select(type => string.Concat(type.ToString().Select((letter, at) => at > 0 && char.IsUpper(letter) ? $"_{letter}" : $"{letter}")).ToUpperInvariant())
            .ToList();

        ShipNames.Lists.Keys.Should().BeEquivalentTo(types);
        ShipNames.Lists.Values.Should().OnlyContain(names => names.Count >= 5);

        var all = ShipNames.Lists.Values.SelectMany(names => names).ToList();
        all.Should().OnlyHaveUniqueItems("a name in two lists would name two kinds of ship alike");
        all.Should().NotIntersectWith(Roles, "a ship of a type with no list is named after its role");
        all.Should().OnlyContain(name => name.Length > 0 && name.All(char.IsAsciiLetterUpper));
    }

    private static string Stem(string name) => name[..name.LastIndexOf('-')];

    /// <summary>The command ship, as startup sync stores it: role COMMAND, a frigate with a surveyor and a mining laser.</summary>
    private static ShipModel CommandShip(string symbol)
        => Synced(symbol, "COMMAND", "FRAME_FRIGATE", "MOUNT_SENSOR_ARRAY_II", "MOUNT_GAS_SIPHON_II", "MOUNT_MINING_LASER_II", "MOUNT_SURVEYOR_II");

    /// <summary>A ship as startup sync stores it: its registration role as its type, its frame and its mounts.</summary>
    private static ShipModel Synced(string symbol, string role, string frame, params string[] mounts)
        => new(
            symbol,
            "X1-FJ91",
            "X1-FJ91-A1",
            "DOCKED",
            "CRUISE",
            0,
            0,
            ShipType: role,
            MountSymbols: mounts,
            FrameJson: frame.Length == 0 ? null : $$"""{"symbol":"{{frame}}","name":"Frame","fuelCapacity":80}""");

    /// <summary>A ship as a purchase stores it: the type it was bought as, no frame and no mounts yet.</summary>
    private static ShipModel Bought(string symbol, string type)
        => new(symbol, "X1-FJ91", "X1-FJ91-H52", "DOCKED", "CRUISE", 0, 0, ShipType: type);
}
