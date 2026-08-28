// -----------------------------------------------------------------------------
// objective.scd - the flag, and the end of the match.
//
// Wire-up points: Flag's state transitions (FlagState.AtTower / Carried /
// Dropped / Captured), Flag.SendHome, and Match.Win.
//
// These are the only gameplay sounds that are *pitched* and rising or falling
// on purpose. Everything else in the game is noise-based, so an objective event
// cuts through a firefight without needing to be louder than one.
// -----------------------------------------------------------------------------
(
'RF_Sfx_FlagPickup': (
    // A jeep has the flag. Rising, bright, and short - good news, stated once.
    dur: 0.70,
    channels: 1,
    graph: {
        var env = EnvGen.ar(Env.perc(0.005, 0.55, curve: -3));
        var freq = EnvGen.ar(Env.new([440, 660, 880], [0.10, 0.14], \exponential));
        var tone = SinOsc.ar(freq) + (SinOsc.ar(freq * 2) * 0.35);
        var sparkle = ~rfv[\tick].(freq: 5000, dur: 0.04) * 0.4;
        ((tone * env * 0.5) + sparkle) * 0.55;
    }
),

'RF_Sfx_FlagDropped': (
    // The carrier died. The pickup sound's shape, inverted: falling, duller.
    dur: 0.80,
    channels: 1,
    graph: {
        var env = EnvGen.ar(Env.perc(0.005, 0.65, curve: -3));
        var freq = EnvGen.ar(Env.new([700, 420, 260], [0.12, 0.22], \exponential));
        var tone = SinOsc.ar(freq) + (Pulse.ar(freq * 0.5, 0.4) * 0.25);
        (LPF.ar(tone, 2200) * env) * 0.45;
    }
),

'RF_Sfx_FlagReturned': (
    // Flag.SendHome - back on its tower, nobody scored. Neutral and mechanical
    // rather than good or bad news: a latch closing.
    dur: 0.90,
    channels: 1,
    graph: {
        var clunk = ~rfv[\shell].(freq: 300, dur: 0.35, decay: 0.25);
        var chime = SinOsc.ar([523, 784]) * EnvGen.ar(Env.perc(0.01, 0.6, curve: -4));
        (clunk + (Mix(chime) * 0.22)) * 0.5;
    }
),

'RF_Sfx_FlagCaptured': (
    // The winning moment, per MatchOutcome.FlagCaptured. This is the single
    // most important sound in the game, so it is the longest and the only one
    // that resolves upward through a full chord.
    dur: 2.00,
    channels: 1,
    graph: {
        var env = EnvGen.ar(Env.new([0, 1, 0.8, 0], [0.01, 0.9, 0.9], [-1, -2, -3]));
        var root = 220;
        var chord = SinOsc.ar([1, 1.5, 2, 3] * root)
            + (Pulse.ar([1, 1.5, 2] * root, 0.35) * 0.18);
        var rise = ~rfv[\air].(from: 400, to: 4000, dur: 0.6, bw: 1.2) * 0.25;
        ((Mix(chord) * 0.22 * env) + rise) * 0.5;
    }
),

'RF_Sfx_MatchWon': (
    // Match.Win for the winning side. A short fanfare figure, not a full cue -
    // RF_Music_Victory carries the actual music.
    dur: 1.60,
    channels: 1,
    graph: {
        var env = EnvGen.ar(Env.new([0, 1, 0.7, 0], [0.02, 0.6, 0.9], [-1, -2, -3]));
        var freq = EnvGen.ar(Env.new([330, 440, 660], [0.18, 0.22], \exponential));
        var horn = Mix(Pulse.ar([freq, freq * 1.005, freq * 2], [0.5, 0.45, 0.3], 0.3));
        (LPF.ar(horn, 3000) * env * 0.3) * 2.4;
    }
),

'RF_Sfx_MatchLost': (
    // Match.Win for the losing side, and the sound of running out of jeeps
    // (MatchOutcome.OutOfJeeps). Falling, minor, and slower than the win.
    dur: 2.00,
    channels: 1,
    graph: {
        var env = EnvGen.ar(Env.new([0, 1, 0.6, 0], [0.03, 0.8, 1.1], [-1, -2, -2]));
        var freq = EnvGen.ar(Env.new([330, 262, 196], [0.35, 0.5], \exponential));
        var horn = Mix(Pulse.ar([freq, freq * 1.006, freq * 0.5], [0.5, 0.55, 0.4], 0.3));
        (LPF.ar(horn, 1600) * env * 0.32) * 2.4;
    }
),
)
