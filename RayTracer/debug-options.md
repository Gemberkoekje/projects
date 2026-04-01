# Renderer Debug Options Plan

This file captures an actionable plan to raise the engine to "professional-grade" debug screenshots and hotkeys. It focuses on a small set of high-impact visual diagnostics, and on making them easy to capture with consistent screenshots.

Summary of user request (implemented here):
- Add detailed debug screens for variance split, current vs accumulated differences, clamp heatmaps, bounce breakdowns, history age, reuse heatmaps, and edge disagreement maps.
- Move the Beauty screen to `F1`, use `F2`..`F8` for debug screens, and use `F9` to globally enable and `F10` to globally disable the debug overlay.

---

## Keybindings (global)

- `F1`: Beauty (final render)
- `F2`: Indirect variance vs Direct variance
- `F3`: Current frame vs Accumulated difference (abs diff)
- `F4`: Clamp heatmap
- `F5`: Ray contribution / bounce breakdown
- `F6`: History age heatmap
- `F7`: Cache / ray reuse heatmap
- `F8`: Edge disagreement map
- `F9`: Toggle debug overlay ON
- `F10`: Toggle debug overlay OFF

Notes:
- When the global overlay is OFF, `F1` should still show the beauty view for quick checks.
- Also allow numeric keys `1`..`8` to select the same pages when overlay is active.

---

## Screens (short descriptions)

1) Indirect variance vs Direct variance (F2)
- Split variance into contributions from bounce 0 (direct) and bounce >=1 (indirect).
- Visualize as two channels or as a composite (e.g., red=indirect, green=direct). Include legend and numeric min/max.

2) Current frame vs Accumulated difference (F3)
- Visualize `abs(current_sample - accumulated_history)` per pixel (luminance or max-RGB). Bright pixels show strong disagreement and temporal accumulation failures.

3) Clamp heatmap (F4)
- Show where clamping occurs and its severity (per-pixel fraction or intensity). Black = none; orange/red = clamp severity.

4) Ray contribution / Bounce breakdown (F5)
- Per-pixel energy split by bounce tiers (bounce 0, bounce 1, bounce 2+). Support stacked or RGB-channel visualizations and a numeric picker.

5) History age (F6)
- Visualize last-updated age per pixel (frames or seconds). Blue = fresh, yellow = medium, red = stale.

6) Cache reuse / Ray reuse heatmap (F7)
- Visualize whether a sample reused cached data (green) vs produced a new ray (red). Intensity encodes reuse strength or fraction.

7) Edge disagreement map (F8)
- Compute neighbor differences (color/depth/normal) to highlight reprojection errors, edge-smearing, and history bleeding.

---

## UI / UX

- 2x2 compositor remains useful; prioritize single-panel fullscreen debug pages keyed to F1..F8 for quick screenshots.
- Always render a compact HUD with: preset, resolution, fps/rays/sec, spp/job, frame index, derived metrics (rejected %, clamped %), and the active debug legend and numeric range.
- Per-pixel picker: clicking a pixel shows exact numeric breakdown for that screen.

---

## Implementation notes (high level)

- Instrument accumulation pipeline to record per-sample metadata: bounce index, clamp flag/magnitude, reuse flag, sample energy, timestamp/frame id.
- Maintain light per-pixel aggregates: running sums, sums-of-squares split by category, counters for clamp/reuse, last-updated frame id.
- For cost-sensitive views, compute on a decimated grid or at reduced resolution and upsample for display.
- Keep debug modes off by default. Expose an "update rate" and allow per-mode sampling frequency to limit overhead.

---

## Prioritization (impact / effort)

1. `F3` (Current vs Accumulated diff): High impact, low effort — ship first.
2. `F4` (Clamp heatmap): High impact, low effort if clamp flags exist.
3. `F2` (Direct vs Indirect variance): High impact, medium effort — requires splitting variance accumulation.
4. `F5` (Bounce breakdown): High impact, medium effort.
5. `F8` (Edge disagreement): Medium impact, low effort using depth/normal buffers.
6. `F6` (History age): Medium impact, low effort if frameId tracked.
7. `F7` (Cache reuse): High impact, high effort — depends on ray-reuse instrumentation.

---

## Repo-specific pointers (where to change)

- `RayTracer.Core/JobSystem.cs` — add per-pixel debug buffers and aggregation, extend `Trace()` and resolve to optionally emit diagnostic buffers.
- `RayTracer.Core/Accumulation` (or equivalent) — track sample metadata and update RNG split accumulators.
- `RayTracer/Program.cs` / `RayForm` — add hotkey handling for `F1..F10`, overlay toggle, and compositor for debug pages.
- `RayTracer/CalibrationForm.cs` — optional: expose default debug presets in calibration UI.
- `RayTracer.Tests` — add deterministic tests for variance/accumulation bookkeeping and clamp counters.

---

## Next steps (concrete)

1. Add this plan to the repo (done).
2. Instrument accumulation to expose `abs(current - history)` and per-pixel clamp flags (implement for `F3` and `F4`).
3. Add HUD and simple hotkey handling to switch pages and toggle overlay (`F1..F10`).
4. Iterate on `F2` (variance split) and `F5` (bounce breakdown).

---

## Recent implementation notes

- Bounce breakdown (F5) implemented (approximate):
  - Added per-pixel accumulators for `bounce0`, `bounce1`, and `bounce2+` in the renderer.
  - Implemented a lightweight multi-bounce estimator: a stochastic secondary sample (one-bounce) plus an optional tertiary sample to populate the `bounce2+` channel. This keeps cost modest while providing a useful visual split.
  - The UI exposes `F5` pages for `Bounce0`, `Bounce1`, `Bounce2+`, and an `BounceRGB` composite (B=bounce0, G=bounce1, R=bounce2+).
  - Caveat: this is an approximate diagnostic, not a fully unbiased per-bounce energy decomposition. For exact accounting the tracer must record each sample's per-bounce contributions in a true path-trace loop.

## Next immediate tasks (I will continue executing)
## Worklog — implemented vs remaining

1. Implement an explicit Edge Disagreement map (F8): compute neighbor differences using filtered color, depth, and normal buffers and display a combined disagreement heatmap to surface reprojection/historic bleeding.
2. Add HUD legend entries for the bounce pages and the Edge Disagreement map (numeric ranges and short descriptions).
3. Optionally make the tertiary-sampling (bounce2+) toggleable or decimated to trade accuracy vs performance.
---
## Quick guidance — which screen to use when

Use this cheat-sheet when capturing diagnostics. Each entry maps common rendering problems to the most useful debug screens (keys in parentheses).

- Ghosting / reprojection errors: `F8` Edge Disagreement, the `ReprojectedVsCurrentDiff` panel, and `HistoryAge` (`F6`). These highlight reprojection mismatches and stale history that cause ghosting.
- Temporal flicker or history bleeding: `F3` Current vs Accumulated Diff, `HistoryAge` (`F6`), and `RejectionMask`. Bright spots indicate disagreement between the new sample and accumulated history or rejected history regions.
- Image too noisy (grainy): `F2` Variance / `VarianceSplit` (direct vs indirect) and `SampleCount`. Use these to locate under-sampled regions and to determine whether noise is coming from direct or indirect lighting.
- Fireflies / outlier pixels: `F4` Clamp Heatmap and `EmissiveLighting`. Clamp heatmap shows where clamping suppressed extreme contributions.
- Missing or incorrect indirect lighting: `F5` Bounce breakdown (`Bounce0`, `Bounce1`, `Bounce2+` and `BounceRGB`) and `IndirectLighting`. These reveal which bounce tiers contribute energy.
- Depth or normal discontinuities (edge artifacts): `Depth`, `Normal`, and `F8` Edge Disagreement. These highlight geometry mismatches and reprojection edges that often cause temporal artifacts.
- Cache / reuse problems (when available): `F7` Reuse / Ray-Reuse heatmap and `RejectionMask` — shows where cached data is reused versus new rays generated.
- Performance hotspots / low effective SPP: `SampleCount` and HUD SPP/job metrics — identify regions with low sampling and adjust trace parameters.

Tip: use the pixel picker to read numeric values (per-bounce energies, age, clamp amount) for problem pixels and combine with HUD screenshots for reproducible bug reports.


I'll proceed to add the Edge Disagreement debug mode and wire it into the UI so F8 shows a combined disagreement heatmap.

If you want, I can implement the following first code changes in the repo now:
---
- Add a small `DebugMode` enum and hotkey handlers in `RayTracer/Program.cs` (or `RayForm`) to wire `F1..F10`.
- Add placeholder debug-buffer exports in `RayTracer.Core.JobSystem` so the UI can read them.
