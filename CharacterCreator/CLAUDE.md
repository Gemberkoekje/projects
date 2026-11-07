# Vortex Character Creator — repo conventions

The rebuild of Thorfgin's character builder (builder.qequoia.nl) on the Vortex stack. Read
[DESIGN.md](DESIGN.md) first (the design, every decision and why), then [NOTES.md](NOTES.md) for
where the last phase stopped.

**It lives here only for the proof of concept.** After Thorfgin's yes it moves to Vortex's private
GitLab (DESIGN.md §10, phase 6). Until then nothing of Vortex's is touched: no GitLab project, no
pushed VOID branch, no Keycloak change.

## Template

Kluskat (this repo) for the `projects` plumbing, by way of werkplaats: the layering, ports and
adapters, inline DI in `Program.cs`, config via the colon-path indexer, the analyzer stack in
`Directory.Build.props`, severities in the single `.globalconfig`, the GitHub workflow and the
gembernodes deploy. landerijen (`C:\git\gitlabs\landerijen-volken`) for Blazor and the ERU sign-in.

## Deltas

- **Layers:** `CharacterCreator.core` ← `.application` ← `.infrastructure` ← `.WebApp`, plus
  `.WebApp.Client` (the build screen, Blazor WebAssembly, references **core only**) and `.test`.
  `Architecture/Layering_specs` guards it.
- **core is the rules engine and must run in the browser:** plain C#, Qowaiv only, no I/O, no
  framework, no storage. It returns facts (findings), never sentences; the UI words them.
- **Warnings are errors** (projects/ house style). Tune a rule only in `.globalconfig`, with a
  `[Justification: …]`.
- **Anonymous by default.** No fallback policy: pages that need a sign-in name a policy
  (`CharacterCreatorPolicies`). `Beheer` = `void.referee`, `void.eventcontrol` or `*_*`, by name.
- **ERU sign-in is gated on `Keycloak:Issuer`.** Empty (the demo, the specs, a bare compose) means
  anonymous only, and `/aanmelden` explains why. Development uses `vortex-dev`, which only takes
  localhost redirects.
- **Guild Hall**, the player register of vortex-tools-style, vendored in
  `CharacterCreator.WebApp/wwwroot/lib/vortex-tools` (`vendor.ps1 -Register GuildHall`). Colours by
  role token only; the app's own components live in `wwwroot/app.css`.
- **Public repo.** `projects` and gembernodes are public: never commit the SL-only skills, the SL
  file (`skill-requirements-sl.json`) or a secret. The offline VOID snapshot
  (`Content/{ruleset}/void-snapshot.json`) holds book skills only, and a published ruleset names a
  skill outside the book only as `hidden-{VOID id}`.
- Dutch and English UI (Beheer and sign-in pages Dutch only), English code names. Test methods are snake_case English
  sentences.
- Local Postgres on **5435**, web on **8084** (`docker compose up -d postgres`, then run the WebApp;
  the dev password is in `appsettings.Development.json` and must match your `.env`).
- CI is GitHub Actions in this repo (`charactercreator-build-and-push.yml`), image on GHCR.
- **The SDK is pinned** in `global.json` (exact, `rollForward: disable`), and CI installs that one.
  The lock files hold packages the SDK adds itself (the WebAssembly trimmer, the web assets) at
  its runtime version, so another SDK fails CI's locked restore (NU1004). To move to a newer SDK:
  install it, change `global.json`, run `dotnet restore --force-evaluate`, and commit the lock
  files together.

## Working here

```bash
dotnet build CharacterCreator.slnx
```

```bash
dotnet test CharacterCreator.slnx
```

- **Every phase ends with a written summary appended to `NOTES.md`.**
- Razor components have nullable on, but avoid `?`: render an attribute that may be absent with
  `@attributes` (see `App.razor`'s theme), not with a `null` value.
- The client's `Program` is an explicit class in its own namespace: a second top-level `Program`
  would clash with the host's in the specs' `WebApplicationFactory<Program>`.
- Whatever a client page injects must be registered in both `Program.cs` files: the server
  prerenders the page first. Components a page shows only once interactive (`BuildScreen`) are
  registered in the client only.
- **The client is trimmed when published.** JSON between server and client goes through
  `core/Rulesets/RulesetJsonContext` (source-generated). Two consequences:
  - a `[JsonConstructor]` there must be at least internal;
  - a non-required `init` property must guard its default in its setter
    (`init => x = value ?? []`), because generated code passes `null` for a missing member.

  Check a client change in a Release publish, not only under `dotnet run`.
- **Browser access** goes through `CharacterCreator.WebApp/wwwroot/js/charactercreator.js` and the
  client's `Services/Browser.cs` (language, localStorage, download).
- **Language:** the `charactercreator.taal` cookie (`/taal/{nl|en}`), read on the server by
  `Localization/CookieUiLanguage` and in the browser from `html lang`. UI text is inline as
  `Ui.T("nl", "en")`; findings are worded by the client's `Words/FindingWords`.
- **The PDF** (`infrastructure/Printing/MigraDocSheetWriter`) lays out a `CharacterSheet` that the
  WebApp's `Printing/SheetComposer` words. PDFsharp-MigraDoc stays at 6.2.4, the last MIT release:
  never bump it without a licence check. Fonts are embedded TTFs (`Printing/Fonts`); check a layout
  change by reading the PDF, and font changes in the Linux image.
- **API answers are never re-executed as the not-found page:** a middleware switches status-code
  pages off under `/api` (a bodiless 415 or 429 would otherwise come back as a 400 from the page).
- **Content changes reach a ruleset only when it is published:** a stored ruleset carries its
  content. Locally, delete the `mt_doc_publishedruleset` row to reseed.
- The VOID side lives in the worktree `C:\git\gitlabs\void-charactercreator`, branch
  `feature/skills-requirements-api`. It is pushed, and on its way to `develop`, whose instance
  `void-test` the demo talks to.
- VOID's shared rule cases are copied, never edited:
  `scripts/sync-void-cases.ps1 -VoidRepo <void checkout>`. `VoidCases_specs` fails on a hand-edited
  copy.
- The engine (`core/Rules/PlanEvaluator`) returns findings with a severity:
  - Problem: VOID enforces the rule;
  - Warning: VOID only warns;
  - Info: something to know.

  Planning never stops on any of them.
- Text with backslashes or quotes: edit it with the edit tool. Bash heredocs and inline Python on
  this machine mangle them.
