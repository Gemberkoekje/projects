# Vista cost ledger

Moved verbatim from `vista-plan.md` §10 (M1 plan) on 2026-10-21. Every slice that changes the look or the
cost adds a row and its note here. Section references (§n) are to [`vista-plan.md`](../vista-plan.md).

*M1 cost ledger:* ns per pixel-sample (mean over dispatches) on the four M0 measurement views at
3840×2160, 64 spp, seed 42. The views are committed in `vista-views/` (M1.1);
`powershell -ExecutionPolicy Bypass -File vista-views/measure.ps1` renders them and prints the row.

| After | Sky only | Aerial, day | Default camera, day | Down a slope, golden hour |
|---|---|---|---|---|
| M0 | 0.24 | 0.70 | 0.88 | 1.32 |
| M1.1 | 0.24 | 0.73 | 0.87 | 1.31 |
| M1.2 | 0.24 | 0.68 | 0.83 | 1.26 |
| M1.3 | 0.22 | 2.18 | 1.49 | 2.30 |
| M1.4 | 0.22 | 2.63 | 2.10 | 3.40 |
| M1.5 | 0.22 | 2.81 | 2.17 | 3.62 |
| M1.6 | 0.22 | 2.81 | 2.17 | 3.62 |
| M1.7 | 0.17 | 3.62 | 2.20 | 3.94 |
| M1.8 | 0.17 | 3.62 | 2.20 | 3.94 |
| M1.9 | 0.26 | 4.04 | 2.80 | 5.56 |
| M1.10 | 0.26 | 4.04 | 2.80 | 5.56 |
| M1.10a | 0.26 | 4.04 | 2.80 | 5.56 |
| M1.11 | 0.27 | 4.02 | 2.78 | 5.67 |
| M1.11a | 0.22 | 4.04 | 2.69 | 6.44 |
| M1.11b | 0.22 | 4.04 | 2.69 | 6.44 |
| M1.11c | 0.22 | 4.04 | 2.69 | 6.44 |
| M1.11d | 0.26 | 3.79 | 2.63 | 5.82 |
| M1.12 | 0.25 | 5.64 | 4.08 | 8.15 |
| M1.12a | 0.25 | 5.63 | 4.08 | 8.17 |
| M1.12b | 0.25 | 5.87 | 4.32 | 8.65 |
| M1.12c | 0.31 | 6.12 | 5.39 | 9.56 |
| M1.13 | 0.45 | 6.75 | 5.75 | 10.90 |
| M1.13a | 0.45 | 7.03 | 5.94 | 11.30 |
| M1.13b | 0.44 | 6.95 | 5.84 | 20.79 |
| M1.14 | 0.44 | 6.95 | 5.84 | 20.79 |

M1.1 changes no pixel: the committed views trace exactly M0's rays per sample (1.00, 2.42, 2.29, 3.27),
so the M1.1 row shows the run-to-run timing noise, about ±4%. Differences smaller than that mean nothing.
M1.2 moves the sun but adds no work: a second run gave 0.24, 0.69, 0.84, 1.28, and the views trace 1.00,
2.42, 2.29 and 3.23 rays per sample (the low sun now casts other shadows on the down-slope view).
M1.3 traces the same rays per sample as M1.2 (1.00, 2.42, 2.29, 3.23); a second run gave 0.22, 2.17, 1.42 and
2.00. The 1.7–3× on terrain is the atmosphere's lookups: every sun shadow ray now reads the columns LUT (four
float4 loads and four exponentials), and every ray that leaves the terrain after a bounce reads two sky-view slices
(32 float2 loads for its four wavelengths). Camera rays read one slice, so the sky view got no dearer. The exposure
meter (a 320 px render, §M1.3 decisions) is not in these numbers; it costs about 1/150 of a 4K frame.
M1.4 traces the same rays per sample again; a second run gave 0.21, 2.55, 2.06 and 3.39. The +21%, +41% and +48% on terrain
is aerial perspective, the first real jump since M0: every segment that ends on the terrain marches its air (up to 8 columns
lookups and two 16-corner reads of the scattering orders for its four wavelengths). The sky view costs what it did.
M1.5 was measured on a GPU shared with a game, so its row is M1.4's times the ratio of this build to `main` (d8f31ea) rendered
alternately, view by view, three rounds each: medians 0.98, 1.067, 1.035 and 1.066 (`main` read 0.23, 2.66, 2.14 and 3.58 in the
two steady rounds; in the third the game's load changed and both builds sped up). The views trace 1.00, 2.63, 2.49 and 3.77 rays
per sample (M1.4: 1.00, 2.42, 2.29, 3.23): the sky's shadow ray at a path's first surface. A sample costs 3–7% more on terrain at
4K, against 7–22% in the 320 px comparisons of M1.5's checks; neighbouring pixels' sky lookups and shadow rays are likely more
coherent at 4K. Russian roulette from bounce 2 pays back 7–10% of the new cost.
For timings on a shared GPU, `measure.ps1` alone is no longer enough: alternate the builds and compare ratios.
M1.6 changes no pixel of the raw image and no line of the integrator or its host, so its row is M1.5's: five views (the four
ledger views and dawn, 640 px, 16 spp) rendered by `main` (b1dac45) and by this slice are byte-identical, PNG and EXR. Denoising
is a separate cost after the render, in M1.6's checks: 7.1 s on the CPU and 0.39 s on CUDA for a 4K frame.
M1.7 changes the world (eroded, and 32.8 km instead of 16.4), so the views trace other rays: 1.00, 2.81, 2.47 and 3.77 per sample
(M1.6: 1.00, 2.63, 2.49, 3.77). Measured as M1.5 was, on a GPU 37–40% busy with other programs: this slice and `main` (45109372)
rendered alternately, view by view, three rounds; median ratios 0.751, 1.288, 1.015 and 1.089 (0.745–0.870, 1.263–1.318,
0.980–1.034, 1.022–1.093). The aerial view's +29% is terrain where M1.6 showed the grid's edge (13% of its frame lies beyond the
grid now, 21.5% before); the down-slope view's +9% the eroded ground under a low sun. The sky view's −25% is not explained: it traces
one camera ray per sample on both builds, and the terrain's height range only picks the sky-view slice; it is 0.06 ns a sample.
M1.8 changes no pixel and no line of the integrator, so its row is M1.7's: the four ledger views and a golden-hour view with every
debug output (640 px, 16 spp) rendered by `main` (b96f2e57) and by this slice are byte-identical, PNG and EXR, sixteen files.
M1.9 changes the geometry (the view-dependent mesh with its far field and detail, bent onto the Earth) and the shading (normal maps).
Measured as M1.5 and M1.7 were, on a GPU 16–34% busy with other programs: this slice and `main` (04bd3154) rendered alternately, view
by view, three rounds; median ratios 1.508, 1.117, 1.271 and 1.411 (1.495–1.538, 1.116–1.117, 1.270–1.272, 1.405–1.423). The views trace
1.00, 3.07, 2.51 and 3.89 rays per sample (M1.8: 1.00, 2.81, 2.47, 3.77): the far field turns rays that left past the grid's edge into
terrain. The normal maps are 5% of it (the same build with and without them, alternately: 1.00, 1.06, 1.05, 1.05); the rest is the far
field and, down the slope, the 3.7 M triangles of detail near the camera. The sky view's +0.1 ns is its camera rays crossing a BLAS whose
root now spans the far field.
M1.10 changes nothing on `main` but this plan (the height field stays on its branch), so its row is M1.9's. On the branch the mesh path
is byte-identical to `main` (the aerial view with every debug output and the down-slope view, 640 px, PNG and EXR).
M1.10a changes the PNG only (its exposure and display curve), so its row is M1.10's: the four ledger views and the golden-hour ridges
rendered by `main` (db9dcae4) and by this slice (480 px, 64 spp) have byte-identical EXRs, and `main`'s PNGs equal this slice's
`--meter average --display aces`. The integrator's `CSMain` is unchanged; the incident meter is a kernel of its own, about 1 ms of GPU a
view, where the average meter rendered the frame again at 320 px.
M1.11 moves the golden-hour sun (8.5° to 2.3°: the down-slope view) and the haze (optical depth 0.100 to 0.119 on all four views: Zermatt's
July humidity). Measured as M1.9 was, on a GPU 36–72% busy with other programs: this slice and `main` (a65a87af) rendered alternately, view
by view, three rounds; median ratios 1.034, 0.996, 0.992 and 1.019 (1.012–1.057, 0.994–1.033, 0.990–0.995, 1.017–1.024), within the
run-to-run noise but for the down-slope view's +2%. The views trace 1.00, 3.07, 2.51 and 3.80 rays per sample (M1.10a: 1.00, 3.07, 2.51,
3.89; the lower sun puts more of the slope in shadow). Why that view costs 2% more a sample with fewer rays was not measured. The haze
should cost nothing: its optical depth is a number in the LUTs, and the day views' ratios are 1.00 within the noise.
M1.11a changes the world under every view (the stream-power incision and the pool filling) and nothing in the integrator. Measured as M1.11
was, on a GPU 25–40% busy with other programs: this slice and `main` (cbeadbe2) rendered alternately, view by view, three rounds; median
ratios 0.817, 1.004, 0.966 and 1.136 (0.775–0.836, 1.003–1.004, 0.966–0.974, 1.134–1.140). The views trace 1.00, 3.16, 2.37 and 3.68 rays per
sample (M1.11: 1.00, 3.07, 2.51, 3.80). The default and down-slope views now stand 532 and 552 m above a valley floor instead of near the
ground: the default view sees more sky and costs 3% less; down the slope the frame is a deep valley in shadow and the range beyond it, fewer
rays per sample but 14% more time per sample, likely the longer rays across the valley through more of the mesh (not measured). The sky
view's −18% is unexplained, as M1.7's −25% was: it traces one camera ray per sample on both builds (0.05 ns a sample).
M1.11b changes the PNG only (AgX's punchy look and the incident meter's +1 EV), so its row is M1.11a's: the four ledger views rendered
by `main` (bfa57bfc) and by this slice (480 px, 64 spp) have byte-identical EXRs, and `main`'s PNGs equal this slice's `--look base
--compensation 0`. Nothing on the GPU changed.
M1.11c also changes the PNG only (the punchy look's saturation held back where it clips, the dither, the 16-bit option), so its row is
M1.11b's: the six views of its checks rendered by `main` (7b0ffbe9) and by this slice (480 px, 64 spp) have byte-identical EXRs, and
`main`'s `--look base` PNGs equal this slice's `--look base --dither off`. The finish runs on the CPU when the files are written.
M1.11d moves the terrain (the uplift map and the rock's strata) and three of the four views: the default and down-slope views stand
3 m above rock again (re-placed on the new world), and the sky view moved to the default's spot (M0's spot is underground now). So
only the aerial view measures the same pose; **the default and down-slope rows before and after M1.11d are not comparable.** The row
is M1.11c's scaled by this slice against `main` (1a741723, with its own cameras and world), rendered alternately, view by view, three
rounds, on a GPU shared with other programs: median ratios 1.170, 0.938, 0.977 and 0.903 (1.166–1.175, 0.917–0.943, 0.972–0.978,
0.899–0.908). The views trace 1.00, 3.03, 2.60 and 3.69 rays per sample (M1.11c: 1.00, 3.16, 2.37, 3.68). Over the new world the aerial
view traces 4% fewer rays per sample and costs 6% less. The sky view's +17% is likely its new height: its rays still all escape (one per
sample) but now start 3 m above the ground, among the terrain's finest triangles, where before they started 550 m up (not measured).
Making a world costs what it did (the incision 13–14 s, the GPU erosion 19–21 s, the maps 8–9 s; `vista-log/M1.11d.md`).
M1.12 gives the ground its materials, evaluated at every surface a path meets: the rules' inputs (four samples of the material
map, two entries of the bed table, the world grid's normal), five noises and five spectra, and on the camera's hit the bump. Measured
as M1.11d was: this slice and `main` (29ac138a) rendered alternately, view by view, three rounds, on a GPU shared with other programs:
median ratios 0.966, 1.489, 1.551 and 1.401 (0.964–1.125, 1.477–1.490, 1.549–1.557, 1.387–1.401). The views trace 1.00, 3.03, 2.60
and 3.62 rays per sample (M1.11d: 1.00, 3.03, 2.60, 3.69): down the slope the rough ground's lower ρ·A ends a few more paths by
Russian roulette. The sky view hits no ground, so its ratio is noise. Where the time goes, from two more rounds of the sky, aerial and
default views against `main`: this build with `--materials off` (the same kernel, M0's single material) 1.00, 1.03 and 1.02; with
`--diffuse lambert` 0.99, 1.47 and 1.58; as built 0.99, 1.48 and 1.57. The larger kernel costs 2–3% and the rough BRDF nothing
measurable; the materials' evaluation costs 45–55% a sample. If that matters, the levers are fewer reads and noises per hit (the
codes, the bed's entry and the normal packed into one read; the sharpening's and the meadow's noises folded into the breakup's),
with the self-test's parity probe keeping C# and the shader in step.
M1.12a changes only the spectral tables (Vista's own 50 wavelengths and their weights; vista-log/M1.12a.md): the shader and the rays
are the same. Measured as M1.12 was: this slice and `main` (3f75fcff) rendered alternately, view by view, three rounds, on a GPU shared
with other programs: median ratios 1.012, 0.998, 0.999 and 1.003 (1.001–1.154, 0.995–1.008, 0.961–1.001, 0.855–1.004; the first
round's spread was the other load settling), at the same rays per sample (1.00, 3.03, 2.60, 3.62). The row is M1.12's times those
medians: no change beyond the noise.
M1.12b adds the forest to the ground's rules: a third noise per hit (the forest's 250 m stands and clearings), the forest's rule
and, where it grows, its colour, its tone of stands and its canopy's kernels at every light the surface samples. Measured as M1.12a
was: this slice and `main` (51b32b06) rendered alternately, view by view, three rounds, on a GPU shared with other programs (desktop
programs only, 20% busy): median ratios 0.999, 1.043, 1.059 and 1.059 (0.976–1.034, 1.030–1.055, 1.044–1.059, 1.037–1.074), at the
same rays per sample (1.00, 3.03, 2.60, 3.62). The row is M1.12a's times those medians. These views, at Zermatt's valley floor, hold
little forest (3% of the world), so the 4–6% is mostly the third noise and the larger surface every hit pays; `--forest off` skips the
noise (not timed). Measured before the forest's thresholds were tuned (vista-log/M1.12b.md), which moved only constants.
M1.12c changes the world under three of the four views and the altitude it stands at: the folded strata erode another seed-42 world,
the sky, default and down-slope views are re-placed on it (M1.11d's spots are underground), and the anchor puts its height 0 at
912 m instead of 1608 m, so the valleys in view are forest. Per hit the materials read a third word of the map and massive rock's
hardness. Measured as M1.12b was: this slice and `main` (065e2c67) rendered alternately, view by view, three rounds, on a GPU
shared with desktop programs (about 30% busy): median ratios 1.227, 1.043, 1.248 and 1.105 (1.189–1.232, 1.036–1.053,
1.230–1.255, 1.017–1.107), at 1.00, 3.09, 2.64 and 3.44 rays per sample against `main`'s 1.00, 3.03, 2.39 and 3.43. The row is
M1.12b's times those medians. Only the aerial view keeps its pose: +4% for 2% more rays and the forest now in its valleys. The
default view's +25% is a new pose with 10% more rays and forest in frame; the sky view's +23% the same new spot, its camera rays
now starting 3 m above other ground (as M1.11d's +17%); the per-hit cost of the massive weight was not separated.
M1.13 carves the world's valleys with glaciers, lays today's glaciers on it and lets the season's snow lie by rule: every hit
evaluates the snow, firn and ice rules (two noises more, the ice map's word) and, where they lie, their spectra and crevasses. The
default and sky views are raised on the re-carved ground (the default 6 m above a meadow), and the down-slope view re-placed
(vista-log/M1.13.md). Measured as M1.12c was: this slice and `main` (844c8307) rendered alternately, view by view, three rounds,
`main` at its own cameras, on a GPU shared with desktop programs: median ratios 1.457, 1.103, 1.067 and 1.140 (1.430–1.572,
1.084–1.132, 1.057–1.075, 1.114–1.153), at 1.00, 3.12, 2.53 and 3.43 rays per sample against `main`'s 1.00, 3.09, 2.64 and 3.44.
The row is M1.12c's times those medians. The aerial view keeps its pose: +10% for the rules every hit pays, with 1% more rays (the
snow's high albedo keeps a few more paths alive). The default's +7% is a new spot with 4% fewer rays; down the slope +14% a new pose;
the sky view's +46% is 0.1 ns a sample at its raised spot, where camera rays start over other ground.
M1.13a adds the valley haze to the air and grows the multiple-scattering tables (ten orders, 24 heights, 12 view-to-sun texels:
2.25× their memory); every step of the aerial march finds one altitude more and, with a layer, its means. Measured as M1.13 was:
this slice and `main` (080a7918) rendered alternately, view by view, three rounds, on a GPU shared with desktop programs: median
ratios 0.997, 1.042, 1.033 and 1.037 (0.936–1.008, 0.962–1.051, 1.012–1.044, 1.034–1.049), at the same rays per sample (1.00,
3.12, 2.53 and 3.43). The row is M1.13's times those medians. These views at their own hours hold almost no valley haze (a July
noon; the golden hour's forming pool), so the 3–4% on terrain is the larger tables' lookups and the march's extra altitude, not
separated. With a strong layer pinned (6 km under the lid) the same views ran 0.992, 1.054, 1.027 and 1.044 times `main`, at
1.00, 3.21, 2.59 and 3.45 rays per sample (a hazier valley keeps a few more paths alive): the layer itself costs nothing more. The
LUT build is not in these numbers: 3.7–4.0 s per atmosphere instead of 1.0.
M1.13b marches each valley's pool and mist in three dimensions below the pools' highest reach, through a sunlight volume built once
a render, in an integrator variant compiled with the pools' code (a render without pools runs M1.13a's). Measured as M1.13a was:
this slice and `main` (c04d2275) rendered alternately, view by view, three rounds, this slice on half the GPU (`VISTA_GPU_SHARE`,
its GPU times unchanged by it: the aerial view 9.88 ns capped, 9.98 uncapped, before the variant split) and `main` at full speed,
on a GPU shared with desktop programs: median ratios 0.971, 0.989, 0.983 and 1.840 (0.970–1.002, 0.989–1.002, 0.983–0.983,
1.837–1.896; the down-slope view measured again, three rounds, once the sunlight volume reached the pools' whole reach), at the same rays per sample (1.00, 3.12, 2.53 and 3.43). The row is M1.13a's times those medians. The sky, aerial and
default views at their own hours hold no pool and run the plain integrator. The down-slope view's golden hour holds pools of haze
(no mist) over 20% of the world, up to 165 m deep: 1.84× for the pools' variant, most of it the variant's own registers and
lookups (with one step a segment it ran 20.1 ns against the flat layer's 10.5). The misty ridges at 07:30 with a strong pool and
mist pinned (`--valley-haze 6 --mist 0.4`, 1920×1080, 64 samples) ran 3.13× M1.13a's flat layer for the same air (22.5 against
7.2 ns, 3.10–3.15 again at the end; 2.8× before the march's steps were tuned, 1.98× against the flat layer when it still ran in the heavier shader): over the
plan's two (vista-log/M1.13b.md).
M1.14 adds the auto camera and changes nothing in the integrator or its host, so its row is M1.13b's: the four ledger views (640 px,
16 spp, the down-slope view with every debug output) rendered by `main` (0cc07311) and by this slice are byte-identical, EXR and PNG. The
auto camera's own cost is a search before the render, about 35–45 s a pick: the plant 2.5–3.2 s and the candidates' buffers 0.6–1.5 s
on the CPU, ten finalists rendered at 192×108 (24–32 s, the GPU at half its share) and the pick's 4K mesh counted (2–7 s)
(vista-log/M1.14.md).
