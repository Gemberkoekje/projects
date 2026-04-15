# Slay the Spire 2 — Completed Work

## Step 1: Pattern Audit Tool (First Deliverable)

Implemented a standalone audit command before DB ingestion.

- `sts2_Annotator` converted into .NET 9 extractor CLI base.
- CLI commands implemented (18 total): `audit`, `extract-cards`, `extract-relics`, `extract-potions`, `extract-events`, `extract-powers`, `extract-characters`, `generate-ratings`, `seed-taxonomy`, `annotate`, `list-models`, `init-database`, `sync-postgres`, `build-synergy-edges`, `build-synergy-clusters`, `discover-archetypes`, `score-affinities`, `advise-pick`.
- `audit` output CSV includes:
  - `file`
  - `entity_type`
  - `fields_found`
  - `fields_missing`
  - `confidence_score`
  - `fallback_required`
- Scripts implemented:
  - `scripts/run_pattern_audit.ps1`
  - `scripts/run_taxonomy_seed.ps1`
  - `scripts/run_relic_extract.ps1`
  - `scripts/run_potion_extract.ps1`
  - `scripts/run_event_extract.ps1`
  - `scripts/run_power_extract.ps1`
  - `scripts/run_strength_ratings.ps1`
  - `scripts/run_postgres_sync.ps1`
  - `scripts/run_ironclad_e2e.ps1`
  - `scripts/run_synergy_edges.ps1`
  - `scripts/run_advise_pick.ps1`
  - `scripts/run_viewer.ps1`
  - `scripts/run_synergy_refresh.ps1`

**Audit result summary (sample run)**:
- Cards: ~85% confidence `1.0`, ~10% confidence `0.80` (mostly missing `canonical_vars`), small edge-case tail.
- Relics: 100% confidence `1.0` in sample.
- Powers: mixed; frequent `0.80`, one `0.60`, one `0.00`.

**Conclusion**: parser-first remains the correct default path. Roslyn fallback should target low-confidence powers and edge-case cards first.

---

## Step 2: Lightweight Extractor with Roslyn Fallback

Current extractor now performs:
1. Fast pattern parsing for cards.
2. Confidence scoring and low-confidence flagging.
3. Method body capture (`OnPlay`, `OnUpgrade`) for downstream annotation.
4. Roslyn-assisted fallback in parsing helpers for class/base ctor/hook and method-body extraction.

Completed in this step:
- Hardened class parsing beyond strict `public sealed class` assumptions.
- Added filename-based fallback for `card_id` when class parse fails.
- Hardened base-type matching (`CardModel`/`RelicModel`/`PowerModel`) via normalized type checks.
- Updated power confidence logic to treat `CanonicalVars` as optional when core power signals are present.
- Persisted extraction QA telemetry fields (`id_resolution_status`, `roslyn_fallback_used`) to card extraction CSV output.
- Added companion/Osty detection for `FromOsty(...)` and `OstyCmd.*`, including staging output for `card_companion_actions`.

---

## Step 2.1: Audit-Driven Improvements

1. **Card ID edge-case hardening**
   - ✅ Fix class parsing for unusual class declarations (not just simple `public sealed class`).
   - ✅ Add fallback ID derivation from filename when class parse fails.
   - ✅ Persist `id_resolution_status` during extraction for QA.

2. **Power extractor robustness**
   - ✅ Accept powers with no `CanonicalVars` as valid when other signatures are present.
   - ✅ Expand hook detection to include expression-bodied overrides and uncommon signatures.

3. **Companion/Osty mechanic support**
   - ✅ Recognize `FromOsty(...)` and `OstyCmd.*` patterns as companion-sourced actions.
   - ✅ Persist companion mechanics to extraction tags and `card_companion_actions` staging CSV.

4. **Taxonomy seeding updates before annotation**
   - ✅ Added taxonomy seed CSV command/output for Regent/Osty tag bootstrapping before annotation pass.
   - ✅ Added Regent resource tags to avoid LLM vocabulary drift:
     - `generate_stars`, `consume_stars`
     - `gain_forge`, `consume_forge`
   - ✅ Added Osty/companion tags:
     - `osty_attack`, `osty_damage_source`, `companion_synergy`

---

## Completed Steps (Chronological)

1. Implemented `run_pattern_audit.ps1` and generated `pattern_audit.csv`.

2. Implemented lightweight `CardExtractor` + confidence scoring + extraction preview CSV.

3. Added Roslyn fallback for low-confidence parsing paths (class/base ctor/hooks/method source), with power-first robustness improvements.

4. Hardened class parsing and filename-based fallback to eliminate empty `card_id` rows.

5. Persisted extraction QA telemetry (`id_resolution_status`, `roslyn_fallback_used`) to CSV staging.

6. Added `FromOsty(...)`/`OstyCmd.*` detection and persisted companion actions/tags.

7. Seeded taxonomy for Forge/Stars/Osty tags before annotation pass (`seed-taxonomy`, `effect_taxonomy_seed.csv`).

8. Added provider abstraction in `Sts2Annotator` with `annotate` command (`--provider anthropic|openai`) and task-based model routing (`--task mechanical|synergy`) using Haiku-class for mechanical annotation batches and Sonnet-class/strong models for synergy reasoning. Supports explicit model override with `--model`.

9. **Phase D — LLM Annotation Integration**: Wired mechanical annotation into the `sync-postgres` pipeline.
   - ✅ Created `MechanicalAnnotationRunner` that reads ingestion CSVs and sends batches to the configured LLM provider.
   - ✅ Each entity's source code (`on_play_source`, `hook_source`, `on_use_source`) is sent in batches to a Haiku-class model.
   - ✅ Structured JSON response populates: `effect_tags`, `effect_description`, `resources_generated`, `resources_consumed`, `scaling_type`, `anti_synergy_tags` (per entity type as appropriate).
   - ✅ Enriched ingestion CSVs are written back in-place before the DB insert step.
   - ✅ Gated by `--annotate` flag on `sync-postgres` (opt-in, no unintended LLM spend).
   - ✅ Supports `--provider`, `--model`, `--batch-size` pass-through from `sync-postgres` options.
   - ✅ Added `AnnotatedEntityCount` to `PostgresSyncResult` for reporting.
   - ✅ Updated `run_postgres_sync.ps1` with `-Annotate` switch.

10. Load base/upgraded card data first, and only emit `card_variants` for exceptions.
    - ✅ Created `CardIngestionRecord` for standard base/upgraded card schema.
    - ✅ Created `CardVariantRecord` for non-standard upgrades/exceptions.
    - ✅ Created `CardIngestionConverter` to split extraction records into ingestion + variants.
    - ✅ Modified `CardExtractionRunner` to output three CSV files:
      - `cards_ingestion.csv`: Base/upgraded cards ready for `cards` table (default path).
      - `card_variants_staging.csv`: Exceptions (enchantments, rare multi-upgrade cards).
      - `card_companion_actions_staging.csv`: Companion mechanics (Osty actions).
    - ✅ Updated CLI output reporting to show all generated files and counts.

11. Extract one character end-to-end (Ironclad), annotate, validate, then scale.
    - ✅ Added `--character <id>` CLI option to `extract-cards`, `extract-relics`, and `extract-potions` commands to scope extraction to a single character's pool.
    - ✅ Created `CharacterPoolReader` helper that parses `{CharacterId}CardPool.cs` / `RelicPool.cs` / `PotionPool.cs` files via `ModelDb.Card<ClassName>()` regex to build per-character class name allowlists.
    - ✅ Added helper script: `scripts/run_ironclad_e2e.ps1` — chains `extract-cards --character ironclad`, `extract-cards --character colorless`, `extract-relics --character ironclad`, `extract-potions --character ironclad`, `generate-ratings`, and `sync-postgres` into a single validated pipeline.
    - Annotation step: run `annotate --task mechanical --prompt-file <prompt.txt>` manually against `cards_ingestion.csv` `on_play_source` fields; spot-check `effect_tags` on Bash, Inflame, Metallicize, Whirlwind.

12. Implemented `extract-cards`, `extract-relics`, and `extract-potions` commands with per-character scope and handling for colorless cards.
    - `scripts/run_full_sync.ps1` provides full multi-character sync (Ironclad, Silent, Defect, Regent, Necrobinder, Colorless).
    - Upsert logic in `PostgresSyncRunner` handles merging runs with overlapping characters without duplicates.

13. Expanded ingestion/annotation parity for relics (including starter relics), potions, and `?` room answers/outcomes.
    - ✅ Added CLI commands: `extract-relics`, `extract-potions`, `extract-events`.
    - ✅ Added relic extraction + ingestion staging:
      - `relic_extract_preview.csv`
      - `relics_ingestion.csv`
      - `relic_hook_listeners_staging.csv`
    - ✅ Added potion extraction + ingestion staging:
      - `potion_extract_preview.csv`
      - `potions_ingestion.csv`
    - ✅ Added event/question-room extraction + staging:
      - `event_extract_preview.csv`
      - `events_ingestion.csv`
      - `event_options_staging.csv`
      - `event_outcomes_staging.csv`
    - ✅ Captured method-body source fields for downstream annotation parity:
      - relic hooks (`hook_source`)
      - potion use handler (`on_use_source`)
      - event option/outcome handlers (`handler_source`, `outcome_source`)

14. Generate and persist `entity_strength_ratings` (`synergy_rating`, `flexibility_rating`, `anti_synergy_rating`) for cards, relics, potions, and event options.
    - ✅ Added CLI command: `generate-ratings`.
    - ✅ Added runner: `EntityStrengthRatingsRunner`.
    - ✅ Added staging output: `entity_strength_ratings_staging.csv`.
    - ✅ Included ratings for entity types: `card`, `relic`, `potion`, and event-option rows keyed as `event_id::option_key`.
    - ✅ Added helper script: `scripts/run_strength_ratings.ps1`.

15. Created a PostgreSQL-backed web project to visualize entities, tags, synergies, and 3-axis ratings with filter/search views.
    - ✅ Added CLI command: `sync-postgres` to run extraction + ratings + taxonomy and sync into PostgreSQL.
    - ✅ Added helper script: `scripts/run_postgres_sync.ps1`.
    - ✅ Added web project: `sts2_Viewer` (Razor Pages, PostgreSQL-backed).
    - ✅ Added filter/search UI for entity type, free-text search, tag, and rating thresholds.
    - ✅ Added synergy cluster view sourced from `synergy_clusters`/`archetypes`.
    - ✅ Added helper script: `scripts/run_viewer.ps1`.

16. Build pairwise synergy edge table (Phase 3 — Layer 1).
    - ✅ Added `BuildSynergyEdges = 13` to `CliCommand` enum in `CliCommand.cs`.
    - ✅ Added `build-synergy-edges` branch in `CliOptions.Parse()` with default output path `synergy_edges_staging.csv`; added `--batch-size <n>` option (default 50 pairs per LLM request).
    - ✅ Added `BatchSize` property to `CliOptions`, threaded through `ForCommand`.
    - ✅ Added handler in `Program.cs` following the existing runner pattern.
    - ✅ Created `SynergyEdgeRecord` DTO (columns: `entity_a_type`, `entity_a_id`, `entity_b_type`, `entity_b_id`, `synergy_strength`, `is_anti_synergy`, `shared_tags`, `explanation`).
    - ✅ Created `BuildSynergyEdgesResult` DTO.
    - ✅ Created `BuildSynergyEdgesRunner` class:
      - Loads all entities (cards, relics, potions) from Postgres with their `effect_tags`, `resources_generated`, `resources_consumed`, `triggers_on`, `anti_synergy_tags`.
      - Tag-overlap pre-filter: only generates pairs that share ≥1 effect tag.
      - Batches filtered pairs to Sonnet-class model (`--task synergy`), requesting score (1–10), direction (A→B / B→A / both), anti-synergy flag, shared tags, one-sentence explanation.
      - Parses LLM JSON responses into `SynergyEdgeRecord` list.
      - Writes `synergy_edges_staging.csv`.
    - ✅ Added upsert of `synergy_edges_staging.csv` into `entity_synergy_edges` table inside `PostgresSyncRunner` (uses `ON CONFLICT ... DO UPDATE`; table is refreshed only when a new synergy-edges staging file is present).
    - ✅ Updated `Usage` string in `CliOptions.cs` to include `build-synergy-edges` and `--batch-size`.
    - ✅ Added helper script: `scripts/run_synergy_edges.ps1`.

17. Implement `advise-pick` CLI command (Phase 3 — Layer 2/3).
    - ✅ Added `AdvisePick = 14` to `CliCommand` enum in `CliCommand.cs`.
    - ✅ Added new fields to `CliOptions`: `ArchetypeRef` (string), `DeckIds` (string[]), `RelicIds` (string[]), `OptionIds` (string[]), `ExplainPick` (bool).
    - ✅ Added `advise-pick` branch in `CliOptions.Parse()` parsing `--archetype`, `--deck`, `--relics` (comma-separated for this command), `--options` (comma-separated), and `--explain` (flag).
    - ✅ Made `--relics` command-aware: comma-separated IDs for `advise-pick`, integer sample size for all other commands.
    - ✅ Added validation: `advise-pick` fails fast with clear message if `--options` is missing.
    - ✅ Added handler in `Program.cs` following the existing runner pattern.
    - ✅ Created `PickScoreRecord` DTO (columns: `rank`, `entity_type`, `entity_id`, `edge_overlap_score`, `archetype_fit_score`, `anti_synergy_penalty`, `flexibility_score`, `composite_score`, `synergy_drivers`, `explanation`).
    - ✅ Created `AdvisePickResult` DTO (holds `Scores` list and `LlmNarrative`).
    - ✅ Created `AdvisePickRunner` class:
      - Accepts archetype ref (numeric ID or name), deck entity IDs, relic entity IDs, offered option IDs from `CliOptions`.
      - Resolves archetype by numeric ID first, then falls back to case-insensitive name lookup.
      - Resolves entity type (card/relic/potion/unknown) for each option via Postgres lookup.
      - Queries `entity_synergy_edges` for all edges between offered options and current deck/relics (both directions).
      - Computes `edge_overlap_score` = sum of `synergy_strength` for matching non-anti edges / (held_size × 10), clamped to [0,1].
      - Queries `synergy_clusters` for `archetype_fit_score` of each option under the chosen archetype (normalises scores > 1 by dividing by 10).
      - Computes `anti_synergy_penalty` from anti-synergy edges + 0.1 flat per `anti_synergy_tags` overlap with current deck `effect_tags`, clamped to [0,1].
      - Looks up `flexibility_rating` from `entity_strength_ratings` for each option → `flexibility_score` = rating / 10 (defaults to 0.5 when absent).
      - Composite score: `0.45 * edge + 0.30 * arch - 0.20 * anti + 0.05 * flex`, clamped to non-negative.
      - Prints ranked results to stdout in the plan-defined format with synergy drivers per option.
      - Reuses `SynergyEdgeRecord` for edge rows (no duplicate type).
    - ✅ If `--explain` flag set: sends scored options to Sonnet-class model for plain-English narrative of top pick; appended to stdout.
    - ✅ Updated `Usage` string in `CliOptions.cs` to include `advise-pick` with all new flags.
    - ✅ Added helper script: `scripts/run_advise_pick.ps1`.

18. Add `/PickAdvisor` Razor Page to `sts2_Viewer` (Phase 3 — Web UI).
    - ✅ Added `sts2_Viewer/Pages/PickAdvisor.cshtml` + `PickAdvisor.cshtml.cs` with character/archetype selection, deck/relic/option inputs, scoring results table, and LLM explanation action.
    - ✅ Added viewer DTOs for advisor/read-write flows: `PickScoreRow`, `RunStateRow`, `ArchetypeRow`, `EntityOptionRow`, `EntitySynergyEdgeRow`, `CharacterRow`.
    - ✅ Extended `PostgresReadService` with pick-advisor data methods (`GetArchetypes`, `GetEntitySynergyEdges`, `ScorePickOptions`, card/relic option feeds including colorless cards).
    - ✅ Added `PostgresWriteService` for `run_states` persistence (`SaveRunState`, `GetRunStates`, `DeleteRunState`).
    - ✅ Added `PickExplanationService` + provider abstraction for optional Sonnet/`gpt-4o` top-pick narrative.
    - ✅ Registered new services in `sts2_Viewer/Program.cs` and added navigation link in `Pages/Shared/_Layout.cshtml`.

19. Fix `character_id` propagation and add colorless card extraction.
    - ✅ Added `CharacterId` property to `CardIngestionRecord` (value = `options.CharacterId`; empty string for full-scan runs).
    - ✅ Updated `CardIngestionConverter.ToIngestionRecord` to accept and forward `CharacterId`.
    - ✅ Updated `CardExtractionRunner` to pass `options.CharacterId` into the converter and emit `character_id` column in `cards_ingestion.csv`.
    - ✅ Updated `PostgresSyncRunner.InsertCards` SQL to include `character_id`; added `DbCharacterId()` helper that maps both `"colorless"` and empty string to `NULL`.
    - ✅ Added `extract-cards --character colorless` as step [2/6] in `run_ironclad_e2e.ps1` so colorless cards are extracted and synced alongside Ironclad cards.
    - Colorless cards store `character_id = NULL` in DB — universally draftable, appear in every character's pick advisor option pool.
    - Note: `--character colorless` already worked mechanically — `CharacterPoolReader.ToPascalCase("colorless")` → `ColorlessCardPool.cs` resolves correctly with no code changes.

20. Implemented dedicated power extraction with ingestion output and Roslyn fallback.
    - ✅ Added `ExtractPowers = 11` to `CliCommand` enum.
    - ✅ Created `PowerExtractor`, `PowerExtractionRecord`, `PowerIngestionRecord`, `PowerExtractionResult`, `PowerExtractionRunner`.
    - ✅ Runner outputs three files: `power_extract_preview.csv` (audit), `powers_ingestion.csv` (DB-ready), `power_hook_listeners_staging.csv` (hook listeners).
    - ✅ Confidence threshold filtering: counts low-confidence powers based on `options.ConfidenceThreshold`.
    - ✅ Hook-to-trigger mapping and ingestion record conversion.
    - ✅ Added `extract-powers` branch in `CliOptions.Parse()` with `--powers <n>` sample size option.
    - ✅ Power sync integrated into `PostgresSyncRunner`: runs `PowerExtractionRunner` inside `sync-postgres`, inserts into `powers` and `hook_listeners` tables, and clears existing power rows before re-insert.
    - ✅ Added helper script: `scripts/run_power_extract.ps1`.

21. Implemented `init-database` CLI command.
    - ✅ Added `InitDatabase = 12` to `CliCommand` enum.
    - ✅ Created `DatabaseInitializer` utility that reads `schema.sql` and executes it against the configured Postgres connection.
    - ✅ Added handler in `Program.cs`.

22. Colorless cards are first-class in the Pick Advisor and Viewer.
    - ✅ `PostgresReadService.GetCharacters()` appends a synthetic "Colorless" entry to the character list.
    - ✅ `PostgresReadService.GetCardOptions(characterId, includeColorless)` returns cards where `character_id IS NULL` alongside character-specific cards.
    - ✅ `PickAdvisorModel.Initialize()` calls `GetCardOptions` with `includeColorless: true` — colorless cards always appear in option search regardless of selected character.
    - ✅ `PostgresReadService.GetRelicOptions()` also includes relics with `character_id IS NULL`.
    - ✅ `PostgresReadService.ScorePickOptions()` resolves entity types across cards/relics/potions with no character filter — colorless entities score correctly.
    - ✅ All pick advisor DTOs (`PickScoreRow`, `RunStateRow`, `ArchetypeRow`, `EntityOptionRow`, `EntitySynergyEdgeRow`, `CharacterRow`) in `sts2_Viewer/Data/`.

23. Implemented full multi-character sync pipeline (Step 1 from `Plan_ToDo.md`) and fixed current build blocker.
    - ✅ Added CLI flag `--all-characters` in `CliOptions` and updated usage text.
    - ✅ Added `CliOptions.WithCharacter(...)` helper for internal per-character extraction orchestration.
    - ✅ Updated `PostgresSyncRunner` to run `extract-cards` per character (`ironclad`, `silent`, `defect`, `regent`, `necrobinder`, `colorless`) when `--all-characters` is set, and then sync combined outputs in one DB transaction.
    - ✅ Added upsert behavior for card tables in `PostgresSyncRunner` (`cards`, `card_variants`, `card_companion_actions`) to prevent duplicate-key failures during merged runs.
    - ✅ Added `scripts/run_full_sync.ps1` helper script.
    - ✅ Extended `scripts/run_postgres_sync.ps1` with optional `-AllCharacters` switch.
    - ✅ Fixed `sts2_Viewer` compile error by adding missing `System.Net.Http` import for `IHttpClientFactory` in `LlmProviderFactory`.

24. Updated `schema.sql` to match runtime/Viewer table dependencies (Step 1 from `Plan_ToDo.md`).
    - ✅ Added missing tables: `characters`, `archetypes`, `synergy_clusters`, `card_rankings`, `entity_synergy_edges`, `run_states`, `pick_advice`, `card_applies_power`.
    - ✅ Added missing `cards.character_id` column required by `PostgresSyncRunner.InsertCards`.
    - ✅ Kept enum-like fields as `TEXT` intentionally and documented this in `schema.sql` for compatibility with current extractor/viewer SQL and ingestion flows.
    - ✅ Added supporting indexes for new tables and existing common query paths.

25. Investigated viewer data freshness issues and fixed two root causes impacting perceived coverage/synergy availability.
    - ✅ Removed the hardcoded entity list cap of `400` in `sts2_Viewer/Pages/Index.cshtml.cs`; added configurable `EntityLimit` (default `3000`, capped at `10000`) and exposed it in `Index` filters.
    - ✅ Added an explicit empty-state message in `sts2_Viewer/Pages/Index.cshtml` when `synergy_clusters` has zero rows, clarifying that archetype cluster data is a separate pipeline from pairwise edges.
    - ✅ Added edge-data safety in `PostgresSyncRunner` during this phase to avoid accidental `entity_synergy_edges` loss on normal sync runs (later superseded by Phase E direct regeneration wiring).

26. Completed Step 2 from `Plan_ToDo.md` (synergy pipeline population + visibility).
    - ✅ Added dedicated command: `build-synergy-clusters`.
    - ✅ Added `BuildSynergyClustersRunner` + `BuildSynergyClustersResult` to generate:
      - `archetypes_staging.csv`
      - `synergy_clusters_staging.csv`
    - ✅ Integrated cluster generation into `sync-postgres`; it now builds and syncs archetypes/clusters every sync run.
    - ✅ Added `archetypes` + `synergy_clusters` reload in `PostgresSyncRunner` so stale cluster data is replaced atomically.
    - ✅ Added Viewer diagnostics panel on `Index` showing row counts for:
      - `entity_synergy_edges`
      - `synergy_clusters`
      - `entity_strength_ratings`
    - ✅ Added one-click helper script: `scripts/run_synergy_refresh.ps1` to chain:
      - `sync-postgres`
      - `build-synergy-edges`
      - `sync-postgres` (edge import pass)

27. Conducted post-full-sync database audit to identify gaps.
    - ✅ Ran `sync-postgres --all-characters` and inspected all tables.
    - ✅ Identified root causes for empty/incomplete data across all 18 tables.
    - ✅ Categorized issues into: localization (titles/descriptions), LLM annotation (effect fields), extraction gaps (character stats), dead tables (card_applies_power, card_rankings), and downstream cascades (archetypes/edges/clusters depend on effect_tags).
    - ✅ Documented full gap analysis and fix plan in `Plan_ToDo.md` with six phases (A–F) and priority ordering.
    - ✅ Confirmed `run_states` and `pick_advice` are web-only tables (not pipeline-populated, expected empty).
    - ✅ Confirmed `card_variants` is empty by design for STS2 (MaxUpgrade is 0 or 1; ExtractVariants is a correct placeholder).

28. Completed Phase A from `Plan_ToDo.md` (schema cleanup of dead tables).
    - ✅ Removed `card_applies_power` from `sts2_Annotator/schema.sql`.
    - ✅ Removed `card_rankings` from `sts2_Annotator/schema.sql`.
    - ✅ Confirmed no related index definitions existed to remove.
    - ✅ Verified `DatabaseInitializer` required no extra updates because schema reset drops all existing public tables before re-creating from `schema.sql`.

29. Completed Phase B from `Plan_ToDo.md` (localization extraction + event readability).
    - ✅ Added `Localization/LocStringReader` with configurable `eng` directory resolution via `appconfig.json` (`Localization:EnglishDirectory`) and `STS2_LOCALIZATION_ENG_DIRECTORY`.
    - ✅ Implemented typed localization lookups for cards, relics, potions, powers, events, and characters.
    - ✅ Added formatting cleanup for SmartFormat placeholders and BBCode tags before persistence.
    - ✅ Wired localization into extraction ingestion outputs:
      - `CardIngestionConverter` (`title`, `description`)
      - `RelicExtractionRunner` (`title`, `description`, `flavor_text`)
      - `PotionExtractionRunner` (`title`, `description`)
      - `PowerExtractionRunner` (`title`, `description` including `_POWER` key mapping)
      - `EventExtractionRunner` (`title`, `description` for events + localized option/outcome text)
    - ✅ Extended event staging and schema/read models for readability fields:
      - `event_options`: added `title`, `description`
      - `event_outcomes`: added `title`, `description`
    - ✅ Updated sync path safeguards for existing databases via `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` for event option/outcome readability columns.
    - ✅ Updated character insert flow to source localized `title`/`description` instead of hardcoded title-only values.

---

## Phase C — Character Stats Extraction

`InsertCharacters` previously inserted only hardcoded `(id, title)` pairs with localized text. Now replaced by a full extraction pipeline.

- **Created `CharacterExtractor`**: Parses `CharacterModel` subclasses in `MegaCrit.Sts2.Core.Models.Characters` via regex for:
  - `StartingHp`, `StartingGold` (from expression-bodied overrides)
  - `MaxEnergy` (default 3 if not overridden)
  - `BaseOrbSlotCount` / `orb_slots` (default 0 if not overridden)
  - `StartingDeck` (all `ModelDb.Card<ClassName>()` references)
  - `StartingRelics` (all `ModelDb.Relic<ClassName>()` references)
  - Filters to known playable characters (ironclad, silent, defect, regent, necrobinder); excludes Deprived/RandomCharacter.
- **Created `CharacterExtractionRecord`** and **`CharacterExtractionResult`** following existing extractor conventions.
- **Created `CharacterExtractionRunner`**: Writes extraction preview CSV and ingestion CSV (with localized title/description).
- **Added `ExtractCharacters` CLI command** (`extract-characters`), wired into `CliCommand`, `CliOptions`,
