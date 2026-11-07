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

---

## Phase 2a — the content (2026-11-07)

_To understand this, start by reading `DESIGN.md` §4.1, then
`CharacterCreator.infrastructure/Content/2026-v07/IMPORT-REPORT.md`, then the two scripts in
`scripts/` (top docstrings first), then `CharacterCreator.core/Content/SkillContent.cs`._

Phase 2 is split: 2a is the content, 2b the VOID import, rulesets and *Beheer → Rulesets*.

### What it does

- **`scripts/import-thorfgin-content.py`** builds the content from two sources:
  - Thorfgin's data (his `src/json/{nl,en}`: skills, spells, recipes, presets);
  - VOID's public rulebook draft (`config/SkillRequirements/vaardigheden-2026-v07.json`).

  The result is 171 entries, one per VOID skill in the 2026 book, keyed by a slug of the VOID name
  and named after the book. It also yields 251 spells and techniques, 132 recipes (plus the common
  ones) and 15 presets. Every one of Thorfgin's skills matched; 11 entries merged several of his
  skills and are flagged for review.
- **`scripts/check-against-rulebook.py`** reads the current PDFs (`Vaardigheden.pdf`,
  `Spreuken_en_Technieken.pdf`, both 2026 V07):
  - it refuses to run when their version differs from the content's;
  - it sets each skill's page from its heading, and records the points the heading prints as
    `bookCost`;
  - it sets each spell's page.

  It found all 171 skills; three spells weren't found, listed in the report.
- **`CharacterCreator.core/Content/`:** `ContentBook` (built through a Qowaiv `Result`: unique keys
  and VOID names, presets only naming known skills), `SkillContent`, `LocalizedText` with a Dutch
  fallback, spells, recipes, presets, `Loresheet`. Plain records, so the browser can use them.
- **`SkillKey`** is plain text in JSON (`SkillKeyJsonConverter`).
- **`IContentSource`** (application port), and **`EmbeddedContentSource`** (infrastructure), which
  reads the JSON embedded in the assembly and keeps each ruleset after the first read. It is
  registered as a singleton.

### Decisions

1. **Keys are slugs of VOID's names** (`healer-a-elemental`), not of the Dutch book names:
   - one entry per VOID skill makes VOID's name the natural join, as in VOID's own loader;
   - a VOID rename will show up in the import's dry run (phase 2b).

   DESIGN.md §4.1 is updated to this.
2. **Names come from the 2026 book**, text from Thorfgin. A split skill gets "(elementair)" /
   "(spiritueel)"; two book skills under one VOID skill share an entry and list both in `book`.
3. **Only skills in VOID's public rulebook draft go in.** So no SL-only skill can reach this public
   repo, even if Thorfgin's older data had one. (It had none: nothing was left out.)
4. **The content is embedded in the infrastructure assembly**, because the runtime-only image has no
   volume. The import report sits next to it but isn't embedded.
5. **No `null` in the content.** An absent loresheet is an empty one (`Loresheet.Exists`); a cost the
   book doesn't print would be -1. A free skill really costs 0 ("Lezen & schrijven (0 punten)").
6. **The PDFs are read, not stored.** They were downloaded into the scratchpad for the check, and the
   repo keeps only what the check wrote into the content.

### Gotchas

- **Thorfgin's English spell file wraps Demonology's and Necromancy's skills in an extra list.**
  Positional NL/EN matching breaks there, so the import matches English to Dutch by skill ids.
- **The book's contents pages also print "Naam (N punten)".** A skill's heading is the last page with
  that text: prerequisites name skills without their points.
- **Thorfgin's text carries escaped newlines** as the two characters `\` and `n`; the import turns
  them into real newlines.
- **`EmbeddedResource LogicalName` with `%(RecursiveDir)`** keeps the build machine's separator: a
  backslash on Windows, a slash on Linux CI. The adapter normalizes names before matching.

### How it was checked

- `dotnet build`: clean, warnings as errors. `dotnet test`: 151 specs; 11 new in
  `Content/Content_2026_specs`:
  - 171 skills, all with names, categories and 2026 pages and costs;
  - the healer split, matched by VOID name ignoring case;
  - spells, recipes and loresheet links;
  - presets naming only known skills;
  - an unknown ruleset refused.
- **Locked restore and the Release publish** still pass.

### Not done / next

- **Phase 2b:**
  - `IVoidClient` with `VoidHttpClient` (`void-test`, the signed-in user's token) and
    `SnapshotVoidClient`;
  - the import: VOID skills joined to the content by VOID name into a `RuleBook`, with a dry-run
    report (unmatched names, cost versus `bookCost`, deprecated, prerequisites outside the book);
  - publishing a `Ruleset` to Marten, *Beheer → Rulesets*, the first-start seed, and
    `/api/rulesets/current`.
- **Mapping Thorfgin's energy properties** (`elemental_mana`, `willpower`, …) onto VOID's manatypes
  waits for VOID's actual manatype names, which the first import from `void-test` will show.
- **The three spells not found** and the 11 merged entries are for SL and EC (the report).

### File map

| Path | What |
|---|---|
| `scripts/import-thorfgin-content.py` | Thorfgin's data and VOID's rulebook draft into the content |
| `scripts/check-against-rulebook.py` | pages and book costs from the current PDFs |
| `CharacterCreator.infrastructure/Content/2026-v07/` | `skills.json`, `presets.json` (embedded), `IMPORT-REPORT.md` |
| `CharacterCreator.infrastructure/Content/EmbeddedContentSource.cs` | reads the embedded content |
| `CharacterCreator.application/Ports/IContentSource.cs` | the port |
| `CharacterCreator.core/Content/` | `ContentBook`, `SkillContent` and the records under it, `LocalizedText` |
| `CharacterCreator.core/Skills/SkillKeyJsonConverter.cs` | `SkillKey` as plain text in JSON |
| `CharacterCreator.test/Content/Content_2026_specs.cs` | the content as committed |

---

## Phase 2b — VOID, rulesets and Beheer (2026-11-07)

_To understand this, start by reading `DESIGN.md` §4.2 and §3.4, then
`CharacterCreator.application/Rulesets/RulesetBuilder.cs` (the join), then
`RulesetHandlers.cs` (seed, preview, publish), then `Components/Pages/Beheer/Rulesets.razor`._

### What it does

- **`RulesetBuilder.Build`** joins VOID's skills to the content by VOID name (ignoring case) into a
  `PublishedRuleset`, with an `ImportReport` (the dry run). VOID's numbers count; the content
  supplies keys and text. Pure: it reads and stores nothing.
- **`PublishedRuleset`** (core): one immutable document per ruleset, id
  `{content ruleset}@{UTC moment}`. It holds VOID's rules per book skill, the content, the creation
  rules, who published it, from where, and whether it is provisional. `Offered()` is what the
  picker offers: rules and content both present, not retired.
- **Handlers:**
  - `SeedRulesetHandler`: on the first start, the content with the offline snapshot, only when
    nothing is published;
  - `PreviewImportHandler`: the dry run, against the current ruleset;
  - `PublishRulesetHandler`: imports again, refuses what the report refuses, stores;
  - `ReadCurrentRulesetHandler`.
- **VOID:** `IVoidClient` with `VoidHttpClient`, a typed `HttpClient` that calls `GET skills` with
  the signed-in person's bearer token. 401/403, other statuses, network and JSON errors all become
  `Result` messages. `VoidSkillsJson` reads VOID's snake_case list envelope.
- **Offline snapshot:** `Content/2026-v07/void-snapshot.json`, made by
  `scripts/make-offline-snapshot.py`, read by `EmbeddedOfflineSnapshot` (`IOfflineVoidSnapshot`).
  171 skills, 146 prerequisite groups, book skills only, `synthesized: true`.
- **`MartenRulesetStore`** (`IRulesetStore`): newest by `PublishedAt`; an existing id is refused.
- **`GET /api/rulesets/current`**, anonymous:
  - the whole ruleset in camelCase JSON (`RulesetJson`);
  - its id as the ETag, with `Cache-Control: no-cache`, and a 304 on a matching `If-None-Match`;
  - a 404 before anything is published;
  - response compression on, Brotli and gzip at the optimal level.
- **Beheer → Rulesets** (`/beheer/rulesets`, policy `Beheer`) shows:
  - what is published;
  - a dry-run button, when `Void:BaseUrl` is set;
  - the report and a publish button;
  - the history.

  The crest bar shows *Beheer* to SL and EC only.
- **`EruAccessTokens`:** the signed-in person's ERU access token from the cookie ticket, refreshed
  through Keycloak's token endpoint when it has expired, and the cookie re-issued with the new pair.
- **`/geen-toegang`:** where the cookie sends a signed-in person who lacks a page's permission.

### Decisions

1. **A skill outside the book is never named in a published ruleset.** A prerequisite on one becomes
   `hidden-{VOID id}`; the name shows only in the dry run (SL and EC). The specs check that the
   serialized ruleset contains no such name.
2. **The offline snapshot is synthesized, not recorded.** `void-test` has no rulebook draft loaded
   yet, so there was nothing to record. Whatever it seeds is `Provisional`, and *Beheer* says so
   until an SL or EC imports from a VOID.
3. **Publishing imports again** instead of storing the earlier dry run: what is stored is VOID's
   state at that moment, and nothing has to be kept between the two posts.
4. **One document carries both languages.** No `?lang=`: switching language reloads nothing, and
   Brotli brings 555 KB down to 84 KB.
5. **A retired skill keeps its rules** but isn't offered; characters that have it still need them.
6. **`Void:BaseUrl` must end with a slash**, checked at startup: relative `skills` would otherwise
   replace the base's last path segment.
7. **Pages in *Beheer* are server-rendered forms**, not interactive: each button is a post, which
   keeps the ERU token on the server.

### Gotchas

- **`RequirementGroup` couldn't be read back from JSON.**
  - Cause: System.Text.Json wants a constructor whose parameter types match the properties
    (`IReadOnlyList`, not `IEnumerable`).
  - Impact: Marten could not have read back any stored ruleset, and the API would have failed too.
  - Fix: a private `[JsonConstructor]` with its parameters in a different order. JSON binds by
    name, and the order keeps `new (kind, amount, [])` unambiguous.

  Only the round-trip spec caught it.
- **The authorization middleware enforces a page's `[Authorize]` before the router renders.** A
  signed-in player without the role was redirected to ASP.NET's default `/Account/AccessDenied`,
  a 404. The cookie now has `AccessDeniedPath = /geen-toegang`.
- **The offline snapshot wrote `takenAt`** while its reader expects VOID's snake_case: `TakenAt`
  silently fell back to 1970. Now `taken_at`.
- **Brotli's default level is its fastest:** 222 KB against gzip's 141 KB for a ruleset.
- **S1075 also flags `url + '/'`** (a hardcoded path delimiter), hence the startup check instead.
- **`git status` warns about LF and CRLF**: `core.autocrlf` is on and the repo stores LF. That is
  expected, not a change.
- **Bash heredocs on this machine choke on apostrophes** (this entry too): append with the edit
  tool.

### How it was checked

- `dotnet build`: clean, warnings as errors. `dotnet test`: 216 specs, 65 new:
  - `Rulesets/RulesetBuilder_specs` (16): the join, case, hidden ids, the report's lists, energy,
    changes against a previous ruleset, duplicates, unpublishable rules;
  - `Rulesets/Offline_seed_specs` (10), on the real content plus the embedded snapshot:
    - 171 skills offered, publishable, marked provisional;
    - book skills only, with no cost differences;
    - every key is a book skill or a hidden id;
    - the JSON round trip;
  - `Rulesets/Ruleset_handler_specs` (10): seed once, preview stores nothing, publish, refusals,
    the same second twice;
  - `Void/VoidSkillsJson_specs` (8) and `Void/VoidHttpClient_specs` (8);
  - `Web/Ruleset_api_specs` (6): JSON, ETag, 304, Brotli, 404 before a publish;
  - `Web/Beheer_rulesets_specs` (7): anonymous to sign-in, a player or infobalie to
    `/geen-toegang`, an SL or EC sees the page, *Beheer* in the crest bar.
- **Test setup:**
  - Marten's store is swapped for `InMemoryRulesetStore`, at the same Scoped lifetime;
  - sign-in goes through a `TestSignIn` scheme driven by a header;
  - challenge and forbid stay the cookie's.
- **Run locally against Docker Postgres:**
  - the first start seeded `2026-v07@20261107T084541Z` (171 rules);
  - the API served it from Postgres;
  - a restart didn't seed again (still one document);
  - the 304 and the anonymous redirect worked;
  - `/geen-toegang` renders in Guild Hall.
- Locked restore and the Release publish pass.

### Not done / next

- **A real import from `void-test`:** it waits for the single VOID push (phase 5), then for Arie to
  refresh `void-test` and load the draft. Until then nobody can sign in to the demo either: the
  `vortex-dev` redirect URIs (Arie).
- **Energy names:** map Thorfgin's energy properties onto VOID's manatypes once a real import shows
  their names (`EnergyTypes` in the report lists them).
- **Phase 3, the build screen:** it loads `/api/rulesets/current`. The WebAssembly publish is
  trimmed ("optimizing assemblies for size"), so read the ruleset with a System.Text.Json
  source-generated context. Reflection plus a private `[JsonConstructor]` may not survive trimming.
- **The rest of the VOID branch**, local commits only until the single push: the "3 XP left over"
  warning and its number in the API, and SL/EC rights on the rules overview.

### File map

| Path | What |
|---|---|
| `CharacterCreator.core/Rulesets/` | `PublishedRuleset` (with `RulesetSummary`), `RulesetJson` |
| `CharacterCreator.core/Skills/RequirementGroup.cs` | the JSON constructor |
| `CharacterCreator.application/Void/VoidSnapshot.cs` | VOID's skills at one moment |
| `CharacterCreator.application/Ports/` | `IVoidClient`, `IOfflineVoidSnapshot`, `IRulesetStore` |
| `CharacterCreator.application/Rulesets/` | `RulesetBuilder`, `ImportReport` (and its records), the four handlers |
| `CharacterCreator.infrastructure/Void/` | `VoidHttpClient`, `VoidSkillsJson`, `EmbeddedOfflineSnapshot` |
| `CharacterCreator.infrastructure/MartenDb/MartenRulesetStore.cs` | the store |
| `CharacterCreator.infrastructure/Content/2026-v07/void-snapshot.json` | the offline snapshot (embedded) |
| `scripts/make-offline-snapshot.py` | synthesizes it |
| `CharacterCreator.WebApp/Endpoints/RulesetEndpoints.cs` | `/api/rulesets/current` |
| `CharacterCreator.WebApp/Components/Pages/Beheer/` | `Rulesets.razor`, `ReportList.razor` |
| `CharacterCreator.WebApp/Components/Account/NoAccess.razor` | `/geen-toegang` |
| `CharacterCreator.WebApp/Authorization/EruAccessTokens.cs` | the ERU token, refreshed |
| `CharacterCreator.WebApp/Program.cs` | VOID client, store, handlers, seed, compression, `AccessDeniedPath` |
| `CharacterCreator.test/TestHelpers/` | `InMemoryRulesetStore`, `TestSignIn`, the factory |
| `CharacterCreator.test/Rulesets/`, `Void/`, `Web/Ruleset_api_specs.cs`, `Web/Beheer_rulesets_specs.cs` | the specs |

---

## Phase 3 — the build screen (2026-11-07)

_To understand this, start by reading `DESIGN.md` §5 and §4.3 (both have an "as built in phase 3"
part), then `CharacterCreator.WebApp.Client/Components/BuildScreen.razor.cs` (the state and every
action), then `SkillPicker.razor`, and `core/Plans/PlanImport.cs` for files._

### What it does

- **The build screen** (`/`, Blazor WebAssembly): the server prerenders the heading and a loading
  line; in the browser `BuildScreen` loads the published ruleset, then shows:
  - **the head:** name, the mode (new character or free planning, with "after N events"), and
    the XP, spent and left counters with the plan-wide findings;
  - **the picker:**
    - search, and Basis/Extra/All (Basis by default for a new character);
    - categories folded open on demand;
    - every skill's state in words;
    - "+" (or "+ anyway") and "+ with prerequisites (n XP)";
    - ticking several and adding them at once;
    - details per skill: cost, maxima, prerequisites, rulebook page, description, spells,
      loresheet;
  - **the chosen table:** XP, −/+, the notes in words, remove; removing what others need asks
    first;
  - **the summary:** energy chips (Guild Hall's energy tokens), properties (HP from 1, AP, imbue
    caps from VOID, craft caps), spell and recipe tiles with a details panel;
  - **keeping it:** the working plan in localStorage after every change, Save to a list (open,
    delete), Export and Import as a file, New plan.
- **Old saves:** `PlanImport` reads our JSON and Thorfgin's three formats, `.dat` included, through
  `legacy.json` (182 of his skills, by id and name).
- **Presets** (`/voorbeelden`): cards with text, skills and XP. *Start from this* opens
  `/?voorbeeld={key}`, which asks first if the current plan has skills.
- **NL/EN:**
  - `/taal/{nl|en}` sets `charactercreator.taal` for a year and comes back;
  - the crest bar has the switch;
  - `html lang`, the layout, the build screen, the presets, the FAQ and the not-found page follow
    it.
- **FAQ** (`/vragen`), written in both languages, with §3.6's table of rule changes.
- **Core additions:**
  - `PlanEvaluator.Options`: the whole picker on one evaluation of the plan;
  - `PlanFile` and `PlanImport`;
  - `LegacySkill` on `ContentBook` and `PublishedRuleset`;
  - `RulesetJsonContext`, the source-generated JSON.
- **Guild Hall components** in `app.css`: buttons, fields, banners, cards, tables, chips, tiles,
  all on role tokens. Dark mode follows. The 2b Beheer page's classes are styled now too.

### Decisions

1. **The engine's facts are worded in the client** (`Words/FindingWords`), Dutch and English. A
   skill outside the book is "a skill outside the book", never named.
2. **The build screen asks the engine after every change.** `Options` evaluates the plan once, then
   one more purchase per skill. That costs 171 small evaluations per click in WebAssembly, with no
   noticeable delay.
3. **Language by cookie and a full reload**, not interactive switching:
   - the server pages follow it without JavaScript;
   - the build screen reads `html lang`;
   - the plan survives, being in the browser;
   - the switch link opts out of enhanced navigation (`data-enhance-nav="false"`).
4. **One JS module** (`wwwroot/js/charactercreator.js`, in the WebApp) for language, storage and
   download. Storage failures (a private window) return empty or false, never throw.
5. **The prerender is a heading and a loading line.** The plan is in the browser, so a fully
   prerendered screen would flash an empty plan first, and cost server time per visit.
6. **Thorfgin's categories are cleaned up in the import script**, not in the UI:
   - `Vechtervaardigheden` is folded into `Vechtersvaardigheden`;
   - each Dutch category gets one English name, with "Skills" capitalized;
   - the picker groups by the Dutch name.
7. **Existing characters wait for phase 7.** A file in that mode opens as free planning.
8. **Empty plan, no plan-wide warning:** "15 XP left over" before anything is chosen says nothing.

### Gotchas

- **Source-generated JSON overwrites `init` defaults.** For a missing member it passes `null` or
  0 rather than keeping the initializer. `PlanFile` and `PublishedRuleset.Legacy` came back null,
  and only the specs caught it. Their setters now guard the default; CLAUDE.md has the rule.
- **A `[JsonConstructor]` used by source generation can't be private:** `RequirementGroup`'s is
  internal now.
- **The test host serves static files as empty 200s.** The content root is the test's bin folder
  (Kluskat's lesson), so a spec can't check a static asset's contents.
- **Blazor HTML-encodes non-ASCII:** `·` arrives as `&#xB7;`, `ë` as `&#xEB;`. Specs match around
  them.
- **Razor `@code` vs a `.razor.cs` partial:**
  - in `.razor` files, `= default!` on a parameter trips S8970 (nullable is off there), so leave
    the initializer out;
  - in the partial, `[Parameter]` setters trip QW0011, so parameters and `@inject` stay in the
    `.razor` file.
- **`JsonElement.EnumerateArray()` is IDisposable** (IDISP004), even in `foreach`: use `using var`.
- **Cookies are per host, localStorage per origin:** on `localhost:8086` the language from 8084
  came along, the plans didn't.
- **A stored ruleset carries its content,** so a content fix needs a new publish (locally: delete
  the row to reseed).

### How it was checked

- `dotnet build`: clean, warnings as errors. `dotnet test`: 278 specs, 62 new:
  - `Plans/PlanImport_specs` (14), `Plans/PlanFile_specs` (7);
  - `Rules/Options_specs` (5): the same as one at a time, on four kinds of plan;
  - `Rulesets/RulesetJsonContext_specs` (5): the round trip without reflection, the same JSON as
    the API, no computed members, an old stored ruleset, `RulesetClient.Prepare`;
  - `Content/Content_2026_specs` (+3): the legacy map, one name per category;
  - `Words/FindingWords_specs` (17), `Web/Language_specs` (8), `Web/Presets_specs` (3).
- **Walked through in the browser (dev):**
  - the chain (Harnas III brings I and II);
  - removing with dependants, and "only this one", which leaves Harnas II flagged;
  - a reload keeps the plan; saving; the switch to English;
  - a preset with the "replace?" question;
  - spell details;
  - importing a `.dat` save of the old builder, with a skill that no longer exists left out and
    named;
  - export;
  - phone width and dark mode, after which the chosen table's grid and the events hint were
    fixed.
- **Release publish (trimmed), run from the publish folder:** the build screen loads, the chain,
  the `.dat` import and saving work. No trim warnings. Locked restore passes.

### Not done / next

- **Phase 4, print:** the PDF (server-side, Guild Hall's print look).
- **Differences per plan across rulesets** (§4.3): needs `GET /api/rulesets/{id}` and a comparison
  of the plan's skills. The screen now only says the plan was made on another ruleset.
- **Mijn personages / existing characters:** phase 7, signed in.
- **Beheer and the sign-in pages** are Dutch only.
- **Thorfgin's energy properties** are still not mapped onto VOID's manatypes: energy comes from
  VOID only.
- **The VOID branch:** unchanged, local only (the 3-XP warning, the SL/EC rights), one push in
  phase 5.

### File map

| Path | What |
|---|---|
| `CharacterCreator.WebApp.Client/Pages/Bouwen.razor` | the page: prerendered heading, then `BuildScreen` |
| `CharacterCreator.WebApp.Client/Components/` | `BuildScreen` (+ `.razor.cs`), `SkillPicker`, `ChosenSkills`, `SummaryPanels`, `SkillDetails` |
| `CharacterCreator.WebApp.Client/Services/` | `Browser` (JS module), `RulesetClient`, `PlanStore`, `IUiLanguage` |
| `CharacterCreator.WebApp.Client/Words/` | `Ui` (language, numbers), `FindingWords` |
| `CharacterCreator.WebApp/wwwroot/js/charactercreator.js` | language, localStorage, download |
| `CharacterCreator.WebApp/wwwroot/app.css` | the Guild Hall components and the build screen |
| `CharacterCreator.WebApp/Localization/CookieUiLanguage.cs`, `Endpoints/LanguageEndpoints.cs` | the language cookie |
| `CharacterCreator.WebApp/Components/Layout/` | `MainLayout` (translated), `LanguageLink`, `ThemeToggle` |
| `CharacterCreator.WebApp/Components/Pages/` | `Voorbeelden`, `Vragen`, `NotFound` |
| `CharacterCreator.core/Plans/` | `PlanFile` (+ `PlanSkill`), `PlanImport` (+ `ImportedPlan`, `PlanSource`) |
| `CharacterCreator.core/Rulesets/RulesetJsonContext.cs` | source-generated JSON |
| `CharacterCreator.core/Rules/PlanEvaluator.cs` | `Options` |
| `CharacterCreator.core/Content/` | `LegacySkill`; `ContentBook.Legacy` |
| `CharacterCreator.infrastructure/Content/2026-v07/legacy.json` | Thorfgin's skills per key (embedded) |
| `scripts/import-thorfgin-content.py` | writes `legacy.json`; one spelling per category |
| `CharacterCreator.test/Plans/`, `Words/`, `Rules/Options_specs.cs`, `Rulesets/RulesetJsonContext_specs.cs`, `Web/Language_specs.cs`, `Web/Presets_specs.cs` | the specs |

---

## Phase 4 — the PDF (2026-11-07)

_To understand this, start by reading `DESIGN.md` §5 (Print, "as built in phase 4"), then
`CharacterCreator.application/Sheets/CharacterSheet.cs` (what a sheet holds), then
`CharacterCreator.WebApp/Printing/SheetComposer.cs` (how a plan becomes one), then
`CharacterCreator.infrastructure/Printing/MigraDocSheetWriter.cs` (the layout)._

### What it does

- **Afdrukken (PDF)** on the build screen posts the plan to `POST /api/plans/sheet` and saves the
  answer as `{name}.pdf`.
- **The endpoint** words the plan into a `CharacterSheet` (`SheetComposer`), checked against the
  current ruleset, in the visitor's language. `ISheetWriter` (`MigraDocSheetWriter`) lays it out.
- **Page 1, the sheet:**
  - eyebrow, the name in crimson Inknut Antiqua, the kind of plan and XP, date and rules;
  - the numbers as boxes, then energy per day;
  - a *Let op* box with what breaks or warns;
  - the skills table: times, XP, page, notes, total.
- **Then:**
  - each skill's text, with cost, times, category, prerequisites and rulebook page;
  - spells and techniques, with energy, incantation, effect, duration and page;
  - recipes, the loresheets as links, and XP per event (the next 10 events).

  The footer on every page: name, rules, page x of y.
- **`PlanSummary`** (client `Words/`): properties, spells and recipes, for the screen's summary and
  the sheet alike. `SummaryPanels` uses it now instead of its own copy.
- **Compose and `.env.example`** pass `VOID_BASE_URL` (2b had missed it).

### Decisions

1. **The plan travels to the server to be printed**, as the design said (made on the server). It is
   not stored and not logged; DESIGN.md §6 now says "never stored" rather than "never reaches".
2. **Words on the server, layout in infrastructure.**
   - `CharacterSheet` (application) holds only finished strings.
   - The WebApp composes it with the client's `FindingWords` and `PlanSummary`, which infrastructure
     can't reference; that keeps the screen and the PDF saying the same.
   - The writer knows nothing of rules or languages.
3. **The current ruleset**, not the one the plan names: the PDF shows what the screen shows.
4. **PDFsharp-MigraDoc 6.2.4**, pinned with landerijen's licence note: 6.5.0 and later are AGPL. The
   same transitive pin applies (`System.Security.Cryptography.Pkcs` 10.0.13).
5. **Fonts from the portal's crest bar** (TTF, OFL). vortex-tools-style ships only WOFF2, which
   PDFsharp can't read, and fontTools isn't installed here. The 24 KB Spline Sans Mono there is a
   subset, so it stays out. No italic face: PDFsharp slants Spectral.
6. **Only the skills' texts start a new page.** The sheet keeps page 1 to itself; the rest flows
   on, which cut the Paladijn sheet from 6 pages to 4.
7. **Anonymous, so limited:** 64 KB body, 300 skills, 30 sheets per minute per address (behind the
   proxy that is the forwarded address), `no-store`.

### Gotchas

- **Status-code pages replay the request.** A bodiless 429 or 415 was re-executed as a POST against
  `/niet-gevonden`, and came back as a 400 ("incorrect Content-type").
  - Wrapping status-code pages in `UseWhen` broke the not-found page itself: re-execution needs the
    main pipeline's routing.
  - The fix is a middleware after it that sets `IStatusCodePagesFeature.Enabled = false` under
    `/api`.
  - The rate limiter also writes a JSON body and `Retry-After`.
- **A form post gets 400 from antiforgery** before the endpoint runs (Blazor's `UseAntiforgery`).
  Harmless; the spec tests 415 with `text/plain`.
- **PDF font names escape spaces:** `Spectral Regular` is written `Spectral#20Regular`.
- **Docker Desktop resets connections to a published port while the app isn't listening**, and
  curl doesn't retry a reset. The container looked dead; it was starting.
- **Postgres binds the host's loopback,** so a container can't reach it at
  `host.docker.internal:5435`. Join its compose network (`charactercreator_default`) instead.

### How it was checked

- `dotnet build`: clean, warnings as errors. `dotnet test`: 303 specs, 25 new:
  - `Printing/SheetWriter_specs` (6): a PDF, the embedded fonts, the link, many pages, an empty
    plan, writing in parallel;
  - `Printing/SheetComposer_specs` (9): title and rules, the screen's numbers, notes, the table,
    texts, spells, loresheets, XP per event, English, unnamed, free planning;
  - `Web/Sheet_specs` (10): the PDF and its file name, empty plan, 415, 400, too many skills, 429
    after 30, file names.
- **Read the PDFs** (Paladijn in Dutch, a broken plan in English): layout, Dutch characters,
  notes, links.
- **In the browser:** the button delivers `Paladijn.pdf`.
- **In the Linux image** (CI's publish plus the runtime-only Dockerfile, on Postgres's network): the
  build screen and a sheet, the same PDF as on Windows; the container has no fonts. Image and
  container removed afterwards.
- Locked restore passes, with the new lock entries.

### Not done / next

- **Phase 5, the demo:**
  - the single VOID push and MR (first the 3-XP warning and the SL/EC rights, locally);
  - Arie refreshes `void-test` and loads the draft;
  - Arie adds the database role, the 1Password item and the `vortex-dev` redirect URIs;
  - the gembernodes manifests, ingress and image-tag PR;
  - then sign in, import, publish, and show Thorfgin.
- **The envelope list** (*Mijn personages*, phase 7) can reuse `ISheetWriter` with another layout.
- **Fonts:** if vortex-tools-style ever ships TTF in `dist/fonts`, take them from there.

### File map

| Path | What |
|---|---|
| `CharacterCreator.application/Sheets/CharacterSheet.cs` | the sheet: facts, notes, tables, entries, links, labels |
| `CharacterCreator.application/Ports/ISheetWriter.cs` | the port |
| `CharacterCreator.infrastructure/Printing/MigraDocSheetWriter.cs` | the layout |
| `CharacterCreator.infrastructure/Printing/EmbeddedFonts.cs` | the font resolver |
| `CharacterCreator.infrastructure/Printing/Fonts/` | Spectral, Inknut Antiqua (TTF) and their OFL texts |
| `CharacterCreator.WebApp/Printing/SheetComposer.cs` | a plan into a sheet, worded |
| `CharacterCreator.WebApp/Endpoints/SheetEndpoints.cs` | `POST /api/plans/sheet`, limits, file name |
| `CharacterCreator.WebApp/Program.cs` | writer, rate limiter, status pages off under `/api` |
| `CharacterCreator.WebApp.Client/Words/PlanSummary.cs` | properties, spells, recipes (screen and sheet) |
| `CharacterCreator.WebApp.Client/Components/BuildScreen.razor(.cs)` | the button and `PrintAsync` |
| `CharacterCreator.WebApp/wwwroot/js/charactercreator.js` | `download` takes bytes too |
| `Directory.Packages.props` | PDFsharp-MigraDoc 6.2.4 and the Pkcs pin |
| `docker-compose.yml`, `.env.example` | `VOID_BASE_URL` |
| `CharacterCreator.test/Printing/`, `Web/Sheet_specs.cs` | the specs |

---

## Phase 5a — the VOID work and the demo, ready to ship (2026-11-07)

_To understand this, start by reading `CHARACTER-CREATOR-NOTES.md` in the VOID worktree
(`C:\git\gitlabs\void-charactercreator`), then DESIGN.md §3.3 and §3.7 ("as built in phase 5"), then
`apps/charactercreator/README.md` in the gembernodes worktree (`C:\git\gembernodes-charactercreator`)._

Phase 5 is split: 5a is everything that can be done without pushing, 5b the pushes, Arie's steps
and the live demo.

### What it does

- **VOID** (branch `feature/skills-requirements-api`, three new local commits on top of the two
  pushed ones):
  - **the 3-XP rule:** a concept character with more than 3 XP left over gets
    `warnings.left_over` (a warning, never a refusal), and `GET /skills` gives
    `creation: {start_xp: 15, max_left_over: 3}`;
  - **SL and EC keep the rules:**
    - `/admin/skills` is the overview, with extra skills marked and every prerequisite in words;
    - Referee and up edit the rule fields and prerequisites;
    - name, sort order and blanks, adding, deleting and the API stay Super only;
  - the branch's notes file.
- **The Character Creator** reads `creation` at import (`VoidSnapshot.Creation`,
  `CreationFromVoid`). It publishes VOID's numbers instead of assuming 15 and 3, and the dry run
  says where they came from. The offline snapshot states them too.
- **gembernodes** (branch `claude/charactercreator-demo`, one local commit):
  - the namespace, the `charactercreator-secrets` 1Password item, one pod (Recreate), the service,
    and the nginx-public ingress with cert-manager for `charactercreator.gemberkoekje.nl`;
  - sign-in through `vortex-dev`, VOID at `void-test`;
  - a README with the database role, the 1Password item and the Keycloak redirect URIs;
  - the image tag is `PENDING-FIRST-BUILD` until `projects` main has built one.

### Decisions

1. **Nothing pushed yet:** not VOID, not `projects`, not gembernodes. Each costs CI or publishes
   something, so each waits for Arie's go.
2. **The creation rules ride on `GET /skills`** rather than a new endpoint: the import already
   calls it.
3. **The admin page only:** `PUT /skills` in the API stays Super, as the decision was about the
   screen.
4. **VoidWalker doesn't show the warning yet:** that is its own repository and MR, outside "VOID,
   once".
5. **The demo pod gets `ghcr-login` from Reflector**, which copies it into every namespace, so the
   app declares none.

### Gotchas

- **VOID fixtures:** a test that adds `SkillRequirements` rows must declare that fixture, or the rows
  leak into the next class. `CheckoutSkillsTest` passed alone and failed in the full run.
- **`run-tests.sh` passes its arguments through `bash -c`:** a `--filter "A|B"` becomes a pipe.
- **The image is only built from `projects` main** (`if: github.ref == 'refs/heads/main'`). So the
  demo needs the Character Creator merged into the public `projects` main, as Kluskat was.
- **The main gembernodes checkout was on another session's branch** (`claude/zeef-…`); the demo is
  in a worktree of its own.

### How it was checked

- **VOID:** `./run-tests.sh` passes all four stages: code style, PHPStan, phpunit (769 tests, 12
  new) and printing (26). The branch is 5 ahead of `develop`, 0 behind, and merges cleanly.
- **Character Creator:** 309 specs (6 new: parsing `creation`, the client's flag, the builder
  publishing VOID's numbers, the offline snapshot).
- **gembernodes:** `kubectl kustomize` builds `apps/charactercreator`, `ingress` and `namespaces`.

### Phase 5b: what remains, in order

1. **VOID:** push `feature/skills-requirements-api` once, and open one MR into `develop`. Then Arie
   refreshes `void-test` in Portainer and loads the draft there (`bin/cake skill_requirements`, dry
   run, then `--apply`).
2. **`projects`:** push `claude/charactercreator` and merge it into main (PR), so "Build and Push
   CharacterCreator" builds the image.
3. **Arie, for the demo:**
   - the database role and the `charactercreator-secrets` 1Password item (README steps 1 and 2);
   - the `vortex-dev` redirect URIs (step 3).
4. **gembernodes:** fill in the image sha, push `claude/charactercreator-demo`, open the PR and
   merge it. Then check the pod, the certificate and the seed.
5. **On the demo:** sign in as an SL or EC, run the dry run against `void-test`, publish, and show
   Thorfgin.

### File map

| Path | What |
|---|---|
| VOID `src/Checkout/SkillRules.php`, `src/Model/Entity/Character.php` | the 3-XP constant and warning |
| VOID `src/Controller/SkillsController.php`, `src/View/ApiView.php` | `creation` beside the list |
| VOID `src/Policy/…/SkillsControllerPolicy.php`, `src/Policy/Entity/SkillPolicy.php` | Referee and up; Super-only fields |
| VOID `src/Controller/Admin/SkillsController.php`, `templates/Admin/Skills/*` | overview and edit |
| VOID `tests/ApiTest/Admin/SkillRulesTest.php` and adjusted tests | the specs |
| VOID `CHARACTER-CREATOR-NOTES.md` | the branch's notes |
| `CharacterCreator.application/Void/VoidSnapshot.cs`, `Rulesets/RulesetBuilder.cs`, `ImportReport.cs` | creation rules from VOID |
| `CharacterCreator.infrastructure/Void/*`, `Content/2026-v07/void-snapshot.json` | reading them |
| gembernodes `apps/charactercreator/`, `ingress/charactercreator-ingress.yaml`, `namespaces/charactercreator-namespace.yaml` | the demo |

---

## UX batch 1 — quieter picker, Undo, compact presets, FAQ (2026-11-07)

_To understand this, start by reading `UX-REVIEW.md` §0 and §3 (P3, P4, P9, P11), then
`CharacterCreator.WebApp.Client/Components/SkillPicker.razor`, then the toast part of
`Components/BuildScreen.razor.cs` (`ShowToast`, `UndoAsync`)._

The first batch from the UX review's §4. Everything is local on `claude/charactercreator`, on top of
the review's own commit. Nothing is pushed.

### What it does

- **Picker rows (P3):**
  - the name, and under it the cost and one status pill (`FindingWords.Status`): ✓ gekozen,
    ⚠ vereist …, ✗ te duur, ⓘ leraar nodig. No pill for "can buy";
  - one main action on the right:
    - "+";
    - the stepper for a skill the plan has (the same control as the chosen list);
    - a named chain, "+ met Harnas I en Harnas II · 4 XP";
    - or "⋯", which opens the details, holding the chain, *Toch toevoegen* and the choices left;
  - rows that can't be bought are dimmed to 70%; *Alleen wat kan* filters the list down to them;
  - the whole row opens the details. Targets are 44 px: add, stepper, ✕, tick box label;
  - the category head counts what is chosen ("✓ 3 gekozen");
  - on a phone the tick boxes hide behind *Meerdere kiezen*.
- **Choices in words:** `FindingWords.Requirement` folds variants of one skill:
  - "een Magiërspreuken A (Wit, Grijs of Zwart)" instead of "één van: …" with three full names;
  - groups of different skills still say "één van: …", with each skill's variants folded;
  - "4 XP in: …" folds the same way.
- **Feedback and Undo (P4):** a dock at the bottom of the screen.
  - After an add or remove, a toast says what happened, including what a chain brought along, and
    offers *Ongedaan maken*. *Nieuw plan* no longer asks; it gets Undo too.
  - Removing what other skills need still asks, in the dock, and the question takes focus.
  - Save and print outcomes are toasts too. Errors stay until closed.
  - Opening, importing and starting from a preset keep their banners: they describe the whole
    plan.
- **Presets (P9):**
  - the button on top, and the whole card clickable (the link's `::after` covers the card);
  - energy and hit points as chips;
  - two lines of text, and the four dearest skills with "+ n meer";
  - *Lees meer* (a `details`) holds the full text and list;
  - filter chips *Vechten · Magie · Genezing · Ambacht · Ritueel* are links, `?soort=`.
- **FAQ (P11):** each question a `details`, the first open. **Focus (P11):** after an add or "one
  less" replaces the row's button, focus goes back to the row.

### Decisions

1. **Toast for 5 s, not 4, and it waits** while the pointer or focus is on it: a keyboard user
   needs time to reach *Ongedaan maken* (WCAG 2.2.1). Errors stay until closed.
2. **The removal question moved into the dock.** The picker now has a stepper, so "one less" can
   start far from the chosen table. The question belongs where the click was.
3. **A chain is the main action only when the plan can pay for it.** Otherwise the row offers "⋯"
   like any other blocked row, so one click never overspends without saying so.
4. **Cost under the name, not in its own column:** the action buttons differ in width, so a cost
   column wandered from row to row.
5. **Preset kinds are derived, not stored:** a kind counts when the preset spends at least 4 XP in
   its categories (`PresetFacts`). No content change and no republish. Every preset gets one kind
   or two.
6. **Tick boxes stay on wide screens** (the README wish "add several at once"). On a phone they wait
   behind a toggle.
7. **Left for later:**
   - the XP bar's motion and the row flash (P4): there is no XP bar until P1;
   - the folding phone head (P11): it waits for P1/P2;
   - short names (P11): they need an SL/EC check.

### Gotchas

- **A parent's `OnAfterRenderAsync` runs before its child's.** When a click triggered both the
  removal question (`BuildScreen`) and the row's focus restore (`SkillPicker`), the child won and
  took focus from the question. The fix:
  - `Browser.RestoreFocusAsync` focuses only when focus was lost (the focused element left the
    DOM);
  - `Browser.FocusAsync` always focuses, for the question.
- **`@onclick:stopPropagation` without a handler** on the action span and the details keeps their
  clicks from also toggling the row.
- **Blazor encodes "+" as `&#x2B;`** in rendered HTML; match around it in specs.
- **The requirement wording reaches the PDF too:** `SheetComposer_specs` now expects "een
  Genezingsspreuken A (".
- **No hot reload:** the preview runs `dotnet run`, so restart it after a client change. Its
  `CharacterCreator/.claude/launch.json` is local and not committed, as no launch config is in this
  repository.
- Once, `localStorage.clear()` followed by `location.href = '/?voorbeeld=…'` in one script left
  the plan empty. Clicking a card or loading the address normally works every time. I didn't
  reproduce it.

### How it was checked

- **Specs:** 333 green: 22 new and 1 changed (`SheetComposer_specs`), covering:
  - the folding and the pills (`FindingWords_specs`);
  - kinds, defining skills and slugs on the real seed (`PresetFacts_specs`);
  - the card layout and the filter (`Presets_specs`);
  - the accordion (`Faq_specs`).
- **Release publish** of the WebApp, trimmed client included.
- **In the browser, at 1280×900 and 375×812:**
  - a chain add, its toast, and focus landing on the row's stepper;
  - Undo of *Nieuw plan*, which restores Arts with all 12 skills;
  - the removal question by keyboard: it takes focus, Tab and Enter pick *Alleen deze*, the toast
    appears, and focus returns to the row's "+";
  - the filter chips and *Lees meer*.
- **Measured:**

  | | Before | After |
  |---|---|---|
  | Presets page, desktop | 4,180 px | 2,241 px |
  | Button from the card's top | 822 px | 17 px |
  | Card height | ≈880 px | 300–390 px |
  | FAQ on a phone | | 866 px |

  The cards miss the review's ≈260 px: energy chips wrap to a second line on some.

### File map

| Path | What |
|---|---|
| `CharacterCreator.WebApp.Client/Components/SkillPicker.razor` | the rows, *Alleen wat kan*, *Meerdere kiezen*, focus restore |
| `CharacterCreator.WebApp.Client/Components/BuildScreen.razor(.cs)` | the dock: toast, Undo, the removal question; *Nieuw plan* without asking |
| `CharacterCreator.WebApp.Client/Words/FindingWords.cs` | folding variants (`Stems`, `Said`), `Status` |
| `CharacterCreator.WebApp.Client/Words/SkillStatus.cs` | the pill and its kinds |
| `CharacterCreator.WebApp.Client/Words/PresetFacts.cs` | preset kinds, defining skills, slugs |
| `CharacterCreator.WebApp.Client/Words/PlanSummary.cs` | `EnergyClass`, shared with the presets page |
| `CharacterCreator.WebApp.Client/Services/Browser.cs`, `WebApp/wwwroot/js/charactercreator.js` | `FocusAsync`, `RestoreFocusAsync` |
| `CharacterCreator.WebApp/Components/Pages/Voorbeelden.razor` | compact cards, filter chips |
| `CharacterCreator.WebApp/Components/Pages/Vragen.razor` | the accordion; the "not allowed" answer names Undo |
| `CharacterCreator.WebApp/wwwroot/app.css` | rows, pills, dock, chips, cards, accordion |
| `CharacterCreator.test/Words/*`, `Web/Presets_specs.cs`, `Web/Faq_specs.cs`, `Printing/SheetComposer_specs.cs` | the specs |
| `UX-REVIEW.md`, `DESIGN.md` §5 | status, and "as built after the UX review" |

### Next

Batch 2: P5 (the first visit's "Hoe wil je beginnen?") and P7 (two main actions, the rest under
*Meer*). Then P1, the sticky XP bar, which gives the toast its place above the bar.
