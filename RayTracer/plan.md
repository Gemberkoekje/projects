# RayTracer — Plan

## Completed work

- Decomposed `JobSystem` into `TileScheduler`, `PathTracer`, `TaaResolver`, `DisplayResolver`, `AccumulationBuffer`, `DebugBufferRenderer`; introduced `RenderBuffers` and `JobSystemFactory`.
- Reduced heap allocations, added `FrameDiagnostics`, Span/Inlining improvements.
- Tightened APIs (option records, constructor guards), CI configured.
- 3D Diffuse Irradiance Cache (Parts A1–A6) — all done.
- Slim `JobSystem.cs` refactor (Parts B1–B4, B6) — done; B5 (alias cleanup) partially complete.

---

## Part C — Slideshow-style camera: long stills, fast moves, instant wall turns

### Goal

Replace the current smooth continuous camera with a **slideshow-style** approach:

1. **Long still images** — the camera holds position for several seconds, allowing the path tracer to accumulate many samples and converge to a clean, noise-free image. No history erasal during stills.
2. **Fast movement** — when the camera does move between cells, it moves quickly (~1s). The image may be slightly grainy during the transition; it will smooth out once the camera stops.
3. **Instant wall turns** — when the next action is a turn (camera would be staring at a wall), skip the slerp animation entirely and snap the rotation immediately. No need for a close-up of a wall.

### Design

**File:** `RayTracer.Core\Pipeline\CameraController.cs`

Introduce a new `State.Still` phase:

| State | Duration | Behaviour |
|---|---|---|
| `Still` | `StillTime` (default ~6s) | Camera doesn't move. `Dirty` stays false so accumulation proceeds uninterrupted. |
| `Moving` | `MoveTime` (reduce to ~1.0s) | Camera lerps between cell centres. `Dirty = true` each frame → soft reset / capped samples. |
| `Turning` | **0s (instant)** | When a turn is needed, snap rotation immediately (no slerp). Then enter `Still` to accumulate the new view. |

### Steps

- [x] **C1 — Add `State.Still` and `StillTime` property**
  - Add `StillTime` property (default `6.0f`).
  - Add `State.Still` enum value.
  - After completing a move, enter `Still` instead of immediately starting the next action.
  - During `Still`, increment `_t` but do NOT set `Dirty` — camera is stationary.
  - When `Still` completes (`_t >= 1`), call `BeginNextAction()`.

- [x] **C2 — Make turns instant**
  - In `BeginNextAction()`, when a turn is needed, apply the rotation immediately (set camera rotation to target, advance navigator state) instead of entering `State.Turning` with a slerp.
  - Chain multiple turns if the navigator requires e.g. a 180° reversal (two consecutive 90° turns).
  - After the instant turn(s), enter `Still` so the new view accumulates cleanly.

- [x] **C3 — Speed up movement**
  - Change `MoveTime` default from `4.0f` to `1.0f`.
  - `TurnTime` becomes unused (turns are instant) — can keep the property for backward compatibility or remove it.

- [x] **C4 — Adjust `Program.cs` render loop**
  - During `State.Still`, ensure `camController.Dirty` is false so `jobSystem.IsMoving` stays false and accumulation is unlimited.
  - During `State.Moving`, the existing soft-reset logic already handles the grainy-during-motion behaviour — no changes needed.
  - Remove or simplify the `_wasTurningLastFrame` / `InvalidateTaaHistory` turn-entry logic since turns are now instant.

- [x] **C5 — Verification**
  - Build passes.
  - Existing tests pass.
  - Visual check: camera holds still ~6s (image converges), moves quickly (~1s, slightly grainy), snaps rotation when turning.

### Implementation notes

- Implemented `StillTime` with a 6s default and changed `MoveTime` to 1s.
- `CameraController` now starts and returns to a still phase where `Dirty` remains false, allowing accumulation to continue uninterrupted.
- Turns snap the camera rotation immediately, support multi-step heading changes, and then enter a still phase before moving.
- Removed animated-turn TAA invalidation tracking from `Program.cs` because turns no longer slerp over frames.
- Added/updated camera controller tests for still phases and instant turns.
- Verification: build passed; all 135 tests passed.

### Execution order

C1 → C2 → C3 → C4 → C5
