// -----------------------------------------------------------------------------
// engine.scd - the render engine behind the IronFlag audio pipeline.
//
// Turns a "recipe" (see sounds/*.scd) into a committed .wav by way of a
// SuperCollider Score rendered in non-realtime by scsynth. Nothing here is
// specific to any one sound; the sounds themselves live in sounds/.
//
// Loaded by build.scd and audition.scd, which put the returned Event in ~rf.
// -----------------------------------------------------------------------------
(
var engine = ();

// Everything renders at CD rate as 16-bit PCM: Unity imports it directly, and
// it is the format AudioImportSettings.cs expects on the other side.
engine[\sampleRate] = 44100;
engine[\headerFormat] = "WAV";
engine[\sampleFormat] = "int16";

// The synthesis server, found next to the sclang running us rather than on
// PATH - a SuperCollider install is self-contained, and on Windows it is
// normally not on PATH at all.
engine[\scsynth] = {
    var dir = Platform.resourceDir;
    var windows = dir +/+ "scsynth.exe";
    if(File.exists(windows)) { windows } { dir +/+ "scsynth" };
};

// Asset names follow the same RF_<Category>_<Name> rule the Blender pipeline
// enforces, for the same reason: a typo should fail the build rather than
// surface later as a missing clip in Unity.
// Note the receiver: in sclang the *pattern* is the receiver and the string
// under test is the argument, which is the reverse of most languages.
engine[\validate] = { |name|
    "^RF_(Sfx|Music)_[A-Za-z0-9]+$".matchRegexp(name.asString);
};

engine[\categoryOf] = { |name|
    if(name.beginsWith("RF_Music_")) { \music } { \sfx };
};

// A stable seed per sound name. Noise UGens draw from the server RNG, which is
// seeded from the clock unless told otherwise - without this every rebuild
// rewrites every noise-based .wav and turns a git diff into confetti.
// djb2, spelled out rather than using .hash so it cannot drift between
// SuperCollider versions.
engine[\seedFor] = { |name|
    var h = 5381;
    name.do { |ch| h = (h * 33 + ch.ascii) % 2147483647 };
    h;
};

// Wraps a recipe graph in the boilerplate every sound needs: output, gain, a
// mono/stereo fold, and a watchdog that frees the synth even if the graph
// forgets its own doneAction.
engine[\synthDefFor] = { |name, recipe|
    var channels = recipe[\channels] ? 1;
    SynthDef(("rf_" ++ name).asSymbol, { |out = 0, amp = 1, lifetime = 8|
        var sig;
        // SynthDef.wrap splices the recipe function in and exposes its own
        // arguments as controls, so notes can set freq, sus and friends.
        sig = SynthDef.wrap(recipe[\graph]);
        sig = sig * amp;
        Line.kr(0, 1, lifetime, doneAction: 2);
        if(channels == 1) {
            // Mono for SFX: Unity will not pan a stereo clip in 3D at all, and
            // mono halves what gets committed.
            if(sig.isArray) { sig = Mix(sig) / sig.size };
            Out.ar(out, sig);
        } {
            if(sig.isArray.not) { sig = sig ! 2 };
            Out.ar(out, sig);
        };
    });
};

// Builds the Score: seed first, then one synth per note, then a sentinel event
// at dur that tells scsynth how long the file is.
engine[\scoreFor] = { |name, recipe|
    var seedDef, voiceDef, events, nodeId, notes, dur;

    dur = recipe[\dur] ? 1.0;
    notes = recipe[\notes] ? [[0.0, []]];

    seedDef = SynthDef(\rf_seed, { |seed = 1|
        RandSeed.ir(1, seed);
        FreeSelf.kr(Impulse.kr(0));
    });
    voiceDef = engine[\synthDefFor].(name, recipe);

    events = [
        [0.0, ['/d_recv', seedDef.asBytes]],
        [0.0, ['/d_recv', voiceDef.asBytes]],
        // Added to the tail of the root group (addAction 1, target 0). Targeting
        // a *synth* here silently produces an empty file: the node is never
        // created, and the render still reports success.
        [0.0, [\s_new, \rf_seed, 990, 1, 0, \seed, engine[\seedFor].(name)]]
    ];

    nodeId = 1000;
    notes.do { |note|
        var at = note[0];
        var args = note[1] ? [];
        events = events.add([at, [\s_new, voiceDef.name, nodeId, 1, 0] ++ args]);
        nodeId = nodeId + 1;
    };

    events = events.add([dur, [\c_set, 0, 0]]);
    Score(events);
};

// Reads a rendered file back and measures it. A render that succeeds into
// silence is the most common failure here, so the build never trusts an exit
// code alone.
engine[\inspect] = { |path|
    block { |done|
        var sf, data, peak = 0, sum = 0, count, frames, channels;
        sf = SoundFile.openRead(path);
        if(sf.isNil) { done.value((ok: false, reason: "unreadable")) };
        frames = sf.numFrames;
        channels = sf.numChannels;
        count = frames * channels;
        if(count <= 0) {
            sf.close;
            done.value((ok: false, reason: "empty"));
        };
        data = FloatArray.newClear(count);
        sf.readData(data);
        sf.close;
        data.do { |x|
            var a = x.abs;
            if(a > peak) { peak = a };
            sum = sum + (x * x);
        };
        (
            ok: true,
            peak: peak,
            rms: (sum / count).sqrt,
            frames: frames,
            channels: channels,
            seconds: frames / engine[\sampleRate]
        );
    };
};

// Renders one recipe to disk and returns a report Event.
engine[\render] = { |name, recipe, outRoot|
    block { |done|
        var score, channels, folder, wavPath, oscPath, cmd, measured, warnings;

        if(engine[\validate].(name).not) {
            done.value((name: name, ok: false,
                reason: "name must look like RF_Sfx_Thing or RF_Music_Thing"));
        };
        if(recipe[\graph].isNil) {
            done.value((name: name, ok: false, reason: "recipe has no graph function"));
        };

        channels = recipe[\channels] ? 1;
        folder = if(engine[\categoryOf].(name) == \music) { "Music" } { "SFX" };
        folder = outRoot +/+ folder;
        if(File.exists(folder).not) { File.mkdir(folder) };

        wavPath = folder +/+ (name ++ ".wav");
        oscPath = Platform.defaultTempDir +/+ (name ++ ".osc");

        score = engine[\scoreFor].(name, recipe);
        score.writeOSCFile(oscPath);

        cmd = "\"" ++ engine[\scsynth].() ++ "\""
            ++ " -N \"" ++ oscPath ++ "\" _ \"" ++ wavPath ++ "\""
            ++ " " ++ engine[\sampleRate]
            ++ " " ++ engine[\headerFormat]
            ++ " " ++ engine[\sampleFormat]
            ++ " -o " ++ channels;
        cmd.unixCmdGetStdOut;

        if(File.exists(oscPath)) { File.delete(oscPath) };

        if(File.exists(wavPath).not) {
            done.value((name: name, ok: false, reason: "scsynth wrote no file"));
        };

        measured = engine[\inspect].(wavPath);
        if(measured[\ok].not) {
            done.value((name: name, ok: false, reason: measured[\reason]));
        };

        warnings = [];
        // int16 clips hard, so anything at full scale is already distorted.
        if(measured[\peak] >= 0.999) {
            warnings = warnings.add("clipping - lower gain");
        };
        // Silence is what a mis-targeted node or an empty graph looks like.
        if(measured[\peak] < 0.02) {
            warnings = warnings.add("effectively silent");
        };
        if((measured[\peak] >= 0.02) and: { measured[\peak] < 0.2 }) {
            warnings = warnings.add("quiet - consider raising gain");
        };

        (
            name: name, ok: true, path: wavPath,
            peak: measured[\peak], rms: measured[\rms],
            seconds: measured[\seconds], channels: measured[\channels],
            warnings: warnings
        );
    };
};

engine;
)
