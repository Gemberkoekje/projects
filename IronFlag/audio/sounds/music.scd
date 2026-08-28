// -----------------------------------------------------------------------------
// music.scd - the menu theme, one theme per vehicle, and the two end cues.
//
// The design doc's M8 line is "per-vehicle music/SFX hook", so the match music
// is written as four separate themes rather than one bed: what is playing tells
// you what you are driving. They share a key (A minor) and a motif so that
// switching vehicle mid-match is a change of character, not a change of track.
//
// These are original themes. The obvious homage would be to arrange the
// public-domain classical pieces the genre is known for, but writing them here
// keeps the whole soundtrack the same kind of thing as everything else in this
// repository: generated from source that can be diffed.
//
// LOOPING: each theme's `dur` is exactly its bar count at its tempo, and the
// last note is placed early enough for its release tail to finish inside the
// file. A note whose tail runs past `dur` is simply cut off, and the loop then
// clicks - so when editing a theme, keep the final beat clear.
// -----------------------------------------------------------------------------
(
var march, melody, bass, snare, theme;

// Every theme is the same instrument selected three ways, so a piece is built
// by concatenating three note lists over one graph. See ~rfv[\ensemble].
melody = { |bpm, rows, root| ~rfv[\score].(bpm, rows, root, [\voice, 0, \pan, 0.0]) };
bass = { |bpm, rows, root| ~rfv[\score].(bpm, rows, root, [\voice, 1, \pan, 0.0]) };
snare = { |bpm, rows| ~rfv[\hits].(bpm, rows, [\voice, 2, \pan, 0.25]) };

// The graph every theme renders through. Stereo, because music is the one place
// in this game where width is free: it never needs to be positioned.
theme = { |voice = 0, freq = 220, sus = 0.4, level = 0.2, tone = 2200, pan = 0|
    Pan2.ar(~rfv[\ensemble].(voice, freq, sus, level, tone), pan);
};

// A plain military backbeat, reused at whatever tempo it is handed.
march = [
    [1, 0.16], [3, 0.12], [5, 0.16], [7, 0.12],
    [9, 0.16], [11, 0.12], [13, 0.16], [15, 0.10], [15.5, 0.10],
    [17, 0.16], [19, 0.12], [21, 0.16], [23, 0.12],
    [25, 0.16], [27, 0.12], [29, 0.16], [29.5, 0.12], [30, 0.14]
];

(
'RF_Music_MenuTheme': (
    // 96 bpm, 32 beats = 20.000s exactly.
    // Slow, minor, and unhurried: this plays while nothing is happening.
    dur: 20.0,
    channels: 2,
    note: "loop",
    graph: theme,
    notes:
        melody.(96, [
            [0.0, 0, 1.5, 0.20], [2.0, 2, 0.5, 0.16], [2.5, 3, 1.5, 0.20],
            [4.0, 4, 2.0, 0.22], [6.5, 3, 0.5, 0.16], [7.0, 2, 1.0, 0.18],
            [8.0, 0, 1.5, 0.20], [10.0, 4, 0.5, 0.16], [10.5, 5, 1.5, 0.22],
            [12.0, 7, 2.0, 0.24], [14.5, 4, 1.0, 0.18],
            [16.0, 5, 1.5, 0.22], [18.0, 4, 0.5, 0.16], [18.5, 3, 1.5, 0.20],
            [20.0, 2, 2.0, 0.20], [22.5, 0, 0.5, 0.16], [23.0, 2, 1.0, 0.18],
            [24.0, 3, 1.5, 0.20], [26.0, 2, 0.5, 0.16], [26.5, 0, 2.5, 0.22]
        ], 220)
        ++ bass.(96, [
            [0.0, 0, 1.8, 0.26], [2.0, 0, 1.8, 0.22], [4.0, 3, 1.8, 0.26],
            [6.0, 3, 1.8, 0.22], [8.0, 0, 1.8, 0.26], [10.0, 0, 1.8, 0.22],
            [12.0, 4, 1.8, 0.26], [14.0, 4, 1.8, 0.22],
            [16.0, 5, 1.8, 0.26], [18.0, 5, 1.8, 0.22], [20.0, 3, 1.8, 0.26],
            [22.0, 3, 1.8, 0.22], [24.0, 2, 1.8, 0.26], [26.0, 0, 2.8, 0.24]
        ], 110)
        ++ snare.(96, march)
),

'RF_Music_MatchJeep': (
    // 120 bpm, 32 beats = 16.000s exactly.
    // "Fastest, weakest, and the only vehicle that can carry the flag" - so the
    // jeep's theme is the quickest and the least armoured-sounding, and it is
    // the one that keeps moving rather than landing on long notes.
    dur: 16.0,
    channels: 2,
    note: "loop",
    graph: theme,
    notes:
        melody.(120, [
            [0.0, 0, 0.5, 0.20], [0.5, 2, 0.5, 0.18], [1.0, 3, 0.5, 0.20],
            [1.5, 4, 0.5, 0.18], [2.0, 3, 1.0, 0.20], [3.5, 2, 0.5, 0.16],
            [4.0, 4, 0.5, 0.20], [4.5, 5, 0.5, 0.18], [5.0, 7, 1.0, 0.24],
            [6.5, 5, 0.5, 0.18], [7.0, 4, 1.0, 0.20],
            [8.0, 3, 0.5, 0.20], [8.5, 4, 0.5, 0.18], [9.0, 5, 0.5, 0.20],
            [9.5, 4, 0.5, 0.18], [10.0, 3, 1.0, 0.20], [11.5, 2, 0.5, 0.16],
            [12.0, 0, 0.5, 0.20], [12.5, 2, 0.5, 0.18], [13.0, 3, 1.0, 0.22],
            [14.0, 2, 1.0, 0.18]
        ], 330)
        ++ bass.(120, [
            [0.0, 0, 0.9, 0.26], [1.0, 0, 0.9, 0.20], [2.0, 3, 0.9, 0.26],
            [3.0, 3, 0.9, 0.20], [4.0, 4, 0.9, 0.26], [5.0, 4, 0.9, 0.20],
            [6.0, 2, 0.9, 0.26], [7.0, 2, 0.9, 0.20],
            [8.0, 0, 0.9, 0.26], [9.0, 0, 0.9, 0.20], [10.0, 3, 0.9, 0.26],
            [11.0, 3, 0.9, 0.20], [12.0, 4, 0.9, 0.26], [13.0, 4, 0.9, 0.20],
            [14.0, 0, 1.6, 0.24]
        ], 110)
        ++ snare.(120, march)
),

'RF_Music_MatchTank': (
    // 96 bpm, 32 beats = 20.000s exactly.
    // "Medium all-rounder" - this is the straight version of the motif, the one
    // the other three are variations on. Squarely on the beat.
    dur: 20.0,
    channels: 2,
    note: "loop",
    graph: theme,
    notes:
        melody.(96, [
            [0.0, 0, 1.0, 0.22], [1.0, 0, 0.5, 0.18], [1.5, 3, 1.5, 0.22],
            [3.5, 2, 0.5, 0.18], [4.0, 4, 1.5, 0.24], [6.0, 3, 1.0, 0.20],
            [7.0, 2, 1.0, 0.20],
            [8.0, 0, 1.0, 0.22], [9.0, 4, 0.5, 0.18], [9.5, 5, 1.5, 0.24],
            [11.5, 4, 0.5, 0.18], [12.0, 7, 1.5, 0.26], [14.0, 5, 1.5, 0.22],
            [16.0, 4, 1.0, 0.22], [17.0, 3, 0.5, 0.18], [17.5, 2, 1.5, 0.22],
            [19.5, 0, 0.5, 0.18], [20.0, 3, 1.5, 0.24], [22.0, 2, 1.0, 0.20],
            [23.0, 0, 1.0, 0.20], [24.0, 0, 2.0, 0.22], [26.5, 3, 1.5, 0.20]
        ], 220)
        ++ bass.(96, [
            [0.0, 0, 1.6, 0.28], [2.0, 0, 1.6, 0.22], [4.0, 4, 1.6, 0.28],
            [6.0, 4, 1.6, 0.22], [8.0, 0, 1.6, 0.28], [10.0, 0, 1.6, 0.22],
            [12.0, 5, 1.6, 0.28], [14.0, 5, 1.6, 0.22], [16.0, 3, 1.6, 0.28],
            [18.0, 3, 1.6, 0.22], [20.0, 4, 1.6, 0.28], [22.0, 4, 1.6, 0.22],
            [24.0, 0, 1.6, 0.28], [26.0, 0, 2.6, 0.24]
        ], 110)
        ++ snare.(96, march)
),

'RF_Music_MatchAsv': (
    // 80 bpm, 32 beats = 24.000s exactly.
    // "Slowest and most armored" - the motif dragged out and pushed down an
    // octave. Fewer notes, longer ones, and the bass carries it.
    dur: 24.0,
    channels: 2,
    note: "loop",
    graph: theme,
    notes:
        melody.(80, [
            [0.0, 0, 2.0, 0.22], [2.5, 3, 1.5, 0.22], [4.0, 2, 2.0, 0.20],
            [6.5, 0, 1.5, 0.20],
            [8.0, 3, 2.0, 0.24], [10.5, 4, 1.5, 0.22], [12.0, 2, 2.0, 0.20],
            [14.5, 0, 1.5, 0.20],
            [16.0, 4, 2.0, 0.24], [18.5, 3, 1.5, 0.22], [20.0, 2, 2.0, 0.20],
            [22.0, 0, 1.5, 0.22]
        ], 165)
        ++ bass.(80, [
            [0.0, 0, 2.6, 0.30], [3.0, 0, 1.6, 0.22], [5.0, 3, 2.6, 0.28],
            [8.0, 0, 2.6, 0.30], [11.0, 0, 1.6, 0.22], [13.0, 4, 2.6, 0.28],
            [16.0, 2, 2.6, 0.30], [19.0, 2, 1.6, 0.22], [21.0, 0, 2.6, 0.28]
        ], 82.5)
        ++ snare.(80, [
            [2, 0.14], [6, 0.12], [10, 0.14], [14, 0.12],
            [18, 0.14], [22, 0.12], [22.5, 0.10], [23, 0.12]
        ])
),

'RF_Music_MatchHelicopter': (
    // 160 bpm, 32 beats = 12.000s exactly.
    // "Airborne, fast and fragile" - the shortest loop, sitting an octave above
    // everything else, with the snare thinned out so it does not fight the
    // rotor loop that is always playing underneath it.
    dur: 12.0,
    channels: 2,
    note: "loop",
    graph: theme,
    notes:
        melody.(160, [
            [0.0, 7, 0.5, 0.20], [0.5, 5, 0.5, 0.18], [1.0, 4, 1.0, 0.22],
            [2.5, 5, 0.5, 0.18], [3.0, 7, 1.0, 0.22],
            [4.0, 8, 0.5, 0.20], [4.5, 7, 0.5, 0.18], [5.0, 5, 1.0, 0.22],
            [6.5, 4, 0.5, 0.18], [7.0, 3, 1.0, 0.20],
            [8.0, 4, 0.5, 0.20], [8.5, 5, 0.5, 0.18], [9.0, 7, 1.0, 0.24],
            [10.0, 5, 0.5, 0.18], [10.5, 4, 1.0, 0.20]
        ], 330)
        ++ bass.(160, [
            [0.0, 0, 0.9, 0.26], [1.0, 0, 0.9, 0.20], [2.0, 4, 0.9, 0.26],
            [3.0, 4, 0.9, 0.20], [4.0, 3, 0.9, 0.26], [5.0, 3, 0.9, 0.20],
            [6.0, 0, 0.9, 0.26], [7.0, 0, 0.9, 0.20],
            [8.0, 4, 0.9, 0.26], [9.0, 4, 0.9, 0.20], [10.0, 0, 1.4, 0.24]
        ], 110)
        ++ snare.(160, [
            [1, 0.12], [3, 0.10], [5, 0.12], [7, 0.10],
            [9, 0.12], [10.5, 0.10]
        ])
),

'RF_Music_Victory': (
    // Match.Win, winning side. Not a loop: it plays once over the result
    // screen. The menu motif resolved up into the major third.
    dur: 6.0,
    channels: 2,
    graph: theme,
    notes:
        melody.(100, [
            [0.0, 0, 0.5, 0.24], [0.5, 2, 0.5, 0.22], [1.0, 4, 1.0, 0.26],
            [2.0, 5, 0.5, 0.24], [2.5, 7, 2.5, 0.30]
        ], 330)
        ++ bass.(100, [
            [0.0, 0, 0.9, 0.30], [1.0, 4, 0.9, 0.28], [2.0, 7, 3.0, 0.32]
        ], 110)
        ++ snare.(100, [[0, 0.16], [1, 0.14], [2, 0.18], [2.5, 0.14], [3, 0.12]])
),

'RF_Music_Defeat': (
    // Match.Win, losing side - and the sound of MatchOutcome.OutOfJeeps. The
    // same figure as the victory cue, falling instead of rising, slower.
    dur: 6.5,
    channels: 2,
    graph: theme,
    notes:
        melody.(76, [
            [0.0, 4, 0.5, 0.22], [0.5, 3, 0.5, 0.20], [1.0, 2, 1.0, 0.22],
            [2.0, 0, 2.5, 0.24]
        ], 220)
        ++ bass.(76, [
            [0.0, 4, 0.9, 0.28], [1.0, 2, 0.9, 0.26], [2.0, 0, 3.0, 0.30]
        ], 82.5)
        ++ snare.(76, [[0, 0.14], [1, 0.10], [2, 0.12]])
),
)
)
