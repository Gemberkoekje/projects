# Before & After: Card Extraction Step 9

## Before Step 9

### Output Files (2 CSVs)
```
extraction_output/
├── card_extract_audit.csv          (only audit trail)
└── card_companion_actions_staging.csv
```

### Workflow
```
Raw Source Code
    ↓ (CardExtractor)
CardExtractionRecord
    ↓ (Direct CSV output)
    └→ card_extract_audit.csv (mixed format)
```

**Problem**: 
- Audit CSV contained confidence scores but mixed raw parsing data
- No dedicated ingestion-ready format
- Variant handling not implemented
- Data was "as-parsed" without separation of concerns

## After Step 9

### Output Files (4 CSVs)
```
extraction_output/
├── cards_extract.csv                      (audit + QA telemetry)
├── cards_ingestion.csv                    ✨ PRIMARY PATH (base/upgraded)
├── card_variants_staging.csv              (exceptions - usually empty)
└── card_companion_actions_staging.csv
```

### Workflow
```
Raw Source Code
    ↓ (CardExtractor)
CardExtractionRecord
    ↓ (CardIngestionConverter)
    ├→ CardIngestionRecord      → cards_ingestion.csv       (DB-ready)
    └→ CardVariantRecord[]      → card_variants_staging.csv (exceptions)
                                
Companion Actions extracted separately
    └→ card_companion_actions_staging.csv
```

**Benefit**:
- ✅ Clear separation: audit ≠ ingestion
- ✅ Three distinct ingestion paths (base/upgraded, variants, companions)
- ✅ All values nullable for sparse cards
- ✅ Annotation fields pre-allocated (empty, ready for LLM)
- ✅ Schema-aligned format from day one

---

## Data Transformation Example

### Raw Extraction (CardExtractionRecord)
```
CardId: "Bash"
EnergyCost: 1
CardType: "Attack"
CardRarity: "Basic"
TargetType: "AnyEnemy"
DamageBase: 8
DamageUpgraded: 10
BlockBase: int.MinValue
BlockUpgraded: int.MinValue
MagicBase: int.MinValue
MagicUpgraded: int.MinValue
MaxUpgradeLevel: 1
OnPlaySource: "public void OnPlay() { ... }"
OnUpgradeSource: "public void OnUpgrade() { ... }"
ConfidenceScore: 0.95
IdResolutionStatus: "class_name"
RoslynFallbackUsed: false
```

### Step 1: Convert to Ingestion
```
CardIngestionRecord:
  CardId: "Bash"
  Title: ""                                    ← Empty (for LLM)
  Description: ""                              ← Empty (for LLM)
  Type: "Attack"
  Rarity: "Basic"
  EnergyCost: 1
  Target: "AnyEnemy"
  DamageBase: 8
  DamageUpgraded: 10
  BlockBase: null                              ← Sparse value
  BlockUpgraded: null
  MagicBase: null
  MagicUpgraded: null
  MaxUpgrade: 1
  OnPlaySource: "public void OnPlay() { ... }"
  OnUpgradeSource: "public void OnUpgrade() { ... }"
  EffectTags: ["companion_synergy", "osty_attack"]
  EffectDescription: ""                        ← Empty (for LLM)
  ResourcesGenerated: []                       ← Empty (for LLM)
  ResourcesConsumed: []                        ← Empty (for LLM)
  ScalingType: ""                              ← Empty (for LLM)
  AntiSynergyTags: []                          ← Empty (for LLM)
```

### Step 2: Check for Variants
```
ExtractVariants("Bash", maxUpgradeLevel: 1)
  ├─ Is maxUpgradeLevel > 1? NO
  └─ Return: [] (empty)

Result: No variant records created (standard card)
```

### Step 3: Output to CSV

#### `cards_ingestion.csv`
```csv
card_id,title,description,type,rarity,energy_cost,target,gains_block,keywords,damage_base,damage_upgraded,block_base,block_upgraded,magic_base,magic_upgraded,max_upgrade,on_play_source,on_upgrade_source,effect_tags,effect_description,resources_generated,resources_consumed,scaling_type,anti_synergy_tags
Bash,,Attack,Basic,1,AnyEnemy,false,,,8,10,,,,1,"public void OnPlay() { ... }","public void OnUpgrade() { ... }",companion_synergy;osty_attack,,,,
```

**Note**: Empty columns are preserved (no values hidden)

#### `card_variants_staging.csv`
```csv
card_id,variant_kind,upgrade_level,source_id,energy_cost,damage_value,block_value,magic_value,effect_tags,effect_description,resources_generated,resources_consumed,scaling_type,anti_synergy_tags
[no rows for Bash]
```

---

## Card with No Numeric Values (e.g., "Slimed")

### Raw Extraction
```
CardId: "Slimed"
EnergyCost: int.MinValue
CardType: "Status"
CardRarity: "Status"
TargetType: "None"
DamageBase: int.MinValue
DamageUpgraded: int.MinValue
BlockBase: int.MinValue
BlockUpgraded: int.MinValue
MagicBase: int.MinValue
MagicUpgraded: int.MinValue
MaxUpgradeLevel: 0              ← Not upgradable
OnPlaySource: ""
OnUpgradeSource: ""
ConfidenceScore: 1.0
```

### Ingestion Format
```
card_id,type,rarity,energy_cost,damage_base,damage_upgraded,block_base,block_upgraded,magic_base,magic_upgraded,max_upgrade,...
Slimed,Status,Status,,,,,,,0,...
```

**Result**: All numeric columns empty ✓ (CSV correctly represents sparse data)

---

## Multi-Upgrade Card (Hypothetical)

If STS2 had a card with MaxUpgradeLevel = 2:

### Raw Extraction
```
CardId: "HypotheticalCard"
MaxUpgradeLevel: 2
DamageBase: 5
DamageUpgraded: 7        ← Level 1 upgrade
OnUpgradeSource: "... if (upgrade == 1) damage += 2; if (upgrade == 2) damage += 3;"
```

### Ingestion + Variants
```
CardIngestionRecord:
  DamageBase: 5
  DamageUpgraded: 7        ← Default: first upgrade
  MaxUpgrade: 2

CardVariantRecord (Level 2):
  CardId: "HypotheticalCard"
  VariantKind: "extra_upgrade"
  UpgradeLevel: 2
  DamageValue: 10          ← 5 + 2 + 3 (cumulative damage)
```

### Output
```
cards_ingestion.csv:
HypotheticalCard,...,5,7,...,2,...

card_variants_staging.csv:
HypotheticalCard,extra_upgrade,2,,,,10,...
```

**Result**: Base path uses base/upgraded, variants table handles level 2+ ✓

---

## Key Improvements

| Aspect | Before | After |
|--------|--------|-------|
| **Separation** | Mixed audit + data | Audit vs. ingestion vs. variants |
| **Schema Alignment** | "As parsed" format | Matches PostgreSQL exactly |
| **Sparse Data** | All values present (int.MinValue) | Nulls for missing values |
| **Annotation** | N/A | Pre-allocated empty fields ready for LLM |
| **Variant Handling** | Not implemented | Automatic extraction for exceptions |
| **Companion Actions** | Mixed with cards | Separate dedicated CSV |
| **Multi-Upgrade** | Not handled | Ready via card_variants table |
| **Future Expansion** | Requires schema changes | CardIngestionConverter update |

---

## When You Run `extract-cards` Now

```powershell
$ dotnet run --project .\sts2_Annotator\sts2_Annotator.csproj -- `
    extract-cards --root-path ... --output-path .\extraction_output\cards_extract.csv

Card extraction complete: 287 records
  Audit preview: .\extraction_output\cards_extract.csv
  Ingestion (base/upgraded): .\extraction_output\cards_ingestion.csv
  Variants (exceptions): .\extraction_output\card_variants_staging.csv (0 records)
  Companion actions: .\extraction_output\card_companion_actions_staging.csv (8 records)
  Low-confidence cards: 3
```

**Output ready for**:
1. ✅ Validation (compare with game data)
2. ✅ Annotation (LLM enrichment of ingestion CSV)
3. ✅ PostgreSQL ingestion (bulk INSERT)
4. ✅ Synergy analysis (Phase 2)
