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

---

## Phase 1 — the rules engine (2026-11-07)

_To understand this, start by reading `DESIGN.md` §3 (the rules) and §6 (facts, not sentences),
then `CharacterCreator.core/Rules/PlanEvaluator.cs` top to bottom, then `Rules/SkillRules.cs` next
to VOID's `src/Checkout/SkillRules.php`. The specs in `CharacterCreator.test/Rules/` read as the
rules in plain English._

### What it does

`CharacterCreator.core`, plain C# with Qowaiv only, so the browser can run it:

- **The model:**
  - `Skills/`: `SkillKey`, `RequirementGroup` (kind `any`/`points`/`extra`, read from VOID's spelling
    by `RequirementKinds.FromVoid`), `RuleSkill` (cost, `base_max`, `times_max`, deprecated,
    prerequisites, energy), `EnergyGrant`;
  - `Plans/`: `Holdings` (skills with times, immutable, ordered), `Plan` (mode, XP, held, planned);
  - `Rules/`: `RuleBook` (built through a Qowaiv `Result`, refusing duplicate or empty keys and
    negative numbers), `CreationRules` (15 XP, at most 3 left over), `XpTable` (VOID's `XpAward`,
    with the book of experiences).
- **`SkillRules`** mirrors VOID's `SkillRules`: `Whole`, `Spent`, `Holds`, `Missing`.
- **`PlanEvaluator`:**
  - `Evaluate` checks a plan in one of three modes:
    - new character: 15 XP, `base_max`, no extra skills, a warning above 3 XP left over;
    - existing character: whole XP, teacher for a first extra purchase, one per event;
    - free: `times_max`, the prerequisites and the XP only.

    It returns XP, spent, remaining, findings (problems first) and energy totals.
  - `Option` is the picker's view of one more purchase of a skill.
  - `Chain` is "add with prerequisites".
  - `Dependants` is "remove these too".
- **Findings**, each with a severity:
  - *Problem* (VOID enforces it): missing prerequisite, not enough XP, above maximum, above the
    creation maximum, not at creation, deprecated, unknown planned skill;
  - *Warning* (VOID only warns): too much left over, more than one teaching;
  - *Info*: needs a teacher, teaching not paid off yet, an unknown skill the character already holds.
- **VOID's shared cases:**
  - In VOID, `tests/data/skill-rules-cases.json` now also holds the XP award table (commit
    `72e705f`, pushed), run by `SkillRulesCasesTest::testAward`.
  - Here, `scripts/sync-void-cases.ps1` copies the file into `CharacterCreator.test/Rules/`, with a
    `.source` file naming VOID's commit and the hash of the text.
  - `VoidCases_specs` runs all 29 cases, the same 29 VOID runs. It also fails when the copy no
    longer matches its recorded hash.

### Decisions

1. **A skill's prerequisites are checked against the character's other skills, never itself.**
   Brew Expertise is in its own "XP in alchemist skills" set, and Teaching Expertise is itself an
   extra skill. Counting the skill would let it meet its own prerequisite. At the checkout's moment
   of purchase the skill isn't held yet, so this matches VOID.
2. **An existing character's XP follows the checkout** (CHECKOUT-DESIGN §6):
   - a first purchase of an extra skill takes the whole XP first, and may start without being paid
     off (Info, with what is still owed);
   - basis purchases must fit in what is left;
   - more than one teaching is a warning, because the checkout would hold the rest back.
3. **Severities:**
   - *Problem* is what VOID enforces, or would refuse;
   - *Warning* is what VOID only warns about (the 3 XP rule, decided in draft 4);
   - *Info* never stops anything.

   Planning stops on nothing. A push (v2, v3) will stop on problems.
4. **Held skills are fixed.** Their prerequisites are never checked (what VOID holds is VOID's
   record), and an unknown held skill is Info (SL-only or rank skills).
5. **Free mode is the old builder's free mode:** `times_max`, the prerequisites and the XP. No
   creation rules, no teachers.
6. **Records with `required` init properties, no positional records.** That is the house style
   (Qowaiv's QW0016, and landerijen and Kluskat have none).
7. **XP is `decimal`.** Whole XP is floored with VOID's epsilon (0.001), through Qowaiv's rounding.

### Gotchas

- **QW0016** rejects positional records; write `required public T Name { get; init; }` (SA1206 wants
  `required` before `public`).
- **SA1642** (StyleCop 1.1.118) treats a `record struct` as a class. The constructor summary must
  say "…the `<see cref="SkillKey"/>` class", not "struct".
- **QW0013** wants Qowaiv rounding. `Qowaiv.Mathematics.DecimalMath` is internal; the public form
  is the extension `System.QowaivDecimalExtensions.Round(decimal, int, DecimalRounding)`, as in
  `(xp + Epsilon).Round(0, DecimalRounding.Floor)`.
- **Sonar S3459/S1144** fire on *private* records that only JSON deserialization fills; make such
  test DTOs public nested records.
- **NUnit test names** built with `$"…{decimal}"` use the current culture (`1,5` here); build them
  with `string.Create(CultureInfo.InvariantCulture, …)` (S6618 refuses `FormattableString`).
- **On this machine, Bash heredocs and inline Python lose backslashes** in what they write, and an
  odd quote can stop a heredoc from running at all. It broke a JSON escape in VOID's cases file
  once. Edit text with backslashes or quotes through the edit tool.

### How it was checked

- `dotnet build`: clean, warnings as errors. `dotnet test`: 140 specs, 101 of them new:
  - the 29 VOID cases (prerequisites, whole XP, award table);
  - new character, existing character, free planning;
  - the picker, the chain, the dependants;
  - holdings, the rule book, the XP table.
- **The hash guard:** a space appended to the copy makes it fail; restoring the copy makes it pass.
- **VOID:** `SkillRulesCasesTest` 29 tests, code style and static analysis green; pushed to
  `feature/skills-requirements-api`.

### Not done / next

- **A teaching already on record** in VOID (a character mid-teaching, paying off) is not modelled
  yet. It belongs with reading characters from VOID (phase 7), as a plan input "teaching of S, owed
  N".
- **The next-events timeline** (spreading purchases over events with the XP table and one teaching
  per event) is built on this engine in phase 7; `XpTable.After` is ready for it.
- **Phase 2:**
  - the content schema, Thorfgin's text mapped to keys, the seeded `void` names;
  - `VoidHttpClient` against `void-test`, the import dry run, publishing, *Beheer → Rulesets*;
  - a recorded snapshot.

### File map

| Path | What |
|---|---|
| `CharacterCreator.core/Skills/` | `SkillKey`, `RequirementGroup`, `RequirementKind(s)`, `RuleSkill`, `EnergyGrant` |
| `CharacterCreator.core/Plans/` | `Holdings`, `Plan`, `PlanMode` |
| `CharacterCreator.core/Rules/` | `RuleBook`, `CreationRules`, `XpTable`, `SkillRules`, `Findings`, `PlanEvaluation` (with `SkillOption`, `PrerequisiteChain`, `EnergyTotal`), `PlanEvaluator` |
| `CharacterCreator.test/TestHelpers/Book.cs` | a small rule book of book skills for the specs |
| `CharacterCreator.test/Rules/` | `VoidCases_specs` (with the copied cases and their `.source`), `NewCharacter_specs`, `Existing_specs`, `Picker_specs`, `ChainAndDependants_specs`, `Building_blocks_specs` |
| `scripts/sync-void-cases.ps1` | copies VOID's shared cases and records commit and hash |
| VOID `tests/data/skill-rules-cases.json`, `tests/TestCase/Checkout/SkillRulesCasesTest.php` | the award cases (commit `72e705f`) |
