// -----------------------------------------------------------------------------
// ui.scd - menus, and the bunker.
//
// Wire-up points: the Main Menu buttons (MainMenuScene), the pause menu
// (PauseMenu), the level editor's toolbar, and the bunker's vehicle selection.
//
// These are the only sounds a player hears with no engine running underneath,
// so they are the quietest set here on purpose - a menu click mixed for a
// firefight is painful in a silent menu.
// -----------------------------------------------------------------------------
(
'RF_Sfx_UiClick': (
    // Moving between menu entries. Plays more than any other sound in the game,
    // so it is tiny and completely dry.
    dur: 0.12,
    channels: 1,
    graph: {
        var env = EnvGen.ar(Env.perc(0.001, 0.06, curve: -6));
        var body = SinOsc.ar(1320) * env;
        var edge = ~rfv[\tick].(freq: 6000, dur: 0.012) * 0.5;
        ((body * 0.5) + edge) * 0.35;
    }
),

'RF_Sfx_UiSelect': (
    // Committing to a choice: starting a match, taking a vehicle. Two-note rise
    // so it reads as "yes" next to the flat click.
    dur: 0.35,
    channels: 1,
    graph: {
        var env = EnvGen.ar(Env.perc(0.002, 0.28, curve: -4));
        var freq = EnvGen.ar(Env.new([880, 1320], [0.06], \exponential));
        ((SinOsc.ar(freq) + (SinOsc.ar(freq * 2) * 0.25)) * env * 0.5) * 0.45;
    }
),

'RF_Sfx_UiBack': (
    // Leaving a screen. RF_Sfx_UiSelect played backwards in intent: two notes
    // down.
    dur: 0.30,
    channels: 1,
    graph: {
        var env = EnvGen.ar(Env.perc(0.002, 0.24, curve: -4));
        var freq = EnvGen.ar(Env.new([880, 587], [0.06], \exponential));
        (LPF.ar(SinOsc.ar(freq), 3000) * env * 0.5) * 0.45;
    }
),

'RF_Sfx_UiDenied': (
    // An unavailable choice: a vehicle with no reserves left (see
    // RESERVES_NOTES), a door that is not yours. Flat, buzzed, and unmistakably
    // negative without being loud.
    dur: 0.28,
    channels: 1,
    graph: {
        var env = EnvGen.ar(Env.new([0, 1, 1, 0], [0.004, 0.16, 0.06], [-1, 0, -3]));
        var buzz = Pulse.ar(160, 0.5) + (Pulse.ar(161.5, 0.5) * 0.8);
        (LPF.ar(buzz, 1200) * env * 0.3) * 0.5;
    }
),
)
