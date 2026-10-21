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
