// -----------------------------------------------------------------------------
// audition.scd - play sounds live, without rendering anything.
//
//     ./audition.ps1 -Sound Cannon
//     ./audition.sh --sound Cannon
//
// This is the half of the workflow that makes SuperCollider worth using rather
// than a script that just writes wav files: the same recipe that renders to
// disk can be heard immediately, so a sound can be tuned in a loop of seconds
// rather than by rendering, importing and entering play mode.
//
// Nothing here writes to the Unity project. When a sound is right, run
// build.ps1 to commit it.
//
// Arguments are bare words for the same reason as build.scd - see its header.
// -----------------------------------------------------------------------------
(
var here, argv, filter, repeat, gap;
var sounds, names, server;

// PathName:isAbsolutePath only tests for a leading slash, so it calls every
// Windows path relative. Test for a drive letter as well - see build.scd.
here = thisProcess.nowExecutingPath ? "audition.scd";
if((here.at(0) != $/) and: { (here.size < 2) or: { here.at(1) != $: } }) {
    here = File.getcwd +/+ here;
};
here = PathName(here).pathOnly;

argv = thisProcess.argv;
filter = "";
repeat = 1;
gap = 0.6;

argv.do { |token, i|
    case
    { token == "sound" } { filter = (argv[i + 1] ? "").asString }
    { token == "repeat" } { repeat = (argv[i + 1] ? 1).asInteger }
    { token == "gap" } { gap = (argv[i + 1] ? 0.6).asFloat };
};

~rf = (here +/+ "rf/engine.scd").standardizePath.load;
~rfv = (here +/+ "rf/voices.scd").standardizePath.load;

sounds = ();
PathName(here +/+ "sounds").files.do { |file|
    if(file.extension == "scd") {
        (file.fullPath.load ? ()).keysValuesDo { |key, recipe| sounds[key] = recipe };
    };
};

names = sounds.keys.asArray.collect(_.asString).sort;
if(filter.isEmpty.not) {
    names = names.select { |n| n.toLower.contains(filter.toLower) };
};

if(names.isEmpty) {
    ("Nothing matches " ++ filter.quote ++ ".").postln;
    1.exit;
};

server = Server.default;
// A real server this time, not the NRT one. Boot is asynchronous, so
// everything below has to happen inside waitForBoot.
server.waitForBoot {
    var clock = TempoClock.new;

    // The SynthDefs are built exactly the way the renderer builds them, so what
    // is heard here is what gets written to disk - the only difference is that
    // the RNG is not seeded, so noise-based sounds vary between plays the way
    // they would in a game.
    names.do { |name|
        ~rf[\synthDefFor].(name, sounds[name.asSymbol]).add;
    };
    server.sync;

    Routine({
        repeat.do {
            names.do { |name|
                var recipe = sounds[name.asSymbol];
                var notes = recipe[\notes] ? [[0.0, []]];
                var defName = ("rf_" ++ name).asSymbol;
                var elapsed = 0;

                ("  " ++ name).postln;

                // Notes carry absolute times, so play them by waiting the gaps
                // between them rather than scheduling each one.
                notes.sort { |a, b| a[0] < b[0] }.do { |note|
                    var at = note[0];
                    var pause = at - elapsed;
                    if(pause > 0) { pause.wait; elapsed = at };
                    server.sendMsg(\s_new, defName, server.nextNodeID, 1, 0,
                        *(note[1] ? []));
                };

                // Let the tail finish before the next sound starts.
                (((recipe[\dur] ? 1.0) - elapsed).max(0) + gap).wait;
            };
        };
        server.sync;
        0.5.wait;
        "done".postln;
        0.exit;
    }).play(clock);
};
)
