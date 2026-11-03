# Vista M1a.3a — Ice caps that leave their flanks bare

The slice's goal, deliverable and verify are in [`vista-plan.md`](../vista-plan.md) §10 (M1a plan, M1a.3a), with a status line that
links here. Section references (§n) are to the plan. Added on 2026-11-03 at the user's call after M1a.3 ("ice caps first"), ahead of
M1a.4.

*M1a.3a status (2026-11-03): done.* **The pale domes on the hero's lit crest are gone: today's ice thins out below the ice that
feeds it.** The ice solve (M1.13) smooths a glacier's surface over five 64 m cells and fills every cell around its iced ones up to
that surface. Across a valley that fill reaches the walls, as it should; below a cap on a summit it filled the flanks up to the cap's
own level. On the lit crest, coarse cells that no ice flows through held 68–144 m of ice, 150–200 m below the summit, whose own cap was
a few metres to tens of metres thick: smooth pale domes hanging down the flanks. Now a cell no ice flows through holds, below the beds
of the iced cells it is averaged with, at most their thickness less how far below them it lies; walls and flat glacier floors fill
as before. The lit crest's crop: ice over 5 m on 16.9% → 1.2% of it; the crest reads as rock with snow on its top. World-wide today's
glaciers shrink from 7.6% to 5.8% of the grid (3.36 → 2.21 km³): the ice above the line stays, the tongues and margins below it lose
the fill that ran downhill past where their flux ends. The carving's glaciers are unchanged, so the eroded ground is M1a.3's bit for
bit (`d00391ab69d1e932`).

*What was built:*

- **The thin-out** (`GlacierFlowSettings.ThinsDownhill`, on in `GlacierFlowSettings.Present`; `GlacierFlow.Solve`'s smoothing step):
  beside the averaged surface, the step averages the iced cells' beds and thicknesses with the same tent; a cell with no flux of its
  own whose bed lies below their mean holds at most their mean thickness less the depth it lies below them. Only the cells no ice
  flows through change, and only below their iced neighbours, so a valley's walls (above the flux line) and the snout's gentle floor
  (a few metres below it) keep their ice.
- **The setting** (`ErosionSettings.GlacierThinning`, the mountains' 1; `Glaciers.Lay` solves today's ice with `Present` when it is
  on): in the cache key only when on and the world has ice, so M1a.3's worlds keep their key and render again with
  `GlacierThinning=0`. The carving (`StreamPower`) keeps `GlacierFlowSettings.Default`, M1.13's flow: changing it would change every
  carved valley (parked, "The carving's glaciers fill below their caps too").
- **Tests** (`GlacierTests`): a summit 20 m over the line on a 19° cone: M1.13's flow fills its flanks with over 50 m of ice no flux
  feeds, today's holds under 10 m there and keeps the summit's cap; the valley glacier still fills its valley across five cells or
  more with a surface flatter than its bed, under both flows.
- **`vista-views/ground/render.ps1 -Sheets m1a3a`**: M1a.3's world (with M1a.3's ice) and M1a.3a's, each made and measured again,
  the hero, the lit loupe, the main massif's glaciers from above, the aerial view, and the four sheets.

*The cause* (the cached shipped world of M1a.3, its ice laid again on the CPU and stored in a scratch cache for the loupes; the lit
crest's 768 m square):

| | M1a.3 | No fill below the iced cells | Fill as thick as the source | **Thin out within one thickness (shipped)** |
|---|---|---|---|---|
| the lit crest | pale domes | rock, a small snow patch on top | a smooth pale blanket over the crest | rock, a small snow patch on top |
| today's ice (km³; share of the grid) | 3.36; 7.97% | 2.08; 5.94% | 2.47; 7.34% | 2.23; 6.18% |
| ice with its bed above the line / 0–200 m below / 200–500 m below (km³) | 0.52 / 1.52 / 1.08 | 0.53 / 0.87 / 0.56 | 0.53 / 1.04 / 0.73 | 0.53 / 0.89 / 0.63 |

The crest's highest 64 m sample stands 7 cm over the line (vista-log/M1a.3.md); its cap's flux is a few hundred cubic metres a year,
and the shallow-ice thickness of that (the flux to the fifth root) is a few metres to tens of metres on the summit's own cells. The
domes were the fill around it: cells with no flux of their own filled to the averaged surface of their iced neighbours, whatever
their bed. On cones (`GlacierTests`, and a sweep of three summit heights and two slopes): M1.13's flow leaves 56–128 cells with no
flux under ice, up to 79–136 m of it (at a 31° slope once a bare summit beside 136 m of ice on its flanks); the shipped flow at most
4 cells and 7 m. "No fill below" cleared the crest too, but cut cells a metre below their neighbours on a flat glacier floor, ragged
margins and holes; "as thick as the source" kept the flanks under the cap's thickness, which still read as a blanket.

*Checks:*

- **The eroded world** (`--erosion-measure`, seed 42, 4097² × 8 m; `measure-m1a3.log` and `measure-m1a3a.log` of the render script):
  the erosion and its hash unchanged (`d00391ab69d1e932`; with the ice, `a54d80542f2b79e7`); today's glaciers 7.6% → 5.8% of the grid,
  3.36 → 2.21 km³, thickest 207 → 213 m; at Zermatt ice 4.9% → 3.2%, firn 2.4% → 2.3%, rock 45.9% → 46.8%, meadow 11.1% → 11.7%.
- **The places** (the crops' ice over 5 m, M1a.3 → M1a.3a): the lit middle range 16.9% → 1.2%; the rows of dots 0% → 0%; the near
  face's 768 m square 13.0% → 3.4%.
- **The sheets**: the lit loupe (`m1a-3a-lit.png`): the domes gone, the crest's rock and its snow; the hero (`m1a-3a-hero.png`): the
  lit middle range without its pale shapes; from above (`m1a-3a-ice.png`): the main massif's glacier network kept, its margins and
  snouts shorter; the aerial view (`m1a-3a-aerial.png`): the valley glaciers almost unchanged under July's snow.
- **Tests** (Release, the whole solution: Vista.Tests 968, two more; RayTracer.Tests 336, Pinball.Tests 55).
- **The GPU**: `--gpu-selftest` passes (90 checks; the cached shipped world, its ice laid by this build, re-erodes to the same bits);
  22 committed views above the eroded ground, the lowest 2.2 m.
- **The goldens** (`--golden-check`): the same eight of ten differ from M1.17's as at M1a.3, by more where ice is in the view: aerial
  −4.6% mean luminance (M1a.3 −1.9%), default −3.5% (+0.2%), hero −1.2% (+0.02%), alpenglow at dusk −0.5% (+0.02%) and dawn +1.3%
  (+1.3%), April's slopes −4.8% (−3.9%), the misty ridges −0.3% (−0.4%), down-slope +0.01% (its blocks 73% rms); the sky and twilight
  within tolerance. Re-baselined at M1a.7.
- **The cost ledger** (`vista-views/measure.ps1`): M1a.3's ice and M1a.3a's timed in turns, two rounds each, with other work holding
  22–26% of the GPU: the ratios 0.98 / 1.02 / 0.96 / 1.00 (sky, aerial, default, down-slope), the trace's cost unchanged within the
  noise; M1a.3's row scaled by them, 0.25 / 5.85 / 3.96 / 15.55 ns a pixel-sample.

*Decisions (revisit freely):*

- **The fill, not the cap**: the cap itself (a few metres to tens of metres of ice on a summit at the line) is what the shallow-ice
  balance gives and is left as it is; what read as blobs was the ice around it no flux fed.
- **Thin out within one thickness**, of the three: the only one that leaves the crest's rock and keeps a flat glacier floor whole.
- **The line stays at 1800 m above height 0** though the glaciers shrink by a quarter: the fill that ran downhill past the tongues'
  flux was the same artefact as the domes, and the line is already low for the Alps (2761 m at Zermatt against about 3000 m); lowering
  it to win the area back would cover one artefact with another. The user judges the glaciers' size from above (`m1a-3a-ice.png`).
- **Today's glaciers only**: the carving keeps M1.13's flow, so the world's shape is M1a.3's and no carved valley moves (parked).
- **A setting in the key, not a new passes version**: M1a.3's worlds stay in the cache and render again, as earlier slices kept
  theirs.

*Gotchas:*

- **The laid ice comes from the cache**: today's glaciers are laid once, after the erosion, and cached with the world, so a change to
  `Glaciers.Lay` reads stale ice unless the key changes (here the setting's). The loop of this slice laid the ice again on the CPU
  from a cached world (its bed is the surface less its ice) and stored it in a scratch `VISTA_WORLD_CACHE`: seconds a variant, against
  five minutes for a measure.
- **No fill below the iced cells cuts holes on flat floors**: a cell a metre below its neighbours' mean bed is "below" them; the
  thin-out's tolerance is the ice's own thickness.

**M1a.3a hand-off.** To understand this, start with `vista-views/sheets/m1a-3a-lit.png` (the domes, then the crest's rock) and
`m1a-3a-ice.png` (the glaciers from above), then `Vista.Core/World/GlacierFlow.cs` (`Solve`'s smoothing step, `ThinsDownhill`) and
`Glaciers.Lay`. Today's ice is still one smooth surface on the 64 m solve: cornices, crevasses, seracs and the caps' close-up surface
are M7's (parked).

*Commands (from `RayTracer/`, after `dotnet build Vista.App -c Release`):*

```
powershell -File vista-views/ground/render.ps1 -Sheets m1a3a [-Out <dir>]
dotnet Vista.App/bin/Release/net10.0-windows/Vista.App.dll --erosion-measure --recipe vista-views/autocam/range-golden.recipe.json --seeds 42 --out <dir> --crops-camera vista-views/signoff/hero.camera.json --crops "2000,850;1300,1250;3520,1680" [--set GlacierThinning=0]
dotnet Vista.App/bin/Release/net10.0-windows/Vista.App.dll --golden-check
powershell -File vista-views/measure.ps1
```

M1a.3's ice for comparison: `$env:VISTA_EROSION_SETTINGS = 'GlacierThinning=0'`.

*Files:*

- Changed: `Vista.Core/World/GlacierFlow.cs` (`ThinsDownhill`, `Present`, the smoothing step), `Glaciers.cs` (today's flow),
  `ErosionSettings.cs` (`GlacierThinning`, the key, the remarks); `Vista.Tests/GlacierTests.cs` (the cap test, the valley test under
  both flows); `vista-views/ground/render.ps1` (`-Sheets m1a3a`); `vista-plan.md` (M1a.3a's entry and status, the parked items),
  `vista-log/M1a.3.md` (its next), `vista-log/cost-ledger.md`, the READMEs.
- New: `vista-log/M1a.3a.md`, `vista-views/sheets/m1a-3a-lit.png`, `m1a-3a-hero.png`, `m1a-3a-ice.png`, `m1a-3a-aerial.png`.

*Next:* M1a.4 — rock below the grid (vista-plan §10, M1a plan).

*Judge this:* `m1a-3a-lit.png`, the lit middle range: M1a.3's pale domes above, M1a.3a below; then `m1a-3a-hero.png` and
`m1a-3a-ice.png` (the glaciers from above). *Ignore this:* the rock's own texture and the near faces' smooth look (M1a.4); the
light and dark bands on the rock (M1a.5); the meadows (M1a.6); the ice's own surface up close, cornices and crevasses (M7).
*Questions:* In the bottom loupe, does the crest read as a rocky ridge with snow on it? From above, do the glaciers on the right
look too small beside the left, or about right for mountains like these? Does the hero's lit range look better without the pale
shapes?
