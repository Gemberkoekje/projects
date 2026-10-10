# Progress

Milestone log (BUILD_BRIEF §9). Per milestone: what was built, what differs from the brief and why, what's open. Design questions and the choices made for them are in `QUESTIONS.md`.

## M0 — Setup

**Built**
- `TheCurator.sln` with `src/Curator.Core` (no references at all), `src/Curator.Core.Tests` (xUnit) and the Godot project `game/Curator.Godot.csproj` (`Godot.NET.Sdk/4.6.3`, references Core).
- `game/project.godot` from the designer's skeleton, moved into `game/`. Its main scene is `scenes/Main.tscn`, an empty node whose script prints a line that proves Core is reachable. The viewport is 1920×1080 with `canvas_items` stretch and `keep` aspect.
- The SplitMix64 PRNG (`Curator.Core.Game.Prng`), keyed by seed, event sequence number and salt, with tests.
- `ProjectStructureTests`: Core references nothing Godot; all three projects share one `TargetFramework`.
- Shared build settings in `Directory.Build.props` (nullable on, implicit usings off, warnings as errors, code style enforced in the build), suppressions in `.globalconfig`.
- CI: `.github/workflows/ci-the-curator.yml` restores, builds and tests the solution on Ubuntu for pushes and PRs that touch `the-curator/`.
- Content and docs from the hand-off zip: `docs/GDD.md`, `BUILD_BRIEF.md` and `CONTENT_GUIDE.md` are unchanged. `game/content/` holds the seeded files and the gold standards, also unchanged.

**Verified**: `dotnet build` and `dotnet test` are green (9 tests). Godot 4.6.3 (Linux .NET build) imports the project, builds it with `--build-solutions`, and runs the main scene with `--headless` and in a window under Xvfb. All of that ran with only the .NET 10 runtime installed.

**Differs from the brief**
- The project lives in `the-curator/` inside the multi-project repo, not at a repo root. Paths in the docs are relative to `the-curator/`.
- `net10.0`, not Godot 4.6's template default `net8.0` (Q3).
- The window opens at 1600×900 (`window_*_override`) so it fits a 1080p screen with a taskbar. The viewport is still 1920×1080, as the brief asks (Q4).
- The work ran in a Linux cloud container, not on the designer's Windows PC (A4). Windows-only settings, such as the `d3d12` driver and the export preset (§7.13), haven't been tried (Q5).

**Open**: whether the designer's PC has the .NET 10 SDK (Q3).
