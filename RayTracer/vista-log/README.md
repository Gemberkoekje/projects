# Vista slice logs

One file per finished slice: what was built, how it was checked and measured, the decisions taken in
code, the gotchas, the hand-off (where to start reading), the commands, the files changed, and what
the next slice should know. [`vista-plan.md`](../vista-plan.md) holds the design and the roadmap; each
slice's entry in its §10 keeps the goal, deliverable and verify, plus one status line that links here.

- **References** in code and scripts of the form "vista-plan §10 M1.x" resolve through that status
  line to the slice's log. Section references (§n) inside a log are to `vista-plan.md`.
- **M0 to M1.10 were moved verbatim** from the plan on 2026-10-21. In them, "below" and "above" mean
  within the same log, and "the cost ledger" is [`cost-ledger.md`](cost-ledger.md).
- **Sheets** a log cites are in [`../vista-views/sheets/`](../vista-views/sheets/README.md).
- **A new log** follows the layout of the existing ones: status, checks, decisions, gotchas, hand-off,
  commands, files, next (vista-plan §10, "Every slice").

| Log | Slice |
|---|---|
| [`M0.md`](M0.md) | Skeleton & seam |
| [`M1.1.md`](M1.1.md) | Contact sheets & loupe |
| [`M1.2.md`](M1.2.md) | Astronomy & place |
| [`M1.3.md`](M1.3.md) | Spectral atmosphere: sky & sunlight |
| [`M1.4.md`](M1.4.md) | Aerial perspective & altitude |
| [`M1.5.md`](M1.5.md) | Sun & sky importance sampling |
| [`M1.6.md`](M1.6.md) | OIDN |
| [`M1.7.md`](M1.7.md) | GPU erosion |
| [`M1.8.md`](M1.8.md) | Watch mode |
| [`M1.9.md`](M1.9.md) | View-dependent terrain mesh |
| [`M1.10.md`](M1.10.md) | Height-field intersection: the measured decision |
| [`M1.10a.md`](M1.10a.md) | Exposure & display |
| [`M1.11.md`](M1.11.md) | Linked sliders & the weather chain |
| [`M1.11a.md`](M1.11a.md) | Drainage |
| [`M1.11b.md`](M1.11b.md) | The display look |
| [`M1.11c.md`](M1.11c.md) | Output finish |
| [`M1.11d.md`](M1.11d.md) | Uplift & rock hardness |
| [`M1.12.md`](M1.12.md) | Materials by rule |
| [`M1.12a.md`](M1.12a.md) | Colour quadrature |
| [`M1.12b.md`](M1.12b.md) | Forest as material |
| [`M1.12c.md`](M1.12c.md) | The world's footing and folded strata |
| [`M1.13.md`](M1.13.md) | Snow & glaciers |
| [`M1.13a.md`](M1.13a.md) | Valley haze layer |
| [`M1.13b.md`](M1.13b.md) | Terrain-following valley mist |
| [`M1.14.md`](M1.14.md) | Auto camera v1 |
| [`cost-ledger.md`](cost-ledger.md) | The cost ledger: ns per pixel-sample per slice |
