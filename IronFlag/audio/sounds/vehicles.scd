// -----------------------------------------------------------------------------
// vehicles.scd - one looping engine per VehicleKind.
//
// Mirrors unity/Assets/RF/Scripts/Vehicles/VehicleKind.cs. These are the only
// continuous sounds in the game: everything else is a one-shot. They are meant
// to be played on a looping AudioSource with pitch driven by throttle, so what
// is rendered here is the idle - the bottom of the range, not the middle.
//
// LOOPING: each of these has no envelope, and its `dur` is chosen so that
// rate * dur is a whole number. That is what makes the file wrap without a
// click; changing one of these durations without re-checking that arithmetic is
// the easiest way to break these sounds. See ~rfv[\motor] for why.
// -----------------------------------------------------------------------------
(
'RF_Sfx_EngineJeep': (
    // "Fastest, weakest" - highest firing rate, thinnest body, most road noise.
    // 30 Hz * 2.0s = 60 cycles.
    dur: 2.00,
    channels: 1,
    note: "loop",
    graph: {
        var motor = ~rfv[\motor].(rate: 30, roughness: 0.5, tone: 950);
        var whine = SinOsc.ar(240) * 0.06;
        (motor + whine) * 0.55;
    }
),

'RF_Sfx_EngineTank': (
    // "Medium all-rounder" - slower, heavier, with a track rumble under it.
    // 14 Hz * 2.0s = 28 cycles.
    dur: 2.00,
    channels: 1,
    note: "loop",
    graph: {
        var motor = ~rfv[\motor].(rate: 14, roughness: 0.35, tone: 520);
        var rumble = LPF.ar(BrownNoise.ar(0.35), 120);
        var tracks = ~rfv[\motor].(rate: 28, roughness: 0.15, tone: 1800) * 0.25;
        (motor + rumble + tracks) * 0.5;
    }
),

'RF_Sfx_EngineAsv': (
    // "Slowest and most armored" - the lowest, thickest engine in the game.
    // 9 Hz * 2.0s = 18 cycles.
    dur: 2.00,
    channels: 1,
    note: "loop",
    graph: {
        var motor = ~rfv[\motor].(rate: 9, roughness: 0.3, tone: 380);
        var rumble = LPF.ar(BrownNoise.ar(0.5), 90);
        var lug = SinOsc.ar(54) * 0.12;
        (motor + rumble + lug) * 0.5;
    }
),

'RF_Sfx_EngineHelicopter': (
    // "Airborne, fast and fragile" - rotor chop rather than a piston engine,
    // because that is the sound that says helicopter before anything else does.
    // 12 Hz * 2.0s = 24 cycles.
    dur: 2.00,
    channels: 1,
    note: "loop",
    graph: {
        var rotor = ~rfv[\rotor].(rate: 12, tone: 1100);
        var turbine = BPF.ar(PinkNoise.ar(1), 3200, 0.4) * 0.25;
        ((rotor * 0.5) + turbine) * 1.4;
    }
),
)
