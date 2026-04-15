# Slay the Spire 2 — Game Knowledge Database & AI Synergy Analysis

| File | Contents |
|---|---|
| `Plan_GeneralInfo.md` | Goal, architecture overview, extraction strategy (1.0–1.3), full PostgreSQL schema (1.4), provider auth/token setup, model routing, and Phase 3 pick advisor design (layers 1–3, CLI spec, web UI spec) |
| `Plan_Done.md` | All completed steps, including Phases A–H, Phase 4 affinity/archetype work, the Phase F relic/potion `character_id` correctness fix, and Phase I Pick Advisor UX polish |
| `Plan_ToDo.md` | Remaining operational reruns/verification passes only (no open feature-phase implementation items) |

## Goal

Build a machine-readable PostgreSQL database capturing all game mechanics from the decompiled STS2 codebase, then use an LLM to:

1. **Recognize synergies** between cards, relics, potions, and other game decisions.
2. **Rank cards** within the context of those synergies and archetypes.
3. **Expand coverage** to include relics (including starter relics), potions, and question-mark (`?`) room answer/outcome data.
4. **Assign standardized strength ratings** for analyzed entities:
   - `synergy_rating` (1-10)
   - `flexibility_rating` (1-10)
   - `anti_synergy_rating` (1-10)
5. **Power a real-time pick advisor**: given a chosen archetype goal and the cards/relics already held in a run, score each option in a card/relic choice and explain *why* it fits or conflicts with the current deck state.

---

## Architecture Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│  Phase 1 — Data Extraction                                           │
│                                                                      │
│  Decompiled C# ──► Lightweight Parser ──► Postgres                   │
│       (.cs files)      (fast prototype: constructor vars, keywords)  │
│                                                                      │
│  Decompiled C# ──► Roslyn Fallback ──► Postgres                      │
│                   (only for edge cases / low-confidence parses)      │
│                                                                      │
│  Decompiled C# ──► LLM Annotation ──────────► Postgres               │
│   (OnPlay / hooks)   (behavioral tags, natural-language effects)     │
└──────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│  Phase 2 — Synergy Recognition & Ranking                            │
│                                                                     │
│  Postgres ──► LLM (Copilot / GPT-4o / Claude) ──►                  │
│   (structured    prompt with card/relic data     JSON               │
│    game data)    + effect tags + hook info       output             │
│                                                                     │
│  Output:  synergy_clusters, card_rankings,                          │
│           archetypes, entity strength ratings,                      │
│           entity_synergy_edges (pairwise)                           │
└─────────────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────────────┐
│  Phase 3 — Pick Advisor (run-aware, deck-state-driven)               │
│                                                                      │
│  User input:                                                         │
│    chosen archetype + current deck/relics + N offered options        │
│                                                                      │
│  Scoring pipeline:                                                   │
│    entity_synergy_edges ──► edge overlap score (rule-based, fast)    │
│    synergy_clusters     ──► archetype fit score                      │
│    anti_synergy_tags    ──► conflict penalty                         │
│    (optional) LLM       ──► narrative explanation per option         │
│                                                                      │
│  Output:  ranked options with per-option score breakdown             │
│           + natural-language reasoning                               │
│                                                                      │
│  Surfaces via:  CLI `advise-pick` command                            │
│                 sts2_Viewer /PickAdvisor page                        │
└──────────────────────────────────────────────────────────────────────┘
```

---

## Phase 1 — Data Extraction to PostgreSQL

### 1.0 Extraction Strategy (Prototype First)

Use a staged approach:

1. **Prototype parser first** (fast):
   - Parse class name
   - Parse `base(...)` constructor args
   - Parse `CanonicalVars`, `GainsBlock`, `OnUpgrade`
   - Extract `OnPlay`/hook method source as raw text
2. **Add confidence checks**:
   - % of classes successfully parsed
   - # fields extracted per class vs expected
3. **Roslyn fallback only where needed**:
   - Use Roslyn for files/classes that fail confidence checks
   - Keep the fast parser for the common path

This gets fast progress without locking into heavy AST work upfront.

**Observed in current codebase**: `CardModel.MaxUpgradeLevel` defaults to `1`, and many cards override to `0` (unupgradable). Multi-upgrade cards appear rare. So the schema uses base/upgraded columns as the default path, with a separate `card_variants` table for exceptions and non-standard states.

### 1.1 What Can Be Extracted Programmatically

A lightweight parser + Roslyn fallback can extract the current metadata set used by the ingestion pipeline, including:

| Data Point | Source Pattern | Example |
|---|---|---|
| Card ID | Class name | `Bash`, `Anger`, `FlameBarrier` |
| Energy cost | Constructor arg 1 | `base(2, ...)` → cost 2 |
| Card type | Constructor arg 2 / `CardType` enum | `CardType.Attack` |
| Card rarity | Constructor arg 3 / `CardRarity` enum | `CardRarity.Basic` |
| Target type | Constructor arg 4 | `TargetType.AnyEnemy` |
| Keywords | `Keywords` property / `CardKeyword` flags | `Exhaust`, `Ethereal`, `Innate`, `Retain`, `Sly`, `Eternal` |
| Numeric values | `DynamicVar` fields (`DamageVar`, `BlockVar`, `PowerVar<T>`, etc.) | `DamageVar { BaseValue = 8, UpgradedValue = 10 }` |
| Gains block? | `GainsBlock` property | `true` / `false` |
| Relic rarity | Constructor / `RelicRarity` enum | `RelicRarity.Rare` |
| Power type | `PowerType` enum | `Buff` / `Debuff` |
| Power stack type | `PowerStackType` | `Counter`, `Duration`, etc. |
| Character stats | `CharacterModel` properties | `StartingHp = 80, StartingGold = 99, MaxEnergy = 3` |
| Starting deck/relics | `StartingDeck`, `StartingRelics` lists | `[Strike, Strike, Strike, Strike, Defend, Defend, Defend, Defend, Bash]` |
| Card/relic/potion pools | pool class lists | Which character owns which entities |
| Question-room option wiring | Event option/choice classes and handlers | Option text + resulting effect path |
| Question-room outcomes | Event resolution methods | Reward/penalty/output per answer |

### 1.2 What Requires LLM Annotation

Behavioral semantics live in method bodies (`OnPlay`, hook overrides like `BeforeDamageReceived`, `AfterSideTurnStart`, etc.). These cannot be trivially parsed into structured data.

**Strategy**: Extract the raw C# source of each `OnPlay` / hook method body and feed it to an LLM with a structured prompt to produce the annotations below. The prompt must also include:

1. **Card keyword glossary** (from `sts2/eng/card_keywords.json`) so keyword-driven costs, tempo, and constraints are interpreted correctly.
2. **Card metadata** — `keywords`, `energy_cost`, `card_type`, and localized description alongside the source.

| Annotation | Description | Example |
|---|---|---|
| `effect_tags` | Normalized tags for what the card/relic/power does | `["deal_damage", "apply_vulnerable"]` |
| `effect_description` | Natural-language summary of behavior | "Deals 8 damage to an enemy and applies 2 Vulnerable" |
| `triggers_on` | What game event activates this (for relics/powers) | `["on_attack_played"]` |
| `trigger_condition` | Condition for activation | `"every 3 attacks"` |
| `scaling_type` | How the effect scales | `"per_stack"`, `"flat"`, `"multiplicative"` |
| `resources_generated` | Resources this creates | `["card_draw", "energy", "block", "strength"]` |
| `resources_consumed` | Resources this consumes | `["exhaust_card", "hp_loss", "energy"]` |
| `anti_synergies` | Things that conflict | `["low_card_count"]` |
| `synergy_rating` | How strong in intended synergy shell | `1..10` |
| `flexibility_rating` | How often broadly pickable/useful | `1..10` |
| `anti_synergy_rating` | How often it conflicts with common plans | `1..10` |

### 1.3 Localization Data

Titles and descriptions are sourced from external localization files and are now ingested directly through the localization reader used by cards, relics, potions, powers, events, and characters.

---

### 1.4 PostgreSQL Schema

> **Schema status update:**
> `sts2_Annotator/schema.sql` includes the runtime/Viewer-dependent tables and columns used by `init-database`, `PostgresSyncRunner`, and `sts2_Viewer`.
> Ownership-sensitive entity sync now preserves character-specific relic/potion ownership instead of collapsing mismatches to shared `NULL` rows.
> 
> - Added tables: `characters`, `archetypes`, `synergy_clusters`, `card_rankings`, `entity_synergy_edges`, `run_states`, `pick_advice`, `card_applies_power`.
> - Added column: `cards.character_id`.
> - Enum-like fields remain `TEXT` by design for current extractor/viewer compatibility.

```sql
-- ============================================================
-- ENUMS
-- ============================================================

CREATE TYPE card_type AS ENUM (
    'Attack', 'Skill', 'Power', 'Status', 'Curse', 'Quest'
);

CREATE TYPE card_rarity AS ENUM (
    'Basic', 'Common', 'Uncommon', 'Rare', 'Ancient',
    'Event', 'Token', 'Status', 'Curse', 'Quest'
);

CREATE TYPE target_type AS ENUM (
    'None', 'Self', 'AnyEnemy', 'AllEnemies', 'Any', 'All'
);

CREATE TYPE relic_rarity AS ENUM (
    'Starter', 'Common', 'Uncommon', 'Rare', 'Shop', 'Event', 'Ancient'
);

CREATE TYPE power_type AS ENUM ('Buff', 'Debuff');

CREATE TYPE potion_rarity AS ENUM (
    'Common', 'Uncommon', 'Rare', 'Event', 'Token'
);

CREATE TYPE entity_type AS ENUM (
    'card', 'relic', 'power', 'potion', 'enchantment',
    'affliction', 'monster', 'encounter', 'event', 'character'
);

-- ============================================================
-- CORE TABLES
-- ============================================================

CREATE TABLE characters (
    id              TEXT PRIMARY KEY,
    title           TEXT,
    starting_hp     INT,
    starting_gold   INT,
    max_energy      INT DEFAULT 3,
    orb_slots       INT DEFAULT 0,
    starting_deck   TEXT[],
    starting_relics TEXT[]
);

CREATE TABLE cards (
    id              TEXT PRIMARY KEY,
    character_id    TEXT REFERENCES characters(id),
    title           TEXT,
    description     TEXT,
    type            card_type NOT NULL,
    rarity          card_rarity NOT NULL,
    energy_cost     INT,
    target          target_type,
    gains_block     BOOLEAN DEFAULT FALSE,
    keywords        TEXT[] DEFAULT '{}',
    -- Default path for STS cards (base + upgraded)
    damage_base     INT,
    damage_upgraded INT,
    block_base      INT,
    block_upgraded  INT,
    magic_base      INT,
    magic_upgraded  INT,
    max_upgrade     INT DEFAULT 1,
    on_play_source  TEXT,
    on_upgrade_source TEXT,
    -- character_id = NULL for colorless cards (available to all characters)
    effect_tags         TEXT[] DEFAULT '{}',
    effect_description  TEXT,
    resources_generated TEXT[] DEFAULT '{}',
    resources_consumed  TEXT[] DEFAULT '{}',
    scaling_type        TEXT,
    anti_synergy_tags   TEXT[] DEFAULT '{}'
);

-- Optional extension table for non-standard upgrades and alt states
CREATE TABLE card_variants (
    id               SERIAL PRIMARY KEY,
    card_id          TEXT REFERENCES cards(id),
    variant_kind     TEXT NOT NULL, -- 'extra_upgrade' | 'enchantment' | 'temporary_state'
    upgrade_level    INT,
    source_id        TEXT,          -- enchantment/affliction id or other source key
    energy_cost      INT,
    damage_value     INT,
    block_value      INT,
    magic_value      INT,
    effect_tags         TEXT[] DEFAULT '{}',
    effect_description  TEXT,
    resources_generated TEXT[] DEFAULT '{}',
    resources_consumed  TEXT[] DEFAULT '{}',
    scaling_type        TEXT,
    anti_synergy_tags   TEXT[] DEFAULT '{}',
    UNIQUE (card_id, variant_kind, upgrade_level, source_id)
);

CREATE TABLE relics (
    id              TEXT PRIMARY KEY,
    character_id    TEXT REFERENCES characters(id),
    title           TEXT,
    description     TEXT,
    flavor_text     TEXT,
    rarity          relic_rarity NOT NULL,
    counter_base    INT,
    amount_base     INT,
    hook_source     TEXT,
    effect_tags         TEXT[] DEFAULT '{}',
    effect_description  TEXT,
    triggers_on         TEXT[] DEFAULT '{}',
    trigger_condition   TEXT,
    resources_generated TEXT[] DEFAULT '{}',
    resources_consumed  TEXT[] DEFAULT '{}'
);

CREATE TABLE powers (
    id              TEXT PRIMARY KEY,
    title           TEXT,
    description     TEXT,
    type            power_type,
    stack_type      TEXT,
    is_player_power BOOLEAN DEFAULT TRUE,
    hook_source     TEXT,
    effect_tags         TEXT[] DEFAULT '{}',
    effect_description  TEXT,
    triggers_on         TEXT[] DEFAULT '{}',
    scaling_type        TEXT
);

CREATE TABLE potions (
    id              TEXT PRIMARY KEY,
    character_id    TEXT REFERENCES characters(id),

    title           TEXT,
    description     TEXT,
    rarity          potion_rarity,
    amount_base     INT,
    on_use_source   TEXT,
    effect_tags         TEXT[] DEFAULT '{}',
    effect_description  TEXT
);

CREATE TABLE enchantments (
    id              TEXT PRIMARY KEY,
    title           TEXT,
    description     TEXT,
    extra_card_text TEXT,
    amount          INT,
    effect_tags     TEXT[] DEFAULT '{}',
    effect_description TEXT
);

CREATE TABLE afflictions (
    id              TEXT PRIMARY KEY,
    title           TEXT,
    description     TEXT,
    extra_card_text TEXT,
    amount          INT,
    effect_tags     TEXT[] DEFAULT '{}',
    effect_description TEXT
);

CREATE TABLE monsters (
    id              TEXT PRIMARY KEY,
    title           TEXT,
    hp_min          INT,
    hp_max          INT,
    move_pattern    TEXT,
    notable_powers  TEXT[]
);

CREATE TABLE encounters (
    id              TEXT PRIMARY KEY,
    room_type       TEXT,
    monster_ids     TEXT[],
    act             INT
);

CREATE TABLE events (
    id              TEXT PRIMARY KEY,
    title           TEXT,
    description     TEXT,
    is_deterministic BOOLEAN DEFAULT FALSE,
    options_summary TEXT
);

-- ============================================================
-- RELATIONSHIP & ANALYSIS TABLES
-- ============================================================

-- Keep both a resolvable FK and a raw class name to avoid ingestion breaks
CREATE TABLE card_applies_power (
    card_id          TEXT REFERENCES cards(id),
    power_id         TEXT NULL REFERENCES powers(id),
    power_class_name TEXT NOT NULL, -- extracted from generic arg or constructor type
    amount_base      INT,
    amount_upgraded  INT,
    target           TEXT,
    resolution_status TEXT DEFAULT 'unresolved',
    PRIMARY KEY (card_id, power_class_name, target)
);

CREATE TABLE hook_listeners (
    entity_type entity_type,
    entity_id   TEXT,
    hook_name   TEXT,
    PRIMARY KEY (entity_type, entity_id, hook_name)
);

CREATE TABLE effect_taxonomy (
    tag             TEXT PRIMARY KEY,
    category        TEXT,
    description     TEXT
);

-- Optional: explicit support for companion-driven actions (e.g., Osty)
CREATE TABLE card_companion_actions (
    card_id          TEXT REFERENCES cards(id),
    companion_id     TEXT NOT NULL, -- e.g., 'Osty'
    action_tag       TEXT NOT NULL, -- e.g., 'osty_attack'
    source_method    TEXT,
    PRIMARY KEY (card_id, companion_id, action_tag)
);

-- ============================================================
-- PHASE 2 OUTPUT TABLES (populated by LLM)
-- ============================================================

CREATE TABLE archetypes (
    id              SERIAL PRIMARY KEY,
    character_id    TEXT REFERENCES characters(id),
    name            TEXT NOT NULL,
    description     TEXT,
    key_effect_tags TEXT[]
);

CREATE TABLE synergy_clusters (
    id               SERIAL PRIMARY KEY,
    archetype_id     INT REFERENCES archetypes(id),
    entity_type      entity_type,
    entity_id        TEXT,
    card_variant_id  INT REFERENCES card_variants(id),
    synergy_role     TEXT,
    synergy_score    FLOAT,
    explanation      TEXT
);

CREATE TABLE card_rankings (
    id                 SERIAL PRIMARY KEY,
    card_id            TEXT REFERENCES cards(id),
    card_variant_id    INT REFERENCES card_variants(id),
    archetype_id       INT REFERENCES archetypes(id),
    rank_in_archetype  INT,
    overall_rank       INT,
    score              FLOAT,
    flexibility_score  FLOAT,
    reasoning          TEXT
);

CREATE TABLE entity_strength_ratings (
    id                   SERIAL PRIMARY KEY,
    entity_type          entity_type NOT NULL,
    entity_id            TEXT NOT NULL,
    card_variant_id      INT REFERENCES card_variants(id),
    synergy_rating       INT NOT NULL CHECK (synergy_rating BETWEEN 1 AND 10),
    flexibility_rating   INT NOT NULL CHECK (flexibility_rating BETWEEN 1 AND 10),
    anti_synergy_rating  INT NOT NULL CHECK (anti_synergy_rating BETWEEN 1 AND 10),
    rationale            TEXT,
    UNIQUE (entity_type, entity_id, card_variant_id)
);

-- ============================================================
-- PHASE 3 SCHEMA — Pick Advisor
-- ============================================================

-- Explicit pairwise synergy (and anti-synergy) edges between any two entities.
-- Populated by `build-synergy-edges` CLI command (LLM batch, Sonnet-class).
-- Stored directionally: A → B means "having A makes B better", not necessarily
-- the reverse. The LLM decides direction; bidirectional pairs get two rows.
CREATE TABLE entity_synergy_edges (
    id                SERIAL PRIMARY KEY,
    entity_a_type     entity_type NOT NULL,
    entity_a_id       TEXT NOT NULL,
    entity_b_type     entity_type NOT NULL,
    entity_b_id       TEXT NOT NULL,
    synergy_strength  INT NOT NULL CHECK (synergy_strength BETWEEN 1 AND 10),
    is_anti_synergy   BOOLEAN DEFAULT FALSE,
    shared_tags       TEXT[] DEFAULT '{}', -- effect tags driving the relationship
    explanation       TEXT,               -- one-sentence reason
    UNIQUE (entity_a_type, entity_a_id, entity_b_type, entity_b_id, is_anti_synergy)
);

-- A named snapshot of a run's current state.
-- Stored as named presets so the user can save/reload across browser sessions.
CREATE TABLE run_states (
    id            SERIAL PRIMARY KEY,
    name          TEXT NOT NULL,
    character_id  TEXT REFERENCES characters(id),
    archetype_id  INT REFERENCES archetypes(id),
    card_ids      TEXT[] DEFAULT '{}',   -- cards currently in deck
    relic_ids     TEXT[] DEFAULT '{}',   -- relics currently held
    created_at    TIMESTAMPTZ DEFAULT now()
);

-- Cached pick advice results for a given run state + set of offered options.
-- Stored for replay and comparison across runs.
CREATE TABLE pick_advice (
    id                   SERIAL PRIMARY KEY,
    run_state_id         INT REFERENCES run_states(id),
    option_entity_type   entity_type NOT NULL,
    option_entity_id     TEXT NOT NULL,
    rank                 INT NOT NULL,   -- 1 = best pick
    edge_overlap_score   FLOAT,          -- # and strength of synergy edges with current deck
    archetype_fit_score  FLOAT,          -- synergy_clusters score for chosen archetype
    anti_synergy_penalty FLOAT,          -- from anti-synergy edges with current deck
    composite_score      FLOAT,          -- weighted sum of the above
    explanation          TEXT,           -- LLM-generated narrative (optional, Sonnet-class)
    created_at           TIMESTAMPTZ DEFAULT now()
);
```

---

## Provider Auth / Token Setup

- API keys can now be resolved from `appconfig.json`, user secrets, or environment variables.
- `sts2_Annotator/appconfig.json` keys:
  - `Llm:AnthropicApiKey`
  - `Llm:OpenAiApiKey`
- User secrets use the same keys (`Llm:AnthropicApiKey`, `Llm:OpenAiApiKey`).
- Environment variable fallback remains supported:
  - `ANTHROPIC_API_KEY`
  - `OPENAI_API_KEY`
- `annotate` fails fast with a clear message if the required key is missing.

## Model Routing (Current)

Anthropic:
- Mechanical annotation (low-cost batch): `claude-haiku-4-5-20251001`
- Synergy reasoning (strong analysis): `claude-sonnet-4-6`

OpenAI:
- Mechanical annotation: `gpt-4o-mini`
- Synergy reasoning: `gpt-4o`

Override with `--model <model-id>` if needed. List available models: `dotnet run --project .\sts2_Annotator\sts2_Annotator.csproj -- list-models`

---

## Phase 3 — Pick Advisor Design

The pick advisor answers: **"Given my archetype goal and what I currently hold, which of these N choices should I take?"**

It is deliberately split into two layers so it works cheaply at query time:

### Layer 1 — Pre-computed synergy graph (offline, LLM batch)

Build a complete pairwise synergy edge table (`entity_synergy_edges`) before a run begins. This is the expensive step and only needs to run once per game patch.

- **CLI command**: `build-synergy-edges`
- **Input**: all entities in Postgres with their `effect_tags`, `effect_description`, `resources_generated`, `resources_consumed`, `triggers_on`, `anti_synergy_tags`
- **Approach**: send batches of entity pairs to a Sonnet-class model and ask it to score the synergy (1–10), identify direction (A→B, B→A, or both), flag anti-synergies, list the shared tags driving the relationship, and write a one-sentence explanation
- **Model routing**: `synergy` task → Sonnet-class (this is reasoning work, not mechanical annotation)
- **Output**: `synergy_edges_staging.csv` →  upserted into `entity_synergy_edges`
- **Scale note**: full cross-product is expensive; seed with tag-overlap pre-filter (only send pairs that share ≥1 effect tag) to reduce batch size

### Layer 2 — Rule-based pick scoring (online, fast, no LLM)

At pick time, the advisor computes a composite score per offered option using only Postgres data:

```
composite_score =
    w_edge   * edge_overlap_score        // synergy edges from option to cards/relics held
  + w_arch   * archetype_fit_score       // synergy_clusters score for chosen archetype
  - w_anti   * anti_synergy_penalty      // anti-synergy edges from option to cards/relics held
  + w_flex   * flexibility_score         // entity_strength_ratings.flexibility_rating
```

Default weights (tunable): `w_edge=0.45`, `w_arch=0.30`, `w_anti=0.20`, `w_flex=0.05`

**`edge_overlap_score`** — for each entity in the current deck/relics, look up whether an edge exists to the offered option; sum `synergy_strength` values, normalized to [0,1] by dividing by `(deck_size * 10)`. Colorless cards (`character_id = NULL`) are always eligible as offered options regardless of the selected character/archetype.

**`archetype_fit_score`** — look up the offered option in `synergy_clusters` for the chosen archetype; use `synergy_score` directly (already 0–1 float).

**`anti_synergy_penalty`** — same as edge overlap but for `is_anti_synergy = true` edges; also adds a flat penalty if the offered option's `anti_synergy_tags` overlap with any `effect_tags` of the current deck.

**`flexibility_score`** — normalised `flexibility_rating / 10` from `entity_strength_ratings`. Acts as a tiebreaker when options score similarly.

### Layer 3 — Optional LLM narrative (on demand, Sonnet-class)

After scoring, the user can request a narrative explanation. The advisor sends the scored options with their breakdown to a Sonnet-class model and asks for a plain-English rationale for the top pick. This is opt-in to control cost.

### CLI command: `advise-pick`

```
dotnet run -- advise-pick \
  --archetype <archetype_id_or_name> \
  --deck <card_id,...>              \
  --relics <relic_id,...>           \
  --options <id1,id2[,id3]>         \
  [--explain]                       # triggers LLM narrative for top pick
  [--provider anthropic|openai]
```

Output (stdout):

```
Rank 1: Inflame          score=7.84  (edge=8.1, arch=9, anti=0, flex=7)
  → Synergizes with: Bash (Strength+Vulnerable), Metallicize (Strength→more block value)
Rank 2: Whirlwind        score=5.20  (edge=5.0, arch=6, anti=2, flex=8)
  → Anti-synergy: no energy-gain relic in deck; costs all energy
Rank 3: Havoc            score=3.10  (edge=2.0, arch=3, anti=0, flex=6)
  → Low archetype fit; no existing Exhaust synergy to leverage
```

### Web UI: `/PickAdvisor` page

The viewer exposes a full interactive pick advisor page:

1. **Character selector** — filters archetype list and available card/relic pools; always includes colorless cards in the deck/option search regardless of character chosen
2. **Archetype selector** — dropdown of archetypes for the chosen character (from `archetypes` table); shows `key_effect_tags` as hint chips
3. **Current deck panel** — searchable multi-select of cards; shows current selection with `effect_tags` badges
4. **Current relics panel** — same as deck panel but for relics
5. **Offered options panel** — 2–3 slots; each is a searchable single-select (card or relic)
6. **"Advise" button** — runs the rule-based scoring (Layer 2) inline, no server round-trip needed beyond a single SQL query
7. **Results panel** — ranked options with a score bar broken down into edge/arch/anti components, plus the list of specific deck cards that drive each synergy edge
8. **"Explain (LLM)" button** — opt-in POST to the server, triggers Layer 3 narrative for the top pick; displayed as a callout below the score bars

**Named run state**: users can save and reload deck/relic snapshots by name, persisted to the `run_states` table.

---

## Phase 4 — Archetype Redesign & Card–Archetype Affinity Scoring

> **Status update:** Implemented in codebase.
> - New commands: `discover-archetypes`, `score-affinities`
> - New table: `entity_archetype_affinity`
> - Pick advisor scoring now uses affinity-first weighting in both CLI and Viewer.

### Problem Statement

The Phase 2 archetype system (`BuildSynergyClustersRunner`) is purely mechanical: it groups entities by shared `effect_tags`. This produces archetypes named after individual tags (e.g., "Deal Damage Synergy", "Apply Vulnerable Synergy") rather than meaningful gameplay concepts like "Poison", "Strength", "Shiv", or "Perfected Strike". Additionally, the Phase 3 pairwise entity synergy edge approach (`BuildSynergyEdgesRunner`) compares every pair of entities sharing a tag via an LLM, which is expensive in tokens and rarely discovers specific synergies that aren't already implied by tag overlap.

### Goals

1. **Concept-driven archetypes** — Each archetype should revolve around a recognizable gameplay concept (e.g., Poison, Shiv, Strength, Exhaust) or a specific build-around card (e.g., Perfected Strike). Archetypes should resemble the official synergies:
   - Ironclad: Block, Bloodletting, Exhaust, Strength, Strike, Vulnerable
   - Silent: Draw, Poison, Shiv, Sly
   - Regent: Colorless, Sovereign Blade, Star
   - Necrobinder: Doom, Ethereal, Osty, Souls
   - Defect: Claw, Lightning, Orbs, Status
2. **Card–archetype affinity scores** — Every card gets a relevance score (0–10) against every archetype of its character. Specialized cards score high for their archetype; flexible/generic cards score moderately across multiple archetypes.
3. **Minimize LLM token usage** — No pairwise card-vs-card comparisons. Use cheap Haiku-class models for the bulk annotation. Reserve Sonnet-class only for the one-time archetype discovery step.
4. **Preserve the existing pick advisor flow** — The `AdvisePickRunner` and Viewer `PickAdvisor` page should continue to work, but use the new card–archetype affinity scores instead of (or in addition to) the old synergy cluster scores.

### Phase 4.1: Archetype Discovery (one-time, Sonnet-class LLM)

**Goal**: Produce a curated list of ~4–8 archetypes per character.

**Approach**: Send one LLM prompt per character containing:
- The character's full card list (id, title, type, rarity, energy cost, keywords, effect_tags, effect_description — all from Postgres)
- The character's relics (id, title, effect_tags, effect_description)
- A system prompt asking the LLM to identify the distinct strategic archetypes for this character

**Prompt design**:
```
You are an expert Slay the Spire 2 deckbuilding analyst.

Given the following cards and relics for the {character} character, identify 4-8 distinct 
archetypes (strategic themes) that a player can build around.

Each archetype should represent a recognizable gameplay concept (e.g., "Poison", "Strength", 
"Shiv", "Exhaust") or a specific build-around card (e.g., "Perfected Strike"). 

For each archetype, provide:
- name: short label (1-3 words)
- description: one sentence explaining the win condition or strategy
- key_effect_tags: 2-5 effect tags from the data that are central to this archetype
- core_card_ids: 3-6 card IDs that are the strongest examples of this archetype

Return a JSON array only, no other text.

Cards:
{card_data_table}

Relics:
{relic_data_table}
```

**Token budget**: ~5 characters × 1 prompt each × ~2000 input tokens + ~500 output tokens ≈ ~12,500 tokens total. Very cheap even with Sonnet-class.

**Output**: Upsert into `archetypes` table. Clear old archetypes for the character first.

**CLI command**: `discover-archetypes`

### Phase 4.2: Card–Archetype Affinity Scoring (bulk, Haiku-class LLM)

**Goal**: For every card, produce a 0–10 affinity score against each archetype of its character.

**Approach**: Send batched prompts to a Haiku-class model. Each batch contains:
- The archetype definitions (from Phase 4.1) for the character
- A batch of ~20–30 cards with their metadata

**Prompt design**:
```
You are an expert Slay the Spire 2 card evaluator.

Below are the archetypes for {character}, followed by a batch of cards.
For each card, rate its affinity (0-10) for EACH archetype.
- 0 = completely irrelevant or anti-synergistic
- 5 = generically useful but not specialized  
- 10 = core build-around card for this archetype

A flexible card (e.g., basic Strike/Defend) should score 3-5 across most archetypes.
A specialized card should score 7-10 for its archetype and 0-3 for others.

Archetypes:
{archetype_definitions}

Cards:
{card_batch}

Return a JSON array where each element has:
  card_id, affinities: { "archetype_name": score, ... }
No other text.
```

**Token budget per character**: ~(total_cards / batch_size) batches × ~1500 tokens per batch. For ~50 cards per character at batch_size=25, that's 2 batches × 1500 ≈ 3,000 tokens per character. ~15,000 tokens total across 5 characters. Very cheap with Haiku-class.

**Output**: New `entity_archetype_affinity` table.

### Phase 4.3: Relic/Potion Affinity Scoring (optional, Haiku-class)

Same approach as Phase 4.2 but for relics and potions. Since there are fewer of these per character, they can be done in a single batch per character per entity type.

**Output**: Same `entity_archetype_affinity` table.

### Schema: `entity_archetype_affinity`

```sql
CREATE TABLE IF NOT EXISTS entity_archetype_affinity (
    archetype_id INTEGER NOT NULL REFERENCES archetypes(id),
    entity_type TEXT NOT NULL,
    entity_id TEXT NOT NULL,
    affinity_score INTEGER NOT NULL CHECK (affinity_score BETWEEN 0 AND 10),
    PRIMARY KEY (archetype_id, entity_type, entity_id)
);

CREATE INDEX IF NOT EXISTS idx_entity_archetype_affinity_entity
    ON entity_archetype_affinity(entity_type, entity_id);
CREATE INDEX IF NOT EXISTS idx_entity_archetype_affinity_archetype
    ON entity_archetype_affinity(archetype_id);
```

### Table Status After Phase 4

| Table | Status |
|---|---|
| `synergy_clusters` | **Replaced** by `entity_archetype_affinity`. Can be dropped or kept for backward compatibility during migration. |
| `entity_synergy_edges` | **Keep but deprioritize**. Pairwise edges are expensive and low-value. The pick advisor should primarily use archetype affinity scores instead. Edges remain useful for niche anti-synergy detection but should not be the primary scoring signal. |
| `entity_strength_ratings` | **Keep as-is**. `flexibility_rating` remains useful as a tiebreaker in pick scoring. |

### Updated Pick Advisor Scoring Formula

#### Current formula
```
composite = w_edge * edge_overlap + w_arch * archetype_fit - w_anti * anti_penalty + w_flex * flexibility
```
With weights: edge=0.45, arch=0.30, anti=0.20, flex=0.05

#### New formula
```
composite = w_aff * archetype_affinity + w_deck * deck_affinity_fit - w_anti * anti_penalty + w_flex * flexibility
```

Where:
- **`archetype_affinity`** (weight 0.50): The option's affinity score for the chosen archetype, normalized to 0–1 (score / 10).
- **`deck_affinity_fit`** (weight 0.25): Average archetype affinity of the option across archetypes that the current deck is already invested in. This captures "does this card fit what I'm already building?" without pairwise edge lookups. Computed as: for each archetype where the current deck has ≥2 cards with affinity ≥5, take the option's affinity for that archetype; average them and normalize to 0–1.
- **`anti_penalty`** (weight 0.15): Retained from current system. Use `anti_synergy_tags` overlap with held deck's `effect_tags`, plus any remaining anti-synergy edges.
- **`flexibility`** (weight 0.10): From `entity_strength_ratings.flexibility_rating`, normalized to 0–1.

### Token Budget Summary

| Step | Model tier | Tokens (approx.) | Cost (Anthropic) |
|---|---|---|---|
| Archetype discovery | Sonnet-class | ~12,500 | ~$0.05 |
| Card affinity scoring | Haiku-class | ~15,000 | ~$0.01 |
| Relic/potion affinity scoring | Haiku-class | ~5,000 | <$0.01 |
| **Total** | | **~32,500** | **~$0.06** |

Compare to the old pairwise edge approach which used Sonnet-class for potentially thousands of entity pairs — this is orders of magnitude cheaper.

### Expected Archetype Output (Reference)

These are the archetypes we expect the LLM to discover (similar names, not necessarily identical):

| Character | Expected Archetypes |
|---|---|
| Ironclad | Block, Bloodletting/Self-Damage, Exhaust, Strength, Strike, Vulnerable |
| Silent | Draw, Poison, Shiv, Sly |
| Regent | Colorless, Sovereign Blade, Star/Forge |
| Necrobinder | Doom, Ethereal, Osty/Companion, Souls |
| Defect | Claw, Lightning, Orbs, Status |

The LLM prompt includes the character's actual card/relic data, so it should naturally discover these themes without hardcoding.
