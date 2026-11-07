# Vortex Character Creator on the Vortex stack — design (draft 6)

Design phase, 2026-11-06. Nothing is built yet.

- **Draft 1:** the design, from reading the current builder, VOID and vortex-tools-style.
- **Draft 2 (Arie, 2026-11-06):**
  - Thorfgin is on board, and the first build is a proof of concept that steps on nobody's toes.
  - The host is `builder.the-vortex.nl`.
  - SL or EC publish rulesets.
  - Pushing a character into VOID waits for v2.
  - The build screen runs in the browser.
- **Draft 3 (Arie, 2026-11-06):**
  - The name stays *Character Creator*.
  - The proof of concept lives in Arie's `projects` repo for now, and runs on gembernodes for the
    demo.
  - v2 pushes new characters into VOID; v3 pushes changes to existing ones.
  - No field is mandatory.
  - The infobalie's list is in VoidWalker.
  - **VOID settles every rule disagreement.**
- **Draft 4 (Arie, 2026-11-06):**
  - The demo host is `charactercreator.gemberkoekje.nl`.
  - Thorfgin won't maintain it; on Vortex's private GitLab every developer can.
  - Keeping the rules and the 2026 text right is the job of SL and EC. §3.7 spells out what they
    need to do, and what VOID's screens allow today.
  - VOID learns the "3 XP left over" rule as a warning that never blocks.
  - In VOID, Referee and up may change the rules.
  - SL and EC edit the text in the app, from v4. Until then it goes through GitLab.
- **Draft 5 (Arie, 2026-11-07):**
  - The VOID change goes to a pushed branch (`feature/skills-requirements-api`, pushed
    2026-11-07), and to `develop` in time.
  - **The demo talks to VOID's develop instance, `void-test`** (API
    `https://test-void-api.the-vortex.nl/`), not to a recorded snapshot (§6, demo).
- **Draft 6 (Arie, 2026-11-07):**
  - **GitLab CI minutes are scarce.** All VOID work for the demo goes onto the one branch, which is
    pushed once when complete. One MR into `develop`; no repeated pushes to `develop`.
  - Arie refreshes `void-test` in Portainer once `develop` is built, and loads the rulebook draft
    there himself.
  - The demo signs in through `vortex-dev`, with its address added; the production client waits
    for the GitLab repo.

§11 lists everything that was settled.

**Where it lives:**

- **For now:** `CharacterCreator/` in the `projects` repo (GitHub `Gemberkoekje/projects`,
  **public**).
  - It is built in the worktree `C:\git\projects-charactercreator`, on the branch
    `claude/charactercreator`, because the main checkout is shared with other work.
  - The demo runs at `charactercreator.gemberkoekje.nl`.
- **After Thorfgin's yes:**
  - it moves to Vortex's private GitLab (`vortexadventures/character-creator`, proposed), where
    every Vortex developer can work on it, and to Vortex's hosting at `builder.the-vortex.nl`;
  - the `projects` folder and the gembernodes demo are removed.

**What I read for draft 1:**

- the live builder at builder.qequoia.nl. It runs entirely in the browser; its source is Thorfgin's
  `charactercreator` on GitHub, v0.7.4 of 2026-04-04;
- VOID's skill work from the envelope checkout (slice 4): `skill_requirements`, `SkillRules` and
  the rulebook draft *Vaardigheden 2026 V07*;
- VOID's rules for concept characters;
- vortex-tools-style 2.0.0, its Guild Hall register;
- Kluskat (in `projects`, deployed on gembernodes) for the build and deploy route.

> **To understand this, start by reading** §0 (at a glance), then §3 (the rules come from VOID),
> then §10 (phases). §1 is the current builder and §2 the decisions. §4–§9 are detail, §11 has the
> open and settled questions, and §12 the sources.

---

## 0. At a glance

- **Same job, new home.** A player picks skills. The Character Creator shows XP, prerequisites,
  energy per day, spells, techniques and recipes, and prints the result. As today, anyone can use
  it without signing in.
- **VOID is the source of truth.**
  - The Character Creator reads from VOID:
    - a skill's cost;
    - how often it may be bought, at creation and in total;
    - whether it is deprecated;
    - its energy and imbue cap, and whether it has a loresheet;
    - its **prerequisites** (`skill_requirements`, built for the checkout).
  - Where VOID and the rulebook (or the old builder) disagree, **VOID wins**. A wrong rule is
    fixed in VOID, and the Character Creator picks it up at its next import.
  - The Character Creator adds only what VOID does not hold:
    - names in Dutch and English, categories and rulebook pages;
    - descriptions, spells and techniques, recipes;
    - loresheet links and presets.
- **The book decides what is offered.** A VOID skill is in the picker only when the content names
  it. The 35 SL-only skills never appear, except on a signed-in player's own character. They are
  also never in either public repo.
- **Published rulesets.** A ruleset combines the content with an imported snapshot of VOID.
  - Once published it never changes.
  - Every plan records the ruleset it was made on.
  - An older plan opened on a newer ruleset lists what changed.
- **Signing in with a Vortex account (ERU) adds more:**
  - open your own VOID characters and keep plans on the server;
  - plan the next events: XP from the table, extra skills through a teacher, one per event;
  - print the list for your envelope.

  In v1 all of this only reads from VOID.
- **Pushing into VOID is for later versions**, and always lands *unvalidated*, for the infobalie
  to validate in VoidWalker (§7):
  - **v2:** a signed-in player pushes a new character;
  - **v3:** a signed-in player pushes changes to an existing character.
- **Planning never refuses anything, as in the checkout.** When a rule says no, the Character
  Creator gives the reason and offers *add anyway*, because items and imbues can meet a
  prerequisite. A push (v2, v3) does need a plan without problems.
- **Guild Hall**, the player register of vortex-tools-style:
  - parchment, the crest bar, Inknut Antiqua and Spectral;
  - light and dark from the shared `vortex-theme` cookie;
  - energy chips in the `--en-*` colours.
- **Our stack:**
  - .NET 10 with the layered template (`core` ← `application` ← `infrastructure` ← host);
  - Marten on PostgreSQL, and Qowaiv;
  - cookie + OIDC sign-in against ERU.

  The build screen runs in the browser (Blazor WebAssembly) on the same C# rules engine as the
  server.
- **Hosting:**
  - the demo: a GitHub workflow builds the image, and gembernodes runs it (Kluskat's route);
  - production: a Portainer stack behind the Caddy edge, with GitLab CI.
- **One small VOID change for v1:** `GET /skills` also returns each skill's prerequisite groups.
  It is on the pushed branch `feature/skills-requirements-api`, on its way to `develop`, whose
  instance (`void-test`) the demo talks to.

---

## 1. The builder today

**What it is.** *Vortex Character Creator*, by Thorfgin ("Deprecated Dodo").

- Built with React 18 and Vite, hosted on GitHub Pages at builder.qequoia.nl, under LGPL-3.0.
- No backend and no login.
- `Thorfgin/charactercreator` is the live one. `charactercreator2` is an older copy (2024 v0.6.1,
  Dutch only).
- It uses ruleset `2023-10b`, with text fixes up to 2026-04.

**What it does.**

- **Name and XP.** A name, then either:
  - the *new character* switch: 15 XP, basis skills only, at most 3 XP left over (the rest is
    lost);
  - or a free maximum XP, in quarter steps.
- **Two pickers**, one for basis and one for extra skills (extra is hidden for a new character).
  Add, raise or lower the count, remove, clear.
- **Prerequisite checks** on add, and on remove: a skill another skill needs cannot be removed.
- **Side panels:**
  - character properties (HP, AP, imbue and craft caps);
  - energy per day (elemental, spiritual, ritual, willpower, inspiration);
  - spells and techniques, and recipes.

  Tooltips show the description, cost, requirements and rulebook page.
- **The rest:**
  - loresheet links (PDFs on the-vortex.nl) and 15 presets (archetypes);
  - several characters saved in localStorage; export and import as a JSON file;
  - PDF export: cover, sheet, skills, spells, recipes, loresheets, XP per event;
  - NL/EN, FAQ, release notes, a disclaimer and some animated easter eggs.

**Its data** (`src/json/{nl,en}/`):

- `vaardigheden.json`: 84 basis and 98 extra skills. Each entry is `{id, skill, category, page,
  description, xp, Requirements, multi_purchase, maxcount, loresheet, Eigenschappen, Spreuken,
  Recepten}`.
- Requirements come in four shapes:
  - `skill`: all of these skills;
  - `any_list`: one of these skills;
  - `Category {name, value}`: XP in these categories;
  - `exception`: a druid, necromancer or demonologist may skip a spell's A prerequisite.
- `spreuken.json`: 8 categories of spells per skill, each with mana type and cost, incantation,
  effect, duration and page.
- `recepten.json`: crafting and alchemy recipes, each with inspiration, effect and components.
- `presets.json`, `faq.json`, `releasenotes.json`.
- Saves look like `{version, is_checked, max_xp, name, Skills: [{id, skill, count}]}`.

**What players asked for** (in its README, approved or under review):

- say *why* a skill can't be added;
- dark mode;
- add several skills at once;
- XP from the number of events;
- force-add a skill over the XP limit;
- imbues;
- add the prerequisite chain automatically (Harnas III also gives I and II);
- a bigger presets button.

**Why rebuild rather than patch:**

- **The rules are a copy, and copies drift.** The builder has its own 2023-10b rules, while VOID
  now holds the 2026 V07 prerequisites. They already disagree (§3.6).
- **There is no link to VOID.**
  - Its own FAQ says you can't submit a character to Vortex.
  - You can't start from your real character.
  - Its README lists "integration with VOIDWALKER / create new characters" as a wish that needs a
    server.
- **It is not on the Vortex look or the Vortex servers.** It has its own styling, no dark mode and
  one maintainer, outside Vortex's GitLab and hosting.

---

## 2. Decisions and assumptions

*Settled* marks what Arie confirmed. The rest are my calls from reading the code, and can be
overruled.

1. **A new codebase, not a port.** The React code stays Thorfgin's. The content carries over:
   descriptions, spells, recipes, presets, FAQ. *Settled.*
   - Thorfgin is on board, but won't maintain it.
   - Vortex's developers maintain the code on the private GitLab.
   - SL and EC answer for the rules and the text (§3.7).
2. **Guild Hall**, because players use it.
   - Its tokens and fonts come from vortex-tools-style.
   - The Character Creator writes its own component CSS on the role tokens, as ERU does.
3. **VOID is the source of truth, and settles every rule disagreement.** *Settled.* The
   Character Creator holds the text, and only what VOID does not know (§4).
   - A rulebook rule should count only once VOID knows it.
   - A rule can also be a **warning that never blocks**, in VOID and in the Character Creator.
     "At most 3 XP left over" is the first one (*Settled*, §3.3).
4. **The book decides what is offered** (§0).
5. **Published rulesets, not live reads.**
   - VOID's API needs a signed-in identity with a PLIN, and anonymous visitors have none.
   - A published snapshot also lets the rules team choose when changes go live in the Character
     Creator.
6. **The same rules as VOID's `SkillRules`:**
   - prerequisite groups of kind `any`, `points` or `extra`;
   - an extra skill is one with `base_max` 0;
   - whole XP only;
   - `times_max`.

   Shared test cases keep the two engines in step (§3.5).
7. **Planning refuses nothing, but a push must be clean.** VOID checks only `base_max` and XP on a
   concept, not prerequisites. So the Character Creator checks them before it pushes; otherwise
   the infobalie inherits the mess.
8. **One choice per VOID skill.** Where VOID splits a book skill in two (healer A elemental and
   spiritual, demonology B, …) the picker offers both, with the book's text on each.
9. **Signing in is optional.** Anonymous plans live in the browser (localStorage) and in exported
   files, as today.
10. **v1 never writes to VOID.** *Settled.*
    - v2 pushes new characters and v3 pushes changes to existing ones. Both use the player's own
      token, land *unvalidated*, and are validated by the infobalie in VoidWalker (§7).
    - Until v3, purchases for an active character go through the envelope at checkout.
11. **The UI is in Dutch and English**, as today. Code names are in English; the content is in
    both languages.
12. **The build screen runs in the browser** (Blazor WebAssembly on .NET 10). *Settled.* The
    other pages are rendered on the server.
    - Why not Blazor Server, like landerijen: the Character Creator is public and recalculates on
      every click. A server connection per anonymous visitor, and the reconnect overlay when a
      phone puts the tab to sleep, cost more than a second composition root.
    - The rules engine is plain C# in `core`, so the server checks a plan with the same code
      before it stores or pushes it.
13. **Marten documents, no event store.**
    - A ruleset is a document that is never changed.
    - A plan is a document with optimistic concurrency.
    - Nothing here needs more history than "which ruleset".
14. **Production host `builder.the-vortex.nl`.** *Settled.* A subdomain of the-vortex.nl shares
    the light/dark cookie, and builder.qequoia.nl will redirect there.
15. **SL or EC publish rulesets** (ERU `void.referee` or `void.eventcontrol`, or `*_*`).
    *Settled.* Infobalie alone is not enough. Whether this matches VOID's Referee-and-up is open
    question 1, and whether it becomes EC only is open question 2.
16. **The name stays *Character Creator***, in both languages. *Settled.*
17. **The proof of concept lives in `projects` and runs on gembernodes.** *Settled.*
    - **Both repos are public**, so neither may hold the SL-only skills, the SL file or a secret.
      The recorded VOID snapshot keeps only the skills the content names.
    - It follows the `projects` house style (Kluskat's `Directory.Build.props`: warnings are
      errors, implicit usings off, the analyzer stack). That is stricter than werkplaats, so the
      move to GitLab costs nothing.

---

## 3. The rules, from VOID

### 3.1 What VOID knows about a skill

From `GET /skills`. Any signed-in player may read it: `SkillsControllerPolicy::index` asks for
`Authorization::Player`.

| VOID field | Meaning in the Character Creator |
|---|---|
| `id`, `name` | the VOID skill, named in lower-case English (`lifeforce B`) |
| `cost` | XP per purchase |
| `base_max` | how often it may be bought at creation; **0 = an extra skill**: not at creation, and the first purchase goes through a teacher |
| `times_max` | how often it may be bought in total |
| `manatype`, `mana_amount` | energy per day, per purchase |
| `imbue_type`, `imbue_cap` | imbue cap, per purchase |
| `loresheet`, `blanks` | has a loresheet; prints blanks |
| `sort_order` | its place in lists |
| `deprecated` | never offered |
| *requirements* (new, §3.4) | prerequisite groups |

VOID does not hold the following, so they go in the content:

- Dutch names, categories, pages and descriptions;
- spells and techniques, recipes and loresheet URLs;
- the properties VOID does not calculate: hit points, armour points and craft caps (Thorfgin's
  `Eigenschappen`).

The properties are for display only. If VOID ever holds them, VOID's values win.

### 3.2 Prerequisites

From `skill_requirements` (on VOID's main since 2026-11; CHECKOUT-DESIGN §6). Every group of a
skill must hold.

| Kind | Holds when | Example |
|---|---|---|
| `any` | the character has one of the group's skills at least `amount` times | *Paladin*: one of healer A (elemental or spiritual), and armour I. *Group Ritual Specialist*: one of the two ritualisms, 3× |
| `points` | the character spent at least `amount` XP on the group's skills | *Brew Expertise*: 3 XP in alchemist skills |
| `extra` | the character has at least `amount` extra skills | *Teaching Expertise*: 1 extra skill |

- **Saying what is missing.** The Character Creator words a missing group the way VOID's
  `describe()` does ("one of …", "3× …", "3 XP in …", "1 extra skill"), in Dutch or English. That
  answers the players' first request.
- **Fixing it.** When every missing group is a single skill (Harnas III needs II, which needs I),
  *add with prerequisites* adds the whole chain and shows its XP. When there is a real choice (an
  `any` group of several skills), it lists the options.

### 3.3 Creation and growth

**A new character** (VOID's concept rules):

- 15 XP, VOID's default;
- each skill at most `base_max` times, which rules out extra skills;
- total cost no more than the XP.

**"At most 3 XP left over; the rest is lost"** (the old builder's rule). Arie, 2026-11-06: VOID
should know it, and warn without blocking.

- **In VOID:**
  - a constant (3);
  - a warning on a concept character with more XP left over, shown by VoidWalker on the character
    page and, from v2, on the *To validate* list;
  - the number in the API, so the Character Creator reads it at import.

  This goes into the same VOID MR as §3.4.
- **In the Character Creator:** a `Warning` finding under the XP counter ("3.5 XP left over;
  everything above 3 is lost"). It never blocks, and a push carries it along to the infobalie.
- **Until VOID's API gives it**, the Character Creator's ruleset holds the number (3), marked as
  such.

*As built in phase 5* (VOID branch `feature/skills-requirements-api`, `CHARACTER-CREATOR-NOTES.md`
there):

- **The constants:** `SkillRules::MAX_LEFT_OVER` (3) and `SkillRules::CONCEPT_XP` (15, also a new
  character's default XP).
- **The warning:** a concept character with more left over gets `warnings.left_over` on
  `GET /characters/{plin}/{chin}`. It is a warning, never a refusal.
- **The import:** `GET /skills` returns `creation: {start_xp, max_left_over}` beside the list.
  The import reads it into the published ruleset; the dry run says whether VOID gave it.
- **Not yet:** VoidWalker doesn't show the warning; that is a VoidWalker merge request of its own.

**After creation** (CHECKOUT-DESIGN §6, as the checkout applies it):

- **Whole XP only.** Quarter and half points stay saved.
- **A basis skill, or an extra skill bought again:** whole XP available must be at least the cost,
  and the skill must be below `times_max`.
- **An extra skill, the first time:**
  - it goes through a teacher, one per event;
  - it is paid off from free XP; that is the teaching card's progress;
  - while it is being paid off, free XP goes there first.
- **XP per event**, one character per player per event:

  | XP before the event | XP gained |
  |---|---|
  | below 20 | 1.5 |
  | 20 or more | 1.25 |
  | 30 or more | 1 |
  | 40 or more | 0.75 |
  | 50 or more | 0.5 |

The Character Creator uses these rules to look ahead, for example:

- "after the next event you have 2 whole XP";
- "you can afford Harnas III after 2 events";
- "Brew Expertise needs a teacher; with your XP it is paid off at the event after next".

### 3.4 The VOID change

- **What:** `GET /skills` and `GET /skills/{id}` also return the skill's requirement groups, as
  `requirements: [{kind, amount, skills: [ids]}]`.
- **How:** one `contain` in `SkillsTable`, the entity's compact fields, and tests.
- **Where:** branch `feature/skills-requirements-api` (commits `d4ed90a`, `72e705f`, pushed
  2026-11-07). `develop` builds the image that `void-test`, the instance the demo talks to, runs.
- **One branch, one push, one MR** (Arie, draft 6: GitLab CI minutes are scarce):
  - every VOID change for the demo collects on this branch, committed locally;
  - the branch is pushed once, when it is complete;
  - one MR goes into `develop`;
  - Arie refreshes `void-test` in Portainer after `develop`'s build.

  Nothing is pushed to `develop` piecemeal.
- **Gotcha found on the way:** VOID's entities use `LazyLoadEntityTrait`, so `has()` and `get()` load
  a missing association. The skill's `requirements` are shown only when the endpoint loaded them,
  and the check reads the loaded fields, or every character response would gain a query per skill.
- **Offline and in the specs**, the Character Creator reads an **offline snapshot** in VOID's
  `GET /skills` shape, embedded next to the content (`Content/{ruleset}/void-snapshot.json`,
  port `IOfflineVoidSnapshot`). *As built in phase 2b* it is **synthesized** by
  `scripts/make-offline-snapshot.py` from the public rulebook draft, the book's costs and
  Thorfgin's data: `void-test` has no draft loaded yet to record from. It holds book skills only,
  because `projects` is public, and what it seeds is marked provisional.
- **The same MR** also carries:
  - the "3 XP left over" warning (§3.3);
  - whatever SL and EC need in VOID's screens to keep the prerequisites right (§3.7).
- **v2 and v3** need a validation marker and VoidWalker's list (§7).

**Waiting outside the Character Creator**, from the checkout work:

1. Arie reviews `vaardigheden-2026-v07-review.md`.
2. The draft, and the SL file, are loaded with `bin/cake skill_requirements --apply`: on
   `void-test` for the demo, on live for going live.

At the checkout design's last update (2026-11-01) neither was done. The demo needs step 2 on
`void-test` (the review can follow); going live needs both on live.

### 3.5 Keeping two engines in step

VOID's `SkillRules` (PHP) and the Character Creator's engine (C#) must agree. Otherwise a player
plans something the desk won't do.

Proposal:

- **A cases file in void**, `tests/data/skill-rules-cases.json`. Each case has skills,
  requirements and a character's skills, plus the expected missing groups and XP.
- **Both test suites run it:** `SkillRulesTest` in void, and the Character Creator's specs.
- **The Character Creator vendors a copy**, and its CI fails when the copy differs from the void
  tag it names.
- **Book skills only**, because void is a public repo.

The file is on the pushed VOID branch (`tests/data/skill-rules-cases.json`). The Character
Creator's copy (`CharacterCreator.test/Rules/skill-rules-cases.json`) records the VOID commit it
came from and its hash; a spec fails when the copy is edited by hand. Comparing it with VOID itself
in CI waits for the move to GitLab, where both repos sit side by side.

### 3.6 What changes for players compared to the old builder

VOID settles these (decision 3). The Character Creator applies VOID's rules, and the list goes
into the release notes and the FAQ.

| Skill | Old builder (2023-10b) | VOID (2026 V07 draft) |
|---|---|---|
| *Uitdrijving* (exorcism) | 4 XP in priest skills | a priest A |
| *Extra Elementaire mana* | 4 XP in mage skills | 4 XP in mage spells **or** healer A (elemental) (Arie, 2026-10-29) |
| *Magiërspreuken B/C* Metaal, Vuur, Hout and *Priesterspreuken B/C* Reinheid, Dood, Natuur | a death druid, necromancer or demonologist may skip the A (`exception`) | no exception: B needs an A, C needs its B |
| skill list | 182 entries, with its own ids | 160 book entries; 206 live VOID skills, of which 171 are in the book and 35 are SL-only |

If the book says otherwise, VOID is corrected first, on its admin skills page or in its rulebook
draft. The next import brings the fix along. For the druid exception, an `any` group that also
lists those skills would do, without a new kind.

### 3.7 Keeping the rules and the text right: SL and EC

Arie, 2026-11-06: this is the job of SL and EC. It covers two separate things in two places:

| What | Where it lives | Who changes it today |
|---|---|---|
| **Rules**: cost, maxima, extra (`base_max` 0), energy, imbue cap, prerequisites | VOID (`skills`, `skill_requirements`) | only **Super**, on VOID's admin skills page; **Referee and up** once the §3.4 MR is in |
| **Text**: names, descriptions, pages, spells and techniques, recipes, loresheet links, presets, FAQ | the Character Creator's content files (§4.1) | a developer, through GitLab; **SL and EC in the app** from v4 |

#### What VOID shows and allows today

Checked on void `main`, 2026-11-06:

- **The edit page** (`/admin/skills/edit/{id}`, `templates/Admin/Skills/edit.php`).
  - It lists a skill's prerequisites under the skill form, one line per group, in VOID's wording
    ("one of mage A (white), mage A (gray), mage A (black)", "3 XP in …").
  - Each group has **Remove**. **Add prerequisite** takes a kind (`any`, `points`, `extra`), an
    amount and a multi-select of every skill.
  - **Only Super can open it.** `Admin/SkillsControllerPolicy`: `edit`, `addRequirement` and
    `deleteRequirement` all need `Authorization::Super`.
- **The skills list** (`/admin/skills`, Read-only and up) shows name, cost, loresheet, blanks,
  `times_max`, mana, imbue cap and deprecated.
  - It does **not** show the prerequisites, or `base_max`, so not which skills are extra.
  - An SL or EC can therefore **not see** a skill's prerequisites anywhere in VOID.
- **VoidWalker** shows no prerequisites, and **the API** doesn't return them (§3.4).
- **There is no overview.** Reviewing the whole book means opening ~170 edit pages one by one.
  The only overview is the review sheet in the repo (`vaardigheden-2026-v07-review.md`).
- **Changing a group** means removing it and adding it again; there is no edit in place.
- **The skill picker** is a plain 12-row multi-select of all ~206 skills, deprecated ones
  included. A group like "3 XP in alchemist skills" means ctrl-clicking 18 skills. The sets the
  loader knows (`@MagierA`, `@Alchemistenvaardigheden`) don't exist in the screen.
- **History.** Changes are written to VOID's history table (every table logs through `Table`),
  but no screen shows a skill's history. So there is no answer to "who changed this rule, and
  when".
- **Whether the rules are loaded on live** can't be seen from the code. At the checkout design's
  last update (2026-11-01) they weren't.
  - Quick check, as Super: open *lifeforce B* on the edit page. *None* under Prerequisites means
    nothing is loaded.
  - Or run the loader's dry run on the server: `bin/cake skill_requirements`.

**Verdict.** The data is shown properly, in plain words, and can be changed through the UI, but
only by Super. For SL and EC to own the rules, VOID needs, in order of importance:

1. **Let them see the rules:**
   - a *Prerequisites* column on `/admin/skills`, with the extra skills marked;
   - or, better, a read-only overview page of every skill with its prerequisites (the review
     sheet, live), for Read-only and up.
2. **Let them change the rules: Referee and up** (*Settled*, Arie, 2026-11-06).
   - VOID's rights are a ladder with Infobalie above Referee, so this also lets Infobalie, EC and
     Super in. Arie disagrees with that order, but it stands for now.
   - Arie still has to discuss with the others whether it should become EC only. He thinks not:
     this is exactly the kind of change an EC wants to delegate.
   - It covers `addRequirement` and `deleteRequirement`, and the rule fields on the edit page:
     `cost`, `base_max`, `times_max`, energy, imbue cap, loresheet, deprecated.
   - Renaming, adding and deleting skills stay Super only. A rename breaks the matching on names
     (the loader's and the Character Creator's).
   - That means splitting the edit page's form, or checking the rights per field (VOID already
     drops fields a caller may not edit).
3. **Make editing bearable:** pick from the named sets (alchemist skills, mage spells, …), hide
   deprecated skills in the picker, and edit a group in place.
4. **Show who changed what:** a history block on the skill's edit page.

1 and 2 are small, and belong in the §3.4 MR. 3 and 4 can follow.

*As built in phase 5* (same VOID branch):

- **1:** `/admin/skills` (Read-only and up) is the overview: maxima as creation / total with
  extra skills marked, and every prerequisite in words.
- **2:** Referee and up edit the rule fields and the prerequisites.
  - A skill's name, sort order and blanks stay Super only: shown disabled, and dropped from
    what others save.
  - Adding and deleting skills, and the API's `PUT /skills`, stay Super only.

#### What SL and EC need to do for the 2026 book

**In VOID (the rules):**

1. **Review the prerequisites draft**, `config/SkillRequirements/vaardigheden-2026-v07-review.md`
   in void: one line per book skill, with "Points to check" at the top. Corrections go into
   `vaardigheden-2026-v07.json` (a developer), or straight into VOID afterwards (step 4).
2. **Check the skill fields against the book:** cost, `base_max` (0 exactly for the *Extra
   Vaardigheden* chapter), `times_max`, energy, imbue cap, loresheet, deprecated. With today's
   screens that is the skills list plus the edit page, as Super.
3. **Have it loaded on live.** A developer with access to the VOID server runs
   `bin/cake skill_requirements` (dry run; it lists every name it can't match), then `--apply`,
   then the same with the SL file `skill-requirements-sl.json`. They also clear the old
   `teachings` rows the checkout notes mention.
4. **From then on**, every rule change happens in VOID's admin skills page. It takes effect in
   the checkout at once, and in the Character Creator at the next published ruleset.

**In the Character Creator (the text):**

5. **Bring the text up to 2026.** Thorfgin's text is the 2023-10b book with fixes up to 2026-04.
   SL and EC go through the 2026 book and note what changed: descriptions, spells and techniques,
   recipes, pages, loresheet links, presets.
   - The current books are the PDFs on the rules page, https://the-vortex.nl/het-spel/regels/ (§12).
   - The page shows no version numbers; the version is in the PDFs, and *Recente wijzigingen
     regelsysteem* lists what changed.
   - The books are Dutch only. The English text in the content is Thorfgin's translation, so
     keeping it current is part of the same job.
6. **Get the changes into the content.** Arie, 2026-11-06: this should not be a developer's job,
   but it needn't be in v1.
   - **Until v4:** on the private GitLab, a developer turns the notes into a merge request. An SL
     or EC with GitLab access can also use GitLab's web editor; the schema check in CI catches
     broken files.
   - **From v4:** SL and EC edit the content in the app (*Beheer → Content*), and their edits go
     into the next ruleset they publish (§10, phase 11).
7. **Publish the ruleset** (SL or EC, *Beheer → Rulesets*):
   - read the dry run: unmatched names, new VOID skills without text, rule differences;
   - publish.

   Do it after every batch of rule or text changes.

The yearly book: the same steps 1, 2, 4, 5, 6 and 7, with the new book's names matched in the
dry run.

---

## 4. Content: what the Character Creator adds

### 4.1 One entry per offered skill

*As built in phase 2a.* The content lives per ruleset in
`CharacterCreator.infrastructure/Content/2026-v07/` (`skills.json`, `presets.json`, and
`IMPORT-REPORT.md` for SL and EC), embedded in the assembly. There is one entry per skill the
picker offers, which is one per VOID skill (decision 8):

```json
{
  "key": "healer-a-elemental",
  "void": "healer A (elemental)",
  "book": ["Genezingsspreuken A"],
  "name": {"nl": "Genezingsspreuken A (elementair)", "en": "Healing Spells A (elemental)"},
  "category": {"nl": "Genezingsvaardigheden (EL)", "en": "Healing Skills (EL)"},
  "page": 38, "pageFrom": "2026-v07", "bookCost": 4,
  "description": {"nl": "…", "en": "…"},
  "properties": [{"name": "elemental_mana", "value": 5}],
  "spells": [{"id": 101, "name": {"nl": "Discern Wounds", "en": "…"}, "cost": "1", "page": 41, "…": "…"}],
  "recipes": [],
  "loresheet": {"url": "", "page": 0, "extra": ""}
}
```

- **`key`** is a slug of the VOID name. One entry per VOID skill makes VOID's name the natural join,
  and VOID's own loader matches on names too. Plans store keys, not VOID ids: ids differ between
  `void-test` and live. A VOID rename shows up in the import's dry run.
- **`book`** names the 2026 book's entry or entries. Two book skills VOID files under one (*Extra
  Elementaire mana* and *Extra Genezingsmana*) share an entry. A book skill VOID splits
  (*Genezingsspreuken A*) becomes two entries with the same text, named "(elementair)" and
  "(spiritueel)".
- **Names** come from the 2026 book, through VOID's rulebook draft
  (`config/SkillRequirements/vaardigheden-2026-v07.json`, public). Only skills in that draft go in,
  so no SL-only skill can reach this public repo.
- **The text, spells, recipes, properties and loresheets** are Thorfgin's (2023-10b, fixed up to
  2026-04). `scripts/import-thorfgin-content.py` matches his skills to the book by name, including his
  "(EL)"/"(SP)" variants. For the 2026 draft it matched every skill: 143 by name, the rest through
  the variants. 11 entries took the text of several of his skills; IMPORT-REPORT.md lists them for
  review.
- **`page` and `bookCost`** come from the current PDFs (2026 V07):
  `scripts/check-against-rulebook.py` finds each skill's heading ("Naam (2 punten)") and each spell.
  All 171 skills were found; three spells weren't (probably renamed) and are in the report.
  `bookCost` lets the VOID import flag where VOID's cost differs from the book's (VOID's wins,
  decision 3).
- **SL and EC** bring the text up to the 2026 book (§3.7, steps 5 and 6).

### 4.2 Import and publish

An import combines a content version with a VOID snapshot. A dry run lists, before anything is
stored:

- book entries with no VOID skill, and VOID names that match twice;
- VOID skills no entry names. The SL-only skills are expected here; anything else is new in VOID;
- deprecated VOID skills the content still offers;
- prerequisites that name a skill outside the book;
- differences from the published ruleset: costs, maxima, energy, prerequisites, and skills added
  or removed.

*Publish* stores the ruleset: the content version, the VOID snapshot and the date. It is never
changed afterwards. Anonymous visitors get the newest published ruleset.

*As built in phase 2b:*

- **One document per ruleset** (`PublishedRuleset`, in Marten), id `{content ruleset}@{UTC moment}`,
  such as `2026-v07@20261107T084541Z`. It holds VOID's rules per book skill, the content, the
  creation rules, who published it and from where, and whether it is **provisional** (from the
  synthesized snapshot). A second publish in the same second is refused, never overwritten.
- **A skill outside the book is never named in it.** A prerequisite on one refers to it as
  `hidden-{VOID id}`. Its name appears only in the dry run, which only SL and EC see.
- **The dry run also lists** costs that differ from the book (VOID's count), the energy and imbue
  types VOID names, and why the rules could not be published (the engine refuses them, or not one
  book skill was found in VOID).
- **Publishing imports again** and stores the result, so what is stored is what VOID says at that
  moment, not what the dry run showed earlier.
- A skill VOID retired keeps its rules (characters that have it need them) but isn't offered.

- **In the demo** (draft 5): an SL or EC signs in and imports from `void-test`, VOID's develop
  instance. Until somebody has, the first start publishes the bundled content with a recorded
  snapshot, so the build screen is never empty.
- **From phase 7:** an SL or EC opens *Rulesets*, picks the content version and imports from live
  VOID with their own token.

### 4.3 Plans across rulesets

- A plan stores its ruleset id and a `{key, times}` per skill.
- Opened on a newer ruleset, it shows the differences that touch it ("Uitdrijving now needs a
  priest A", "Rekenen costs 2") and offers to move.
- Thorfgin's saves import through his id-to-key map (§4.1). The differences between 2023-10b and
  the current ruleset are listed the same way.

*As built in phase 3:*

- **The plan file** (`PlanFile`) is `{format: "vortex-character-creator", version, id, name, mode,
  xp, ruleset, savedAt, skills: [{key, times}]}`.
  - The browser keeps the plan being worked on (`charactercreator.werk`, after every change) and
    a list of saved plans (`charactercreator.opgeslagen`).
  - Export and import use the same JSON.
- **Thorfgin's saves:** `legacy.json` next to the content maps each of his 182 skills (id and
  Dutch name) to a key. `PlanImport` reads:
  - his `.dat` export, which is base64 of the URI-encoded JSON;
  - his 2023-10b format, which names skills by id;
  - his 2023-10a format and the 2023-10 array, which name skills by name.

  A skill with no entry now is reported by its old name. His `is_checked` becomes a new
  character; otherwise it is free planning with his `max_xp`.
- **Opened on another ruleset**, the build screen says so, and the findings show what breaks. The
  per-skill list of differences against the old ruleset waits for `GET /api/rulesets/{id}`.

---

## 5. Screens

**Crest bar** (`header.vtx-header`):

- the crest, "Vortex Adventures" and *Character Creator*;
- at the end: `.vtx-user` (when signed in), the theme toggle, NL/EN, sign in or out;
- a second row (`nav.vtx-header-nav`): *Bouwen* / *Build*, *Mijn personages* / *My characters*
  (when signed in), *Voorbeelden* / *Presets*, *Vragen* / *FAQ*.

The footer is the portal's.

**Build** is the main screen. On a phone the same blocks are stacked, with the summary as a tab.

```
┌ Name [Ilse van de Vijfhoek ]  ○ New character ● Existing ○ Free  XP 23.5 · spent 21 · left 2 ┐
│ ┌ Skills ──────────────────────────────┐ ┌ Energy per day ───────────────────────────────┐ │
│ │ [search…]  Basis | Extra | All       │ │ (●) Elemental 6  (●) Willpower 3              │ │
│ │ ▸ Pantser                            │ ├ Properties ───────────────────────────────────┤ │
│ │   Harnas II        2  ✓ can buy  [+] │ │ HP 3 · AP 2 · Glyph imbue cap 20              │ │
│ │   Harnas III       3  needs Harnas II│ ├ Spells & techniques (12) ─────────────────────┤ │
│ │                       [+ with chain] │ │ tiles…                                        │ │
│ │ ▸ Magiër  …                          │ ├ Recipes (3) ──────────────────────────────────┤ │
│ └──────────────────────────────────────┘ │ tiles…                                        │ │
│ ┌ Chosen ─────────────────────────────────────────────────────────────────────────────┐   │
│ │ Skill            XP  ×   Loresheet  Notes                                           │   │
│ │ Harnas I          1  1               −  ×                                           │   │
│ │ Magiërspreuken B  3  1   ⧉           ⚠ added anyway: needs one of Magiërspreuken A  │   │
│ └─────────────────────────────────────────────────────────────────────────────────────┘   │
│ [Print]  [Save]  [Export file]                                       2 XP left            │
└───────────────────────────────────────────────────────────────────────────────────────────┘
```

- **States in words.** The picker shows each skill's state: *can buy*, *needs …*, *needs a
  teacher*, *at maximum*, *not enough XP*. Finding out why not never takes a click.
- **Add anyway.** A skill can still be added; it then carries the warning.
- **Removing.** Removing a skill other skills depend on names them, and offers *remove these too*.
- **Several at once.** Several skills can be ticked and added in one go.
- **Energy chips** use Guild Hall's `.echip` with the `--en-elemental`, `--en-spiritual`,
  `--en-willpower` and `--en-inspiration` tokens (and the ritual ones).
- **Spells and recipes** are tiles with a details panel: incantation, cost, effect, duration,
  page.

*As built in phase 3:*

- **Layout:** as sketched on a wide screen; on a phone, the chosen table comes first.
- **No loresheet column:** the loresheet link sits under the skill's name.
- **Modes:** *new character* and *free*; *existing* comes with *Mijn personages* (phase 7).
  - Free planning has an "after N events" helper, which fills the XP from the XP table.
- **Chains:** the chain button says when a choice is left to the player.
- **Removing:** a removal that breaks other skills asks *remove those too / only this one /
  cancel*.
- **Several at once:** ticked skills are added with one button.
- **NL/EN:** a link in the crest bar sets a language cookie (`charactercreator.taal`) and reloads
  the page. Beheer and the sign-in pages stay Dutch.

*As built after the UX review, batch 1 (UX-REVIEW.md P3, P4, P9, P11):*

- **Picker rows:** one status pill (✓ chosen, ⚠ needs …, ✗ too dear, ⓘ teacher; none for "can
  buy") and one main action:
  - "+", or the stepper for a skill the plan has;
  - a named chain ("+ met Harnas II · 4 XP") when it decides everything and fits the XP left;
  - otherwise "⋯", which opens the details with the chain and *add anyway*.

  Rows that can't be bought are dimmed. *Only what fits* filters the list, and the whole row opens
  the details.
- **Choices in words:** variants of one skill are said once: "een Magiërspreuken A (Wit, Grijs of
  Zwart)". The screen and the PDF both use this.
- **Undo instead of "are you sure?":** adding, removing and *new plan* happen at once. A toast at the
  bottom then says what happened and offers *Undo*. Removing what other skills need still asks,
  in that same spot.
- **Several at once** stays: the tick boxes show on wide screens; on a phone they sit behind *Pick
  several*.

*As built after the UX review, batch 2 (P5, P7):*

- **The head** holds the name with *Mijn plannen ▾* (the saved plans, as a document app lists its
  files) and the kind of plan. Under them is a bar with:
  - the XP counters;
  - a *Voorlopige regels ⓘ* badge instead of the banner;
  - *Opslaan* and *Afdrukken*;
  - *⋯ Meer*: export, import, new plan.

  P1 will make this bar sticky.
- **A first visit** (an empty plan) shows *Hoe wil je beginnen?* instead of empty cards:
  - one preset per main kind, and *Alle 15*;
  - reading a file from the old builder;
  - *Zelf kiezen*, which goes to the search box.
- **Undo everywhere a plan is replaced:** starting from a preset, opening a saved plan, importing.
  The old builder's import keeps its banner, which explains what changed.

**Voorbeelden / Presets**: the presets as cards, with archetype, text and skills, and *start from
this*. As built: *start from this* opens the build screen with `?voorbeeld={key}`, which asks
first when the current plan has skills. After the UX review the cards are compact:
- the button on top, and the whole card clickable;
- energy and hit points;
- two lines of text and the four dearest skills, the rest under *Lees meer*.

Filter chips (*Vechten · Magie · Genezing · Ambacht · Ritueel*) are links (`?soort=`). A preset counts
as a kind when it spends at least 4 XP in that kind's categories.

**Mijn personages / My characters** (signed in):

- The player's VOID characters, each with name, status, XP and whole XP available.
- Opening a character makes a plan of it. Its current skills are fixed (shown, not removable); the
  plan holds the new purchases.
- *Next events* lays the plan out per event, using the XP table, the one-extra-skill-per-event
  rule and the teaching pay-off.
- It prints **the list for the envelope**: per skill, the name as VOID spells it and its cost,
  plus a box for the teacher's PLIN-CHIN on an extra skill. These are the fields the checkout's
  skill rows ask for.
- Skills the player holds that the book doesn't name (SL-only skills, rank skills) are shown by
  their VOID name, without explanation.

**Send to Vortex** (v2 for a new character, v3 for changes; signed in, a plan without problems):
see §7.

**Print**: a PDF in Guild Hall's print look, which is always light. It holds the sheet (name, XP,
properties, energy), the skills, spells and techniques, recipes, loresheet links and XP per event.
It is made on the server.

*As built in phase 4:*

- **The flow:** *Afdrukken (PDF)* on the build screen posts the plan the browser keeps to
  `POST /api/plans/sheet`. The plan is checked against the current ruleset, as on the screen, and
  comes back as `{name}.pdf`, in the visitor's language (the language cookie).
- **Page 1 is the sheet:**
  - name, kind of plan and XP, the rules it was checked against;
  - the numbers (XP, spent, left, hit points, armour points, imbue and craft caps) as boxes;
  - energy per day;
  - a *Let op* box with what breaks or warns;
  - the skills table: times, XP, page, notes.
- **The following pages:** the skills' texts, spells and techniques, recipes, the loresheets
  (real links) and XP per event (the next 10 events from the plan's XP). These flow on rather
  than each starting a page.
- **Wording:** the server words the sheet with the client's `FindingWords` and `PlanSummary`, so
  the PDF and the screen say the same.
- **The writer** is MigraDoc (PDFsharp 6.2.4, the last MIT release, pinned as in landerijen). It
  embeds Spectral and Inknut Antiqua as TTF (OFL), so it needs no system fonts: checked in the
  Linux container image.
- **The endpoint is anonymous:**
  - 64 KB body limit, at most 300 skills;
  - 30 sheets per minute per address;
  - `Cache-Control: no-store`;
  - the plan is neither stored nor logged.

**Beheer** (SL and EC, from phase 7): *Rulesets* (§4.2) and the content check.

---

## 6. Architecture

```
CharacterCreator.core            rules engine and model: Ruleset, RuleSkill, RequirementGroup, Plan,
                                 PlanEvaluation, Finding; plain C#, Qowaiv only, no I/O
CharacterCreator.application     ports (IVoidClient, IRulesetStore, IPlanStore, IContentSource,
                                 ISheetWriter) and handlers
CharacterCreator.infrastructure  MartenRulesetStore, MartenPlanStore, VoidHttpClient,
                                 EmbeddedOfflineSnapshot, EmbeddedContentSource, MigraDocSheetWriter
CharacterCreator.WebApp          host: composition root, OIDC, server-rendered pages, /api endpoints
CharacterCreator.WebApp.Client   the build screen (Blazor WebAssembly); references core only
CharacterCreator.test            specs: engine, handlers, HTTP, schema, the shared VOID cases
```

**Templates:**

- Kluskat (`projects`, cloned from werkplaats) for the `projects` plumbing: build props, the
  GitHub workflow, the Dockerfile and the gembernodes deploy;
- landerijen for Blazor and the ERU sign-in.

**Deltas from those:**

- **A WebAssembly client project.** It is a second composition root (the browser's), and may
  reference only `core`.
- **Anonymous pages.** The fallback policy is not "authenticated". Only *Mijn personages*,
  `/api/me/*` and *Beheer* need a sign-in.
- **No ASP.NET Identity and no app password.** Cookie + OIDC against ERU only, as in landerijen,
  and gated on configuration: without the Keycloak settings the app runs anonymous only, as the
  demo does.

**The engine returns facts, not sentences** (landerijen's rule):

- It returns `Finding` codes with arguments: `MissingGroup(kind, amount, skills)`,
  `NotEnoughXp(need, have)`, `NeedsTeacher`, `AtMaximum`, `TooMuchLeftOver(left, limit)`.
- Each finding has a severity:
  - a **problem** breaks a rule VOID enforces, and stops a push (never planning);
  - a **warning** is a rule VOID only warns about (3 XP left over); it never stops anything.
- The UI and the PDF put them into Dutch or English.
- Enums get an empty first member.

**Data flow:**

- The client loads the current ruleset once (`/api/rulesets/current`, its id as the ETag, then
  304s). *As built in phase 2b* it carries both languages, so switching language reloads nothing.
  Measured: 555 KB of JSON, 93 KB gzipped, 84 KB with Brotli (at the optimal level; the default,
  fastest level gave 222 KB).
- The client evaluates plans itself. The server evaluates again whatever it stores or pushes.

**VOID is called from the server only**, a backend-for-frontend:

- The access token stays in the auth cookie's ticket (`SaveTokens`, refreshed), never in the
  browser, and the browser talks to one origin.
- For reading the player's data:
  - `GET /players/{plin}`, the player with their characters (as the portal does);
  - `GET /characters/{plin}/{chin}`: skills, XP, teacher;
  - `GET /factions`.
- For the import: `GET /skills` and `GET /manatypes`.
- For v2: `PUT /players/{plin}/characters`, then `/characters/{plin}/{chin}/skills[/{id}]` with
  `PUT` or `DELETE`.
- **The demo calls `void-test`** (`Void:BaseUrl` = `https://test-void-api.the-vortex.nl/`; the
  app refuses to start when it doesn't end with a slash), VOID's develop instance. It trusts the same Keycloak realm as live (VoidWalker-test signs in with
  `vortex-spa` on `auth.the-vortex.nl/realms/vortex`), so a real ERU token works there.
- **Offline and in the specs**, the embedded offline snapshot (§3.4) stands in for
  `VoidHttpClient`: the first start publishes it, and the specs build rulesets from it.
- **No CORS involved.** CORS is a browser rule; these are server-to-server calls.
  - Reachability checked 2026-11-06: a throwaway pod on gembernodes got `401` from
    `https://void-api.the-vortex.nl/skills` (no token, as expected) in 0.1 s.
  - The only thing that keeps a hosted copy from calling VOID for a player is the sign-in: it
    needs a Keycloak client that accepts its redirect URI (see Keycloak below).

**Keycloak:**

- **Production:** a new confidential client in the realm (proposed `vortex-character-creator`),
  created at go-live.
  - It needs the audience mapper that puts `vortex-api` in the token, because VOID checks `aud`.
  - Redirect URIs for builder.the-vortex.nl.
- **Local development:** `vortex-dev`, as the other tools use. Only developers may use it, it is
  public with PKCE, and it takes localhost redirects only.
- **The demo** signs in through `vortex-dev` (Arie, draft 6).
  - Arie adds `https://charactercreator.gemberkoekje.nl/signin-oidc` and `/signout-callback-oidc`
    to its redirect URIs. It already carries the `vortex-api` audience and is public with PKCE, so
    the demo needs no secret.
  - Only developers may sign in through `vortex-dev`, which suits a demo. Players can still use the
    demo anonymously.
  - The production client waits for the GitLab repo.

**Marten:**

- `Ruleset`: never changed after publishing; ids like `2026-v07@2026-11-20`.
- `Plan`: PLIN, name, ruleset id, mode, start XP, skills, a link to the pushed character
  `{plin, chin}` (v2), and a version for optimistic concurrency.
- Anonymous plans are never stored on the server. Printing sends one, only to make the PDF
  (phase 4).

**PDF:** PDFsharp-MigraDoc (as in landerijen's print pack), with Guild Hall's fonts. As built:
`dist/fonts` has only WOFF2, which PDFsharp can't read, so the TTFs come from the portal's crest bar
(`assets/fonts`, OFL): Spectral Regular and SemiBold, Inknut Antiqua SemiBold and Bold. There is no
italic face; PDFsharp slants Spectral.

**Styling:**

- `vendor.ps1 -Register GuildHall` copies the package into `wwwroot/lib/vortex-tools`.
- In `<head>`: `vortex-theme.js`, then `guildhall.min.css`, then the app's own CSS.
- Colours by role token only (README, *Using Guild Hall*).
- The server renders the theme from the cookie, so the page doesn't flash.
- On the demo host the cookie isn't shared with the-vortex.nl, so the toggle there remembers its
  own choice.

**Config** (colon paths; secrets are empty strings in `appsettings.json`, with the values from the
environment):

- `ConnectionStrings:Postgres`;
- `Void:BaseUrl` (the demo: `https://test-void-api.the-vortex.nl/`), or `Void:SnapshotFile` offline;
- `Keycloak:Issuer`, `Keycloak:ClientId`, `Keycloak:ClientSecret`;
- `CharacterCreator:PublicBaseUrl`.

**Demo on gembernodes** (Kluskat's route, Kluskat's `DEPLOY.md` as the recipe):

- **Build:** `.github/workflows/charactercreator-build-and-push.yml` in `projects` builds and tests
  on every push. On `main` it publishes and pushes `ghcr.io/gemberkoekje/charactercreator:<sha>`.
- **Manifests:** in gembernodes, `apps/charactercreator/` (deployment, service, kustomization, a
  `OnePasswordItem`), `namespaces/charactercreator-namespace.yaml` and
  `ingress/charactercreator-ingress.yaml` for `charactercreator.gemberkoekje.nl`.
  - The `*.gemberkoekje.nl` wildcard already points at the cluster.
  - Every deploy is an image-tag bump in a gembernodes PR.
- **Database:** the cluster's shared PostgreSQL. Arie creates the role and database
  `charactercreator`, and the 1Password item `charactercreator-secrets` (vault `Gembercluster`)
  with `ConnectionStrings__Postgres`, plus `Keycloak__ClientSecret` if the demo's client is
  confidential.
- **VOID:** `Void__BaseUrl=https://test-void-api.the-vortex.nl/`, and the Keycloak settings once
  the demo's client exists.
- **One pod.** The image runs as non-root, with `/healthz` for the probes.

**Production** (after the move to GitLab):

- docker-compose (postgres and the app) locally, on ports clear of werkplaats (5433/8081) and
  landerijen (5434/…): 5435/8084;
- a Portainer *Repository* stack on endpoint 3, joined to the `edge` network, with Caddy serving
  `builder.the-vortex.nl`;
- the GitLab deploy job copied from werkplaats: re-send the stack's environment variables, and use
  the API token plus Caddy basic auth;
- CI jobs are manual buttons while CI minutes are short.

---

## 7. v2 and v3: pushing into VOID, unvalidated

Arie (2026-11-06): in the long term a signed-in player can push both a **new character (v2)** and
**changes to an existing one (v3)** straight into VOID. Both land *unvalidated* and show up in
VoidWalker for the infobalie. Each version gets its own design pass first; this section is the
starting point.

### 7.1 What VOID already has, and lacks

**What it has.** A player may make and edit **one** concept character through the API, so v2
needs no new rights to create one:

- `CharacterPolicy::canEdit` lets the owner edit while `status` is `concept`;
- `CharactersTable::ruleOnlyOneConcept` allows one concept per player;
- `CharactersSkillsTable::ruleLimitConceptCharacter` checks `times ≤ base_max` and that the total
  cost is no more than the XP;
- VOID refuses a deprecated faction.

**What it lacks:**

- a way to tell the infobalie "a player made this; check it";
- for v3, any way for a player to change an active character: only Referee and up may edit one.

### 7.2 The proposal

*Validated* and *active* are separate things: the infobalie checks a character before the event,
and activates it when it is first played.

- **A validation marker on `characters`**: empty means *unvalidated*; filled means when and by
  whom it was validated.
- **The owner may edit an unvalidated concept**, and no longer once it is validated. That is one
  extra condition in `CharacterPolicy` / `CharactersSkillPolicy`.
- **VoidWalker gets a *To validate* list** (*Settled: in VoidWalker*): unvalidated characters,
  oldest first.
  - Each row shows the character, any findings the player overruled, and a link to the character
    page.
  - The character page shows the *unvalidated* badge and a **Validate** action.
  - The infobalie can correct skills first.
- **Activation at the first event is unchanged.** Setting the status to active also makes the
  player's other characters inactive.

### 7.3 v2: a new character

No field is mandatory (*Settled*): a player often doesn't know their faction yet.

- **Faction:** left empty, VOID stores its `-` faction (`CharactersTable::nameToId`).
- **Belief, group and world** may be empty too.
- **Name:** VOID requires a non-empty name (`CharacterValidator::notEmptyString('name')`) that is
  unique among the player's characters.
  - Without a name, the Character Creator sends a placeholder, *Nieuw personage* (the player's
    language), which the player or the infobalie changes later.
  - VOID's rule stands, because VOID is the source of truth.

The steps:

1. **Check.** The server evaluates the plan again: *New character* mode, no problems. Warnings
   (3 XP left over) go along to the infobalie.
2. **Create.** `PUT /players/{plin}/characters` with whatever fields the player filled in. VOID
   gives the CHIN (the player's highest + 1) and 15 XP, and the character starts unvalidated.
3. **Sync the skills.** One `PUT …/skills` per skill. VOID has no transaction across calls, so
   this step is a sync: make VOID's skills equal to the plan (add, change `times`, delete).
   - It is safe to repeat after a failure.
   - It runs again for later edits, until the character is validated.
4. **Link.** The plan stores `{plin, chin}`. VOID's errors (a second concept, a name already used)
   come back as the reason, in the player's language.

After validation the Character Creator shows the character read-only. After activation it treats
it as an existing character.

### 7.4 v3: changes to an existing character

The planned purchases on an active character, pushed as **pending changes** for the infobalie to
validate. The player's VOID data stays as it is until then.

Questions for v3's design pass:

- **Where pending changes live:** in VOID (a pending-skills table the *To validate* list shows),
  or as a wish list the envelope checkout reads into its skill rows (the checkout already plans
  teachers, XP after the event's award, and the card reprint)?
- **Teaching:** an extra skill needs a teacher and is paid off over events. Validating it means
  starting a teaching, which is checkout logic today.
- **Timing:** XP comes in at checkout. Is a change pushed before the event checked against the XP
  after the award, as the checkout does?

---

## 8. Access

| Who | Can |
|---|---|
| anyone | build, view presets, print, save in the browser, export and import a file |
| signed in (any ERU account with a PLIN) | the above, plus: their own VOID characters (read only), plans on the server, the envelope list; in v2 and v3, pushing |
| SL or EC | the above, plus: importing and publishing rulesets |

- **The SL-or-EC policy** is one policy, `Beheer`. It is met by any of the ERU permissions
  `void.referee` (SL), `void.eventcontrol` (EC) or `*_*`, read from
  `resource_access.vortex-api.roles` at sign-in, as landerijen does.
  - The permissions are listed by name, not taken as a ladder, so `void.infobalie` alone does not
    qualify. Adding it would match VOID's Referee and up (§11, open question 1).
  - Making it EC only means removing `void.referee` from the policy (under discussion; Arie leans
    against it).
  - There is no in-app override.
  - From v4 the same policy also covers *Beheer → Content*.
- **No PLIN.** A player without a PLIN can sign in, but gets only the anonymous features, plus a
  line explaining why.
- **The demo** runs anonymous only until it has a Keycloak client of its own (§6, Keycloak). With
  one, an SL or EC signs in there to import from `void-test`.

---

## 9. Testing and checking

- **Engine specs** per rule and per finding, plus the shared VOID cases (§3.5).
- **A ruleset spec** that loads the current content against the recorded VOID snapshot:
  - every book entry is matched and every prerequisite resolves;
  - the snapshot holds no skill the content doesn't name, which guards the public repo.
- **Handler specs** with fake ports.
- **HTTP specs** with a throwaway PostgreSQL and a stand-in VOID. When a real VOID is needed, the
  checkout's `checkout-e2e/` shows how to run one locally.
- **Content schema specs**: Thorfgin's ajv tests, in C#.
- **Screens:**
  - add the Character Creator to vortex-tools-style's `screens.config.mjs` (both themes, phone and
    desktop) and run the contrast audit;
  - do a keyboard-only pass over the build screen.

---

## 10. Phases

Each phase ends with a NOTES.md entry: decisions, gotchas, a file map, and *to understand this,
start by reading…*.

### The proof of concept

Phases 0–5 happen in `projects` (worktree `C:\git\projects-charactercreator`, branch
`claude/charactercreator`). Of Vortex's they touch only:

- the pushed VOID branch, on its way to `develop`;
- `void-test`, which the demo reads from;
- the demo's Keycloak redirect (Arie).

There is no Character Creator GitLab project yet.

0. **Scaffold.** *Done 2026-11-07.*
   - `CharacterCreator/`, with Kluskat's build props and layers, and the WebAssembly client
     project.
   - Guild Hall vendored, the crest bar shell.
   - OIDC wired but gated on configuration, so it is off without settings.
   - The GitHub workflow, the Dockerfile and docker-compose.
   - In VOID: the `GET /skills` requirements change (§3.4) and the shared cases file (§3.5), on
     `feature/skills-requirements-api`.
1. **Engine.** *Done 2026-11-07.*
   - The model, prerequisites, creation and growth rules, findings (problems and warnings), the
     XP table.
   - Specs and the VOID cases. No UI.
2. **Content, rulesets and VOID.** *Done 2026-11-07* (2a the content, 2b the rest).
   - The content schema, and Thorfgin's text mapped to keys, checked against the current PDFs on the
     rules page (§12): the book's version, page numbers and loresheet links.
   - The seeded `void` names.
   - `VoidHttpClient`, the import dry run, publishing, and *Beheer → Rulesets* for a signed-in SL
     or EC. This comes forward from phase 7, because the demo imports from `void-test`.
   - An offline snapshot (book skills only) for the specs, for offline work and for the first
     start. Synthesized for now (§3.4); replaced by the first real import from `void-test`.
   - The ruleset API.
3. **Build screen.** *Done 2026-11-07.*
   - Picker, the chosen-skills table, reasons, add with chain, remove with dependants.
   - Summary panels and presets.
   - localStorage, file export and import (including old saves).
   - NL/EN, light and dark.
4. **Print**: the PDF. *Done 2026-11-07.*
5. **Demo on gembernodes.**
   - Arie creates the database role and the 1Password item.
   - Arie adds the demo's redirect URIs to `vortex-dev` (§6, Keycloak).
   - **VOID, once** (draft 6):
     - first complete the branch with the rest of v1's VOID work: the "3 XP left over" warning and
       its number in the API (§3.3), and SL/EC seeing the rules with Referee and up changing them
       (§3.7, verdict items 1 and 2);
     - run VOID's full test set locally;
     - push once, and open one MR into `develop`.
   - After `develop`'s build, Arie refreshes `void-test` in Portainer and loads the rulebook draft
     there (`bin/cake skill_requirements`, dry run first, then `--apply`).
   - The gembernodes manifests and the ingress, then an image-tag PR.
   - Sign in, import from `void-test`, publish.
   - Show Thorfgin at `charactercreator.gemberkoekje.nl`.

### After Thorfgin's yes

6. **Move to Vortex.**
   - Create the private GitLab project, and carry `CharacterCreator/`'s history over
     (`git subtree split`).
   - Swap the GitHub workflow for GitLab CI.
   - Remove the folder from `projects` and the app from gembernodes.
   - The cases-file comparison in CI (§3.5), now that both repos sit side by side. The rest of v1's
     VOID work went in with the demo's single MR (phase 5).
7. **Signed in** (read only): my characters, plans on the server, *next events*, the envelope
   list.
8. **Go-live v1.**
   - SL and EC have done §3.7's steps 1–6: the rules are reviewed and loaded on live, and the
     text is at the 2026 book.
   - Publish the first ruleset from live VOID.
   - The `vortex-character-creator` Keycloak client, the Portainer stack, Caddy, and Thorfgin's
     redirect.
   - The rules page's "Character Creator" link moves from builder.qequoia.nl to
     builder.the-vortex.nl.
   - FAQ and release notes, including §3.6, and an announcement.
9. **v2: push new characters** (§7.3).
   - Its design pass first.
   - Then VOID's validation marker and the policy change, and VoidWalker's *To validate* list.
   - Then the Character Creator's push and sync.
10. **v3: push changes to existing characters** (§7.4). Its own design pass first.
11. **v4: SL and EC edit the content in the app** (§3.7, step 6). Its own design pass first.
    - **Where the content lives:** today the content files in the repo are the source. In v4,
      Marten documents become the source for the text, versioned, with the repo files as the
      first import.
    - **The screen:** per skill, spell, recipe, preset and FAQ entry, Dutch and English side by
      side, with a preview of how the build screen shows it.
    - **Drafts and publishing:** edits are drafts until an SL or EC publishes a ruleset, which
      takes both the text and VOID's rules. Every edit records who made it and when.

Later, not in this design:

- a *Plan in the Character Creator* link from the portal's character screen;
- an automatic VOID sync (this needs a service identity VOID accepts);
- showing a teacher their students.

---

## 11. Questions

### Open

1. **Should publishing match VOID?** Changing a rule in VOID is now Referee and up, which
   includes Infobalie. Publishing a ruleset is SL or EC by name, which excludes Infobalie. So an
   Infobalie could change a rule in VOID, but not publish it to the Character Creator.
   - Aligning them would mean `void.infobalie` joins the `Beheer` policy (§8).
   - Left as is, an SL or EC publishes what an Infobalie changed.
2. **EC only, or not:** Arie still has to discuss this with the others, for both VOID and
   publishing. He leans against it, because EC wants to delegate these changes.
3. **Dependency:** the review of the 2026 V07 draft, and loading it (and the SL file) on live
   (§3.7, steps 1–3). Going live waits for it.

### Settled

- **Draft 2 (Arie, 2026-11-06):**
  - Thorfgin is on board; the first build is a proof of concept that steps on nobody's toes.
  - The production host is `builder.the-vortex.nl`.
  - SL or EC publish rulesets (perhaps EC only later).
  - v1 never writes to VOID.
  - The build screen runs in the browser (WebAssembly).
- **Draft 3 (Arie, 2026-11-06):**
  - The name stays *Character Creator*.
  - The proof of concept lives in the `projects` repo for now, and runs on gembernodes for the
    demo.
  - The long term supports pushing both new characters (v2) and changes to existing ones (v3).
  - No field is mandatory; even the faction is often empty.
  - The *To validate* list is in VoidWalker.
  - VOID is the source of truth and settles every rule disagreement. That also settles the druid
    exception (§3.6) and keeps the properties VOID doesn't hold as display content (§3.1).
- **Draft 4 (Arie, 2026-11-06):**
  - The demo host is `charactercreator.gemberkoekje.nl`.
  - Thorfgin won't maintain it. On Vortex's private GitLab every developer can.
  - The rules and the 2026 text are the responsibility of SL and EC (§3.7).
  - VOID learns "at most 3 XP left over" as a warning, and so does the Character Creator; neither
    blocks on it (§3.3).
  - In VOID, **Referee and up** may change the rules. Infobalie above Referee is an old choice
    Arie disagrees with, but it stands for now. Whether it becomes EC only is still under
    discussion (§3.7, verdict item 2).
  - Editing the text is **not a developer's job**. SL and EC edit it in the app from v4; until
    then it goes through GitLab (§3.7, step 6).
- **Draft 5 (Arie, 2026-11-07):**
  - The VOID change goes to a pushed branch, and to `develop` in time.
  - The demo talks to `develop`'s instance, `void-test`, instead of a recorded snapshot.
- **Draft 6 (Arie, 2026-11-07):**
  - `void-test` pulls `develop`'s latest image when Arie refreshes it in Portainer.
  - Arie loads the rulebook draft on `void-test` himself, in Portainer.
  - The demo's sign-in goes on `vortex-dev`; the production client waits for the GitLab repo.
  - All VOID work for the demo goes onto one branch, pushed once, then one MR into `develop`. That
    saves GitLab CI minutes.

---

## 12. Sources

- **The current builder:**
  - builder.qequoia.nl;
  - github.com/Thorfgin/charactercreator, v0.7.4 of 2026-04-04: README, `src/json`,
    `SharedActions.js`, `Toolbar.jsx`, `ExportToPDF.js`, `public/locales`;
  - github.com/Thorfgin/charactercreator2.
- **The current rulebooks** (Arie, 2026-11-07): https://the-vortex.nl/het-spel/regels/. As listed
  there on 2026-11-07, with the upload month as the only version hint:
  - `wp-content/uploads/2026/03/Vaardigheden.pdf` (skills);
  - `2026/03/Spreuken_en_Technieken.pdf` (spells and fighter techniques);
  - `2026/03/Spelregels.pdf`;
  - `2026/03/Crafting_loresheets.pdf`;
  - `2026/03/Geschiedenis.pdf` (*Recente wijzigingen regelsysteem*, March 2026);
  - the imbue and alchemy loresheets (`2024/06/…`);
  - Priest Runes and Mage Glyphs, with their fonts (`2022/03`, `2022/04`).

  All in Dutch; there are no English books.
- **The checkout design:** `C:\git\gitlabs\CHECKOUT-DESIGN.md` §0, §6 and §9.
- **void** (all paths in the void repo):
  - `CHECKOUT-STEP4-NOTES.md` and `PLAYER-NO-ACCESS-NOTES.md`;
  - `src/Checkout/SkillRules.php`;
  - `config/Migrations/20261031120000_SkillRequirements.php`;
  - `config/SkillRequirements/vaardigheden-2026-v07.json` and `-review.md`;
  - `src/Model/Entity/{Skill,Character}.php`;
  - `src/Model/Table/{Characters,CharactersSkills,Skills}Table.php`;
  - `src/Model/Validation/CharacterValidator.php`;
  - `src/Policy/Controller/SkillsControllerPolicy.php`;
  - `src/Policy/Entity/{Character,CharactersSkill}Policy.php`;
  - `src/EruAuth/Service/KeycloakTokenValidator.php`;
  - `config/routes.php`.
- **The SL file:** `C:\git\gitlabs\skill-requirements-sl.json`. SL only; never commit it to a
  public repo, and `projects` and gembernodes are public.
- **vortex-tools-style 2.0.0:** the README (*Using Guild Hall*) and `reference/guildhall.html`.
- **The sibling apps:**
  - Kluskat's `DEPLOY.md`, `Directory.Build.props` and `kluskat-build-and-push.yml` (in
    `projects`), and gembernodes `apps/kluskat/`;
  - werkplaats and landerijen-volken `CLAUDE.md` (the template deltas);
  - landerijen `DESIGN.md` (the layout of this kind of document).
