// -----------------------------------------------------------------------------
// build.scd - renders every IronFlag sound into the Unity project.
//
// Run through build.ps1 / build.sh rather than directly; they find sclang for
// you and give you real flags. Under the wrapper this is just:
//
//     sclang audio/build.scd sound Cannon
//
// The arguments are bare words rather than --flags because sclang parses
// anything leading with a dash as one of its own options and exits, and a
// lone -- drops it into an interactive REPL that never returns. The wrappers
// translate -Sound/--sound into this form so the quirk stays in one place.
//
// Discovers sounds/*.scd the way blender/build.py discovers assets/*.py: there
// is no registry to update, a new module is picked up by existing.
// -----------------------------------------------------------------------------
(
var status;

// Everything runs inside a block because `exit` does not stop execution in
// sclang - it schedules the process to quit but lets the current expression run
// to the end, so an early `0.exit` would print a listing and then render
// anyway. `done.value(...)` is the real early return.
status = block { |done|
    var here, repoRoot, outRoot, argv, filter, listOnly, quiet;
    var sounds, names, reports, failures, warned, pad, num;

    // --- where we are -------------------------------------------------------

    // nowExecutingPath is whatever was typed on the command line, so it is only
    // absolute if the caller made it so. Resolve it either way: PathName("")
    // .parentPath throws rather than returning nil.
    //
    // PathName:isAbsolutePath only tests for a leading slash, so it calls every
    // Windows path relative and this ends up prepending the working directory
    // to a perfectly good C:/... path. Test for a drive letter as well.
    here = thisProcess.nowExecutingPath ? "build.scd";
    if((here.at(0) != $/) and: { (here.size < 2) or: { here.at(1) != $: } }) {
        here = File.getcwd +/+ here;
    };
    here = PathName(here).pathOnly;
    repoRoot = PathName(here).parentPath;

    // --- arguments ----------------------------------------------------------

    argv = thisProcess.argv;
    filter = "";
    listOnly = false;
    quiet = false;
    outRoot = repoRoot +/+ "unity/Assets/RF/Audio";

    // `arg` is a reserved word in sclang - naming this block argument `arg` is a
    // syntax error, not a shadowing warning.
    argv.do { |token, i|
        case
        { token == "list" } { listOnly = true }
        { token == "quiet" } { quiet = true }
        { token == "sound" } { filter = (argv[i + 1] ? "").asString }
        { token == "out" } { outRoot = (argv[i + 1] ? outRoot).asString };
    };

    pad = { |str, width|
        var s = str.asString;
        s ++ " ".dup((width - s.size).max(0)).join;
    };
    num = { |x, places = 2| x.round(10 ** places.neg).asString };

    // --- library ------------------------------------------------------------

    ~rf = (here +/+ "rf/engine.scd").standardizePath.load;
    ~rfv = (here +/+ "rf/voices.scd").standardizePath.load;

    if(~rf.isNil or: { ~rfv.isNil }) {
        "FATAL: could not load audio/rf/engine.scd or audio/rf/voices.scd".postln;
        done.value(1);
    };

    // --- discovery ----------------------------------------------------------

    sounds = ();
    PathName(here +/+ "sounds").files.do { |file|
        var loaded;
        if(file.extension == "scd") {
            loaded = file.fullPath.load;
            if(loaded.isNil) {
                ("FATAL: " ++ file.fileName
                    ++ " did not evaluate to a set of sounds").postln;
                done.value(1);
            };
            loaded.keysValuesDo { |key, recipe|
                if(sounds[key].notNil) {
                    ("FATAL: " ++ key ++ " is defined twice").postln;
                    done.value(1);
                };
                sounds[key] = recipe;
            };
        };
    };

    names = sounds.keys.asArray.collect(_.asString).sort;

    if(filter.isEmpty.not) {
        names = names.select { |n| n.asString.toLower.contains(filter.toLower) };
    };

    if(names.isEmpty) {
        if(filter.isEmpty) {
            "No sounds found in audio/sounds/.".postln;
        } {
            ("No sound matches " ++ filter.quote ++ ".").postln;
        };
        done.value(1);
    };

    // --- list ---------------------------------------------------------------

    if(listOnly) {
        ("" ++ names.size ++ " sounds:").postln;
        names.do { |n|
            var recipe = sounds[n.asSymbol];
            ("  " ++ pad.(n, 28)
                ++ pad.(num.(recipe[\dur] ? 1.0) ++ "s", 8)
                ++ pad.(if((recipe[\channels] ? 1) == 1, "mono", "stereo"), 8)
                ++ (recipe[\note] ? "")).postln;
        };
        done.value(0);
    };

    // --- render -------------------------------------------------------------

    ("Rendering " ++ names.size ++ " sounds into " ++ outRoot).postln;
    "".postln;

    reports = names.collect { |name|
        var report = ~rf[\render].(name, sounds[name.asSymbol], outRoot);
        if(quiet.not) {
            if(report[\ok]) {
                ("  " ++ pad.(name, 28)
                    ++ pad.(num.(report[\seconds]) ++ "s", 8)
                    ++ pad.(if(report[\channels] == 1, "mono", "stereo"), 8)
                    ++ "peak " ++ pad.(num.(report[\peak]), 6)
                    ++ "rms " ++ num.(report[\rms], 3)
                    ++ if(report[\warnings].isEmpty, "",
                        "   <- " ++ report[\warnings].join(", "))).postln;
            } {
                ("  " ++ pad.(name, 28) ++ "FAILED: " ++ report[\reason]).postln;
            };
        };
        report;
    };

    // --- verdict ------------------------------------------------------------

    "".postln;
    failures = reports.select { |r| r[\ok].not };
    warned = reports.select { |r| r[\ok] and: { r[\warnings].isEmpty.not } };

    if(failures.isEmpty) {
        ("Rendered " ++ reports.size ++ " sounds."
            ++ if(warned.isEmpty, "", " " ++ warned.size ++ " with warnings.")).postln;
        0;
    } {
        ("" ++ failures.size ++ " of " ++ reports.size ++ " sounds failed.").postln;
        1;
    };
};

status.exit;
)
