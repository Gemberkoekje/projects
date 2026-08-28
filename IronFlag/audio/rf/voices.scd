// -----------------------------------------------------------------------------
// voices.scd - the shared DSP vocabulary every sound is built out of.
//
// This is the audio equivalent of blender/rf/primitives.py: a small set of
// building blocks so that individual sounds read as a recipe rather than as a
// wall of UGens, and so that the whole game shares one sonic character.
//
// Every function here returns a mono UGen graph unless noted. They are called
// from inside a SynthDef graph, so they may only use UGens - no scheduling.
//
// Loaded by build.scd into the global ~rfv.
// -----------------------------------------------------------------------------
(
var v = ();

// --- transients -------------------------------------------------------------

// The initial spit of a gun or an impact: noise squeezed through a resonant
// band so it reads as a pitched crack rather than a hiss.
v[\crack] = { |freq = 2000, dur = 0.06, res = 0.3|
    var env = EnvGen.ar(Env.perc(0.0005, dur, curve: -8));
    BPF.ar(WhiteNoise.ar(1), freq, res) * env * 4;
};

// A short clicky tick, for mechanical detail: bolt travel, button presses.
v[\tick] = { |freq = 4000, dur = 0.02|
    var env = EnvGen.ar(Env.perc(0.0002, dur, curve: -12));
    HPF.ar(WhiteNoise.ar(1), freq) * env;
};

// --- bodies -----------------------------------------------------------------

// The low thump under an explosion or a heavy gun: a sine dropping in pitch,
// which is the oldest trick in game audio and still the most legible one.
v[\boom] = { |from = 180, to = 32, dur = 0.7, curve = -4|
    var env = EnvGen.ar(Env.perc(0.004, dur, curve: curve));
    var freq = XLine.ar(from, to.max(8), dur * 0.9);
    SinOsc.ar(freq) * env;
};

// Body resonance - the "shell" a sound rings inside. Distinguishes a shot
// fired inside a metal turret from one fired in the open.
v[\shell] = { |freq = 320, dur = 0.25, decay = 0.2|
    var env = EnvGen.ar(Env.perc(0.001, dur, curve: -6));
    Ringz.ar(Impulse.ar(0), freq, decay) * env * 0.3;
};

// --- air --------------------------------------------------------------------

// Filtered noise sweeping down: rocket exhaust, a shell passing, wind.
v[\air] = { |from = 6000, to = 400, dur = 0.5, bw = 0.5|
    var env = EnvGen.ar(Env.perc(dur * 0.25, dur * 0.75, curve: -2));
    BPF.ar(PinkNoise.ar(1), XLine.ar(from, to, dur), bw) * env * 3;
};

// Scattered debris: rubble landing, shell casings, structure collapse. Density
// is hits per second.
v[\debris] = { |dur = 0.8, density = 60, freq = 2400|
    var env = EnvGen.ar(Env.perc(0.01, dur, curve: -3));
    var hits = Dust.ar(density);
    HPF.ar(Ringz.ar(hits, freq * LFNoise1.kr(8).range(0.6, 1.6), 0.03), 300) * env * 0.5;
};

// --- machinery --------------------------------------------------------------

// A running engine: a pulse train at the firing rate, coloured by roughness.
//
// Loops seamlessly only when rate * renderLength is a whole number, so that the
// file ends exactly on a cycle boundary. The grit is gated by the same pulse
// rather than running continuously: noise cannot be periodic, so the only way
// the loop point stays inaudible is for the noise to be near silent when the
// file wraps.
v[\motor] = { |rate = 26, roughness = 0.4, tone = 700|
    var pulse = LFPulse.ar(rate, 0, 0.22);
    var body = LPF.ar(pulse, tone);
    var grit = BPF.ar(WhiteNoise.ar(roughness), tone * 2, 0.7) * pulse;
    (body + grit) * 0.5;
};

// Rotor chop: the helicopter's defining sound, an amplitude gate over noise.
v[\rotor] = { |rate = 13, tone = 900|
    var chop = LFPulse.ar(rate, 0, 0.35).lag(0.004);
    var blade = BPF.ar(PinkNoise.ar(1), tone, 0.8) * 2;
    var thump = SinOsc.ar(rate * 2) * 0.35;
    (blade * chop) + (thump * chop);
};

// --- musical ----------------------------------------------------------------

// The one melodic voice the score is written for: a brass-ish pulse with a
// sharp attack, detuned against itself so a single line still sounds like a
// section rather than a beep.
v[\brass] = { |freq = 220, sus = 0.4, tone = 2200, level = 0.2, detune = 0.006|
    var env = EnvGen.ar(Env.new([0, 1, 0.75, 0], [0.012, sus * 0.5, sus * 0.5], [-2, -1, -3]));
    var voices = [freq, freq * (1 + detune), freq * (1 - detune)];
    var sig = Mix(Pulse.ar(voices, [0.5, 0.42, 0.58], 0.33));
    sig = LPF.ar(sig, tone * EnvGen.kr(Env.perc(0.01, sus, 1, -3)).linexp(0, 1, 0.5, 1.6));
    sig * env * level;
};

// The low pulse a march sits on.
v[\bass] = { |freq = 55, sus = 0.3, level = 0.3|
    var env = EnvGen.ar(Env.perc(0.006, sus, curve: -3));
    var sig = LPF.ar(Pulse.ar(freq, 0.4), freq * 6);
    sig * env * level;
};

// Military snare, near enough: filtered noise with a short tail.
v[\snare] = { |dur = 0.12, level = 0.25, freq = 1900|
    var env = EnvGen.ar(Env.perc(0.001, dur, curve: -6));
    var sig = BPF.ar(WhiteNoise.ar(1), freq, 0.9) * 2;
    (sig + (SinOsc.ar(190) * 0.4)) * env * level;
};

// The three musical voices behind one control, so a whole piece of music can be
// a single recipe. A recipe renders from exactly one SynthDef, so a march that
// wants brass over bass over a snare needs them selectable per note.
//
// Select.ar computes every branch and throws two away. That is wasteful in a
// live synth and completely free here, where rendering is offline and a few
// seconds of music take well under a second of CPU.
v[\ensemble] = { |voice = 0, freq = 220, sus = 0.4, level = 0.2, tone = 2200|
    Select.ar(voice.clip(0, 2), [
        v[\brass].(freq, sus, tone, level),
        v[\bass].(freq * 0.5, sus, level),
        v[\snare].(sus.min(0.2), level, tone)
    ]);
};

// --- helpers ----------------------------------------------------------------

// Equal-tempered frequency from a scale degree. Written out rather than using
// Scale/Tuning so the sounds do not depend on quark state.
v[\minorScale] = [0, 2, 3, 5, 7, 8, 10];

v[\degreeToFreq] = { |degree, root = 55, scale|
    var table = scale ? v[\minorScale];
    var octave = (degree / table.size).floor;
    var step = degree % table.size;
    root * (2 ** (octave + (table[step] / 12)));
};

// Turns [beat, degree, sus, level] rows into the [time, [args]] pairs the
// engine's `notes` field wants. Keeps a score readable as music.
v[\score] = { |bpm = 96, rows, root = 55, extra|
    var beat = 60 / bpm;
    rows.collect { |row|
        var args = [
            \freq, v[\degreeToFreq].(row[1], root),
            \sus, (row[2] ? 1) * beat,
            \level, row[3] ? 0.2,
            \lifetime, ((row[2] ? 1) * beat) + 1.5
        ];
        if(extra.notNil) { args = args ++ extra };
        [row[0] * beat, args];
    };
};

// Same, but for unpitched percussion rows of [beat, level].
v[\hits] = { |bpm = 96, rows, extra|
    var beat = 60 / bpm;
    rows.collect { |row|
        var args = [\level, row[1] ? 0.2, \lifetime, 1.0];
        if(extra.notNil) { args = args ++ extra };
        [row[0] * beat, args];
    };
};

v;
)
