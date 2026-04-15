# Step 9 Implementation: Base/Upgraded Card Data with Variant Exceptions

## Overview
Implemented the card ingestion strategy to load base/upgraded card data as the default path, with card variants only for exceptions. This aligns with the PostgreSQL schema where most STS2 cards follow the `base` + `upgraded` column structure.

## Implementation Details

### New Files Created

1. **`CardIngestionRecord.cs`** — Standard card format ready for DB ingestion
   - Represents cards using base/upgraded columns only
   - Includes all annotation fields (effect_tags, resources, scaling_type, anti_synergy_tags)
   - Ready for direct insertion into `cards` table

2. **`CardVariantRecord.cs`** — Exception card format
   - Represents non-standard upgrades (enchantments, temporary states, rare multi-upgrade cards)
   - Includes variant_kind, upgrade_level, source_id for flexible tracking
   - Ready for insertion into `card_variants` table when exceptions are found

3. **`CardIngestionConverter.cs`** — Transformation logic
   - Converts `CardExtractionRecord` (raw parsing output) → `CardIngestionRecord` (ingestion-ready)
   - Implements `ExtractVariants()` for future exception handling (currently returns empty for STS2's standard structure)

### Modified Files

1. **`CardExtractionRunner.cs`** — Enhanced output pipeline
   - Now generates **four** CSV outputs instead of two:
     1. `extract-cards_audit.csv` — Raw extraction audit (confidence scores, QA telemetry)
     2. `cards_ingestion.csv` — Base/upgraded cards (default path for DB load)
     3. `card_variants_staging.csv` — Exceptions (currently sparse in STS2)
     4. `card_companion_actions_staging.csv` — Companion mechanics (Osty, etc.)

2. **`CardExtractionResult.cs`** — Extended result tracking
   - Added `IngestionOutputPath` and `VariantOutputPath`
   - Added `VariantCount` for variant record telemetry

3. **`Program.cs`** — Improved CLI reporting
   - Now reports all four generated files and their paths
   - Shows variant count and low-confidence count
   - Better visibility into extraction pipeline output

## CSV Output Structure

### `cards_ingestion.csv` (Base/Upgraded Default Path)
Columns ready for `cards` table insertion:
- `card_id`, `title`, `description`
- `type`, `rarity`, `energy_cost`, `target`, `gains_block`
- `keywords`
- `damage_base`, `damage_upgraded` (standard pattern)
- `block_base`, `block_upgraded`
- `magic_base`, `magic_upgraded`
- `max_upgrade`
- `on_play_source`, `on_upgrade_source`
- `effect_tags`, `effect_description`
- `resources_generated`, `resources_consumed`
- `scaling_type`, `anti_synergy_tags`

### `card_variants_staging.csv` (Exceptions Only)
Columns for `card_variants` table:
- `card_id`, `variant_kind`, `upgrade_level`, `source_id`
- `energy_cost`, `damage_value`, `block_value`, `magic_value` (single values, not base/upgraded)
- `effect_tags`, `effect_description`
- `resources_generated`, `resources_consumed`
- `scaling_type`, `anti_synergy_tags`

**Current Status**: Variants table is **empty for standard STS2 cards** — this is correct. Non-standard upgrades (if discovered) will populate this in future iterations.

## How It Works

1. **Extraction Phase** → `CardExtractionRecord` (raw parsed data)
2. **Conversion Phase** → Split via `CardIngestionConverter`:
   - ✅ All cards → `CardIngestionRecord` (base/upgraded)
   - ❌ Exceptions (rare) → `CardVariantRecord`
3. **Ingestion Phase** (future):
   - Load `cards_ingestion.csv` into `cards` table
   - Load `card_variants_staging.csv` into `card_variants` table (usually empty)
   - Load `card_companion_actions_staging.csv` into `card_companion_actions` table

## STS2 Observations

- **MaxUpgradeLevel**: Nearly all cards use `MaxUpgradeLevel = 1` (standard upgrade) or `MaxUpgradeLevel = 0` (unupgradable)
- **Multi-upgrade cards**: Extremely rare or non-existent in current decompiled codebase
- **Variants expectation**: For Ironclad and similar characters, expect variant count = 0 (all standard cards)

## Next Steps

1. Run extraction on a single character (Ironclad) to verify output format
2. Validate ingestion CSV against actual game card definitions
3. Annotate `effect_tags`, `effect_description`, `resources_generated/consumed`, `scaling_type` via LLM
4. Load base Ironclad deck into PostgreSQL and validate schema
5. Scale to remaining characters (Survivor, Ironclad alternate, etc.)

## Build Status
✅ All code compiles successfully.
