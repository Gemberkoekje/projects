# Vortex Character Creator — phase notes

Newest entry last. Each phase ends with an entry: what was built, the decisions, the gotchas, the
file map, and where to start reading.

---

## Phase 0 — scaffold (2026-11-07)

_To understand this, start by reading `DESIGN.md` §0, §6 and §10, then
`CharacterCreator.WebApp/Program.cs`, `Components/App.razor` and `Components/Layout/MainLayout.razor`,
then `CharacterCreator.WebApp.Client/Pages/Bouwen.razor`._

### What it does

- **Solution** `CharacterCreator.slnx`:
  - the four layers plus the WebAssembly client and the tests;
  - Kluskat's `Directory.Build.props` and `.globalconfig` (warnings are errors);
  - central packages, lock files.
- **Host:**
  - Blazor Web App, static server rendering, with `/` (the build screen) as an InteractiveWebAssembly
    page from the client project. It is prerendered on the server, then the browser takes over (the
    placeholder shows which one is running);
  - `/voorbeelden` and `/vragen` placeholders, not-found and error pages;
  - `/healthz`.
- **Guild Hall shell:**
  - vortex-tools-style 2.0.0 vendored (GuildHall register);
  - the crest bar: wordmark *Character Creator*, the user when signed in, the theme toggle, sign
    in/out, the nav row; the footer with the version;
  - the theme rendered on the server from the shared `vortex-theme` cookie.
- **ERU sign-in**, adapted from landerijen and gated on `Keycloak:Issuer`:
  - cookie + OIDC, the `eru_permission` claims, `/aanmelden` and `/afmelden`;
  - three account pages (not configured, failed, signed out).
- **Authorization:** the `Beheer` policy (`void.referee`, `void.eventcontrol`, `*_*`); no fallback
  policy, so everything is anonymous unless it says otherwise.
- **Marten** wired to PostgreSQL. For now it only holds the data-protection key ring
  (`MartenXmlRepository`, from Kluskat).
- **Docker and CI:** a runtime-only `Dockerfile`, a `docker-compose.yml` (postgres on 5435, app on
  8084), `.env.example`, and the GitHub workflow
  `.github/workflows/charactercreator-build-and-push.yml` (from Kluskat's: build, test, publish,
  push `ghcr.io/gemberkoekje/charactercreator:<sha>` on main).
- **The VOID side**, in the local worktree `C:\git\gitlabs\void-charactercreator`, branch
  `feature/skills-requirements-api` (from `origin/develop`, upstream removed, **not pushed**),
  commit `d4ed90a`:
  - `GET /skills` and `/skills/{id}` return each skill's `requirements` groups;
  - `tests/data/skill-rules-cases.json` holds the cases both engines must agree on, run by
    `SkillRulesCasesTest`.

### Decisions

1. **ASP.NET packages at 10.0.14.** NuGet's audit (an error here) flagged
   `Microsoft.AspNetCore.Components.WebAssembly.Server` 10.0.12 with two high-severity RCE advisories
   (GHSA-g27p-qf6j-c96c, GHSA-qjpx-jqm5-h2c4); 10.0.14 is the first patched version. The other
   ASP.NET packages moved with it. werkplaats and landerijen still pin 10.0.12 for other ASP.NET
   packages; they are not flagged, but worth a look.
2. **The theme attribute is an attribute dictionary** (`@attributes`), not a `string?` that is `null`
   when absent. Same effect (no attribute without the cookie), no nullable.
3. **`Keycloak:Issuer`**, as landerijen names it (DESIGN.md said `Keycloak:Authority`; fixed there).
4. **Sign-in has sign-out's shape**, with a sign-in icon (Bootstrap Icons `box-arrow-in-right`). On a
   phone Guild Hall shrinks `.vtx-signout` to an icon square and hides the label, and a bare text link
   overflowed it.
5. **The cookie session is 8 hours, sliding**, as in landerijen. Revisit in phase 7, when VOID is
   called with the stored access token.
6. **Placeholders are Dutch.** The NL/EN switch arrives with the build screen in phase 3.

### Gotchas

- **Two public `Program` classes.** .NET 10 makes a top-level `Program` public. With both host and
  client written that way, `WebApplicationFactory<Program>` in the specs is ambiguous (CS0433). The
  client's `Program` is now an explicit `internal static class` in its own namespace.
- **Guild Hall assumes `box-sizing: border-box`.** The crest bar's nav row is
  `calc(100% + 3rem)` wide with padding. The package's reference page sets border-box, but
  `guildhall.css` does not, so without the reset in `app.css` the page scrolls sideways by 16 px.
  Worth fixing in vortex-tools-style itself.
- **Sonar S8970 is a false positive in Razor.** It calls `= default!` on a `[CascadingParameter]`
  unnecessary, while CS8618 demands it. The cascaded `HttpContext` is `HttpContext?` instead; it
  really is absent during interactive rendering.
- **The house `RuntimeIdentifiers`** (linux-x64, win-x64) don't fit a WebAssembly project; the
  client overrides them with `browser-wasm`.
- **VOID: `has()` loads.** VOID's entities use `LazyLoadEntityTrait`, so `$entity->has('assoc')` and
  `get()` load a missing association. The first `Skill::getCompact()` used `has('requirements')`
  and quietly added a query per skill to every character response. It now checks `$this->_fields`.
- **VOID's test runner** runs its arguments through `bash -c`, so a `|` in `--filter` becomes a
  shell pipe (exit 127, broken-pipe noise). Use one plain word per run.
- **Python on Windows writes CRLF** in text mode. VOID is LF (`.gitattributes`): write with
  `newline='\n'`. In `projects` the Windows working tree is CRLF (`core.autocrlf=true`); the
  repository stores LF either way.

### How it was checked

- `dotnet build`: clean, warnings as errors. `dotnet test`: 39 specs:
  - layering, including "the client references only core";
  - the `Beheer` policy, including "Infobalie alone may not";
  - permission parsing and return URLs;
  - the shell over HTTP: crest bar, Guild Hall loaded, theme from the cookie (and a hostile cookie
    never echoed), the anonymous pages, `/aanmelden` without ERU, `/healthz`, a 404.
- **In the browser**, against a local Postgres:
  - the page is prerendered, then taken over by WebAssembly ("Dit scherm draait nu in je browser");
  - Guild Hall fonts and colours in light and dark;
  - the toggle writes the cookie, and enhanced navigation keeps the active tab;
  - no console errors and no server errors;
  - no sideways scroll on desktop or at 375 px.
- **The demo path:** Release publish, the runtime-only image, run against the local Postgres.
  `/healthz` returned 200, the page rendered, the data-protection key landed in Postgres, and the
  trimmed WebAssembly took over in the browser. The first visit downloads about 2.7 MB compressed
  (8.9 MB unpacked), cached after that.
- **VOID:** `./run-tests.sh` (code style, static analysis, PHPUnit 748 tests, printing), all green.
- **Reachability:** a throwaway pod on gembernodes got `401` from `void-api.the-vortex.nl` in
  0.1 s. Server-to-server calls involve no CORS.

### Not done / next

- **Phase 1, the engine:**
  - copy `tests/data/skill-rules-cases.json` from the VOID branch into the test project, and make a
    spec run it;
  - the model, prerequisites, creation and growth rules, findings, the XP table.
- **The GitHub workflow has not run yet:** nothing is pushed. The first push to `main` builds the
  image. Before that, check that CI restores in locked mode with these lock files.
- **Local preview:** `C:\git\.claude\launch.json` has a `charactercreator` entry (port 8084).
  `CharacterCreator/.env` (gitignored) holds the local Postgres password.

### File map

| Path | What |
|---|---|
| `CharacterCreator.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `.globalconfig`, `NuGet.config` | solution and build settings (Kluskat's, plus the Blazor and OIDC packages) |
| `CharacterCreator.core/`, `.application/` | empty layers, ready for phase 1 |
| `CharacterCreator.infrastructure/Adapters/DataProtection/MartenXmlRepository.cs` | the key ring in PostgreSQL |
| `CharacterCreator.WebApp/Program.cs` | composition root: Marten, data protection, cookie + gated OIDC, policies, Razor components |
| `CharacterCreator.WebApp/Authorization/` | ERU claims and permissions, the sign-in events, `CharacterCreatorPolicies` |
| `CharacterCreator.WebApp/Endpoints/AccountEndpoints.cs` | `/aanmelden`, `/afmelden` |
| `CharacterCreator.WebApp/Components/` | `App` (head, theme), `Routes`, `Layout/MainLayout` (crest bar, footer), `Layout/ThemeToggle`, pages, account pages |
| `CharacterCreator.WebApp/wwwroot/app.css`, `wwwroot/lib/vortex-tools/` | own styles; vendored Guild Hall 2.0.0 |
| `CharacterCreator.WebApp.Client/` | the browser's `Program`, `Pages/Bouwen.razor` |
| `CharacterCreator.test/` | `Architecture/`, `Authorization/`, `Web/Shell_specs`, `TestHelpers/CharacterCreatorWebApplicationFactory` |
| `Dockerfile`, `docker-compose.yml`, `.env.example` | image and local run |
| `../.github/workflows/charactercreator-build-and-push.yml` | CI |
