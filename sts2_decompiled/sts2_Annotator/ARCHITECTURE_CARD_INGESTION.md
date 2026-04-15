# Card Ingestion Architecture — Step 9

## Data Flow Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│ Phase 1: Extraction (CardExtractor)                             │
│ Parses raw C# source → CardExtractionRecord                     │
│ - Fast pattern matching (class, constructor, variables)         │
│ - Roslyn fallback for low-confidence paths                      │
│ - Extracts: id, cost, type, rarity, target, damage, block, etc. │
│ - Captures method sources (OnPlay, OnUpgrade)                   │
│ - Detects companion actions (Osty)                              │
└──────────────────────────────────┬──────────────────────────────┘
                                   │
                                   ↓
┌──────────────────────────────────────────────────────────────────┐
│ Phase 2: Conversion (CardIngestionConverter)                    │
│ Transforms extraction record → ingestion + variants             │
│                                                                  │
│ For each CardExtractionRecord:                                  │
│   1. Create CardIngestionRecord (base/upgraded path)            │
│      - Copies all extracted fields                              │
│      - Initializes annotation fields (effect_tags, etc.) empty  │
│      - Ready for direct DB insertion                            │
│                                                                  │
│   2. Extract variants (exceptions only)                         │
│      - Check MaxUpgradeLevel > 1?                               │
│      - If yes → create CardVariantRecord for each extra level   │
│      - For STS2: typically empty (cards are standard)           │
└──────────────────────────────────┬──────────────────────────────┘
                                   │
                                   ↓
┌──────────────────────────────────────────────────────────────────┐
│ Phase 3: Output (CardExtractionRunner)                          │
│ Generates 4 CSV files for separate ingestion paths              │
│                                                                  │
│ CSV 1: extract-cards_audit.csv                                  │
│   - Raw extraction data with confidence scores                  │
│   - For QA and confidence analysis                              │
│   - Identifies low-confidence cards needing Roslyn review       │
│                                                                  │
│ CSV 2: cards_ingestion.csv ✨ PRIMARY PATH                       │
│   - CardIngestionRecords converted to CSV                       │
│   - Columns match PostgreSQL `cards` table schema               │
│   - Base/upgraded columns by default                            │
│   - Ready for bulk INSERT (after annotation)                    │
│                                                                  │
│ CSV 3: card_variants_staging.csv (exceptions)                   │
│   - CardVariantRecords for non-standard upgrades                │
│   - For STS2: typically empty (no multi-upgrade cards)          │
│   - Ready for bulk INSERT if exceptions found                   │
│                                                                  │
│ CSV 4: card_companion_actions_staging.csv                       │
│   - Companion mechanics (FromOsty, OstyCmd)                     │
│   - Separate ingestion path for card_companion_actions table    │
└──────────────────────────────────┬──────────────────────────────┘
                                   │
                                   ↓
┌──────────────────────────────────────────────────────────────────┐
│ Phase 4: Annotation (Future - LLM Batch)                        │
│ Enrich `cards_ingestion.csv` with LLM outputs                   │
│                                                                  │
│ For each card, populate:                                        │
│   - effect_tags (normalized behavior tags)                      │
│   - effect_description (natural language summary)               │
│   - resources_generated (what card produces)                    │
│   - resources_consumed (what card costs)                        │
│   - scaling_type (how effect scales)                            │
│   - anti_synergy_tags (conflicts to avoid)                      │
│                                                                  │
│ Uses task-based routing:                                        │
│   - Mechanical annotation: Haiku-class (low-cost, fast)         │
│   - Synergy reasoning: Sonnet-class (stronger, for ranking)     │
└──────────────────────────────────┬──────────────────────────────┘
                                   │
                                   ↓
┌──────────────────────────────────────────────────────────────────┐
│ Phase 5: DB Ingestion (PostgreSQL)                              │
│                                                                  │
│ 1. Load cards_ingestion.csv → INSERT INTO cards (base/upgraded) │
│ 2. Load card_variants_staging.csv → INSERT INTO card_variants   │
│ 3. Load card_companion_actions_staging.csv → INSERT INTO ...    │
│                                                                  │
│ Result: Fully indexed game database ready for synergy analysis  │
└──────────────────────────────────────────────────────────────────┘
```

## Class Relationships

```
CardExtractionRecord (raw parsing output)
  │
  ├── fields: card_id, energy_cost, type, rarity, target,
  │   damages, block, magic, on_play_source, on_upgrade_source,
  │   effect_tags, companion_actions, confidence_score, etc.
  │
  └─ used by CardExtractionRunner to populate:
     │
     ├─→ extract-cards_audit.csv (raw audit trail)
     │
     └─→ CardIngestionConverter.ToIngestionRecord()
          │
          ├─→ CardIngestionRecord (base/upgraded only)
          │    └─→ cards_ingestion.csv (PRIMARY PATH)
          │
          └─→ CardIngestionConverter.ExtractVariants()
               └─→ List<CardVariantRecord> (exceptions only)
                    └─→ card_variants_staging.csv
```

## Key Design Decisions

1. **Base/Upgraded by Default**
   - STS2 cards predominantly use MaxUpgradeLevel = 0 or 1
   - Single `base` + `upgraded` pair covers ~99% of cards
   - Avoids variant table bloat

2. **Conversion Layer**
   - `CardIngestionConverter` separates parsing from ingestion logic
   - Allows future modifications to conversion rules without touching extractor
   - Enables A/B testing of different schema mappings

3. **Separate Output Paths**
   - Audit CSV preserved for confidence analysis and Roslyn targeting
   - Ingestion CSVs use standard format (not audit-specific)
   - Companion actions split to separate table (one-to-many relationship)

4. **Nullable Integers for Missing Data**
   - Card may have no damage (status/power cards)
   - Using `int?` with null output as empty string in CSV
   - DB schema uses `INT` columns (NULLs allowed by default)

5. **Deferred Annotation**
   - `effect_tags`, `effect_description`, `resources_*`, `scaling_type`, `anti_synergy_tags` are initialized empty
   - LLM annotation phase fills these post-extraction
   - Preserves separation of concerns (parsing ≠ semantic analysis)

## STS2-Specific Notes

- **MaxUpgradeLevel = 0**: Card is unupgradable (e.g., basic strikes, statuses)
  - `damage_base` = `damage_upgraded` = same value
  - Schema still supports this; values are just identical

- **MaxUpgradeLevel = 1**: Standard upgrade (e.g., Bash, Defend)
  - `damage_base` < `damage_upgraded`
  - Common pattern

- **MaxUpgradeLevel > 1**: Multi-upgrade cards (rare/absent in STS2)
  - Would create variant records for upgrade levels 2, 3, etc.
  - Current implementation is ready for this if discovered

## Next Action Items

1. Test extraction on Ironclad cards
2. Verify `cards_ingestion.csv` format matches schema
3. Inspect variant count (expect ~0 for STS2)
4. Prepare annotation batches by character
5. Configure LLM model routing (Haiku for mechanical, Sonnet for synergy)
