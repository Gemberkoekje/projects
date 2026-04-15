# Quick Reference: Running Card Extraction (Step 9)

## Extract Cards Command

```powershell
dotnet run --project .\sts2_Annotator\sts2_Annotator.csproj -- `
  extract-cards `
  --root-path "D:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2_decompiled" `
  --output-path ".\extraction_output\cards_extract.csv"
```

## Generated Output Files

After running `extract-cards`, you'll get **4 CSV files** in the output directory:

### 1. `cards_extract.csv` — Audit Trail
- **Purpose**: QA and confidence analysis
- **Columns**: file, card_id, id_resolution_status, roslyn_fallback_used, energy_cost, type, rarity, target, gains_block, max_upgrade, damage_base, damage_upgraded, block_base, block_upgraded, magic_base, magic_upgraded, confidence_score, effect_tags, companion_actions, on_play_source, on_upgrade_source
- **Use**: Identify low-confidence cards; target with Roslyn review

### 2. `cards_ingestion.csv` — PRIMARY INGESTION PATH ✨
- **Purpose**: Direct insertion into PostgreSQL `cards` table
- **Columns**: card_id, title, description, type, rarity, energy_cost, target, gains_block, keywords, damage_base, damage_upgraded, block_base, block_upgraded, magic_base, magic_upgraded, max_upgrade, on_play_source, on_upgrade_source, effect_tags, effect_description, resources_generated, resources_consumed, scaling_type, anti_synergy_tags
- **Use**: Load into DB after annotation
- **Expected Format**: Base/upgraded pairs (STS2 standard)

### 3. `card_variants_staging.csv` — Exception Cards
- **Purpose**: Non-standard upgrades (usually empty for STS2)
- **Columns**: card_id, variant_kind, upgrade_level, source_id, energy_cost, damage_value, block_value, magic_value, effect_tags, effect_description, resources_generated, resources_consumed, scaling_type, anti_synergy_tags
- **Expected**: Sparse/empty for standard STS2 cards
- **Use**: When found, insert into `card_variants` table

### 4. `card_companion_actions_staging.csv` — Companion Mechanics
- **Purpose**: Osty and other companion-driven actions
- **Columns**: card_id, companion_id, action_tag, source_method
- **Use**: Insert into `card_companion_actions` table

## Next Steps After Extraction

### Step 1: Validate Extraction
```bash
# Count records in each CSV
wc -l .\extraction_output\cards_*.csv
# Expected: ~300 cards_ingestion (Ironclad + shared), ~0 card_variants
```

### Step 2: Annotate (Mechanical Pass)
```powershell
# Use Haiku-class model for low-cost batch annotation
dotnet run --project .\sts2_Annotator\sts2_Annotator.csproj -- `
  annotate `
  --root-path "D:\SteamLibrary\...\sts2_decompiled" `
  --input-path ".\extraction_output\cards_ingestion.csv" `
  --output-path ".\extraction_output\cards_annotated.csv" `
  --provider anthropic `
  --task mechanical
```

### Step 3: Load into PostgreSQL
```sql
-- Assuming cards_ingestion.csv is prepared/annotated
COPY cards (card_id, title, description, type, rarity, energy_cost, target, gains_block, keywords, damage_base, damage_upgraded, block_base, block_upgraded, magic_base, magic_upgraded, max_upgrade, on_play_source, on_upgrade_source, effect_tags, effect_description, resources_generated, resources_consumed, scaling_type, anti_synergy_tags)
FROM STDIN WITH CSV HEADER;
-- Paste file content
```

## Understanding the Output

### Example: Ironclad's "Bash"
From `cards_ingestion.csv`:
```
card_id,type,rarity,energy_cost,damage_base,damage_upgraded,max_upgrade,...
Bash,Attack,Basic,1,8,10,1,...
```

- **damage_base**: 8 (without upgrade)
- **damage_upgraded**: 10 (with one upgrade)
- **max_upgrade**: 1 (can upgrade once)

### Example: Status Card (No Upgrade)
```
card_id,type,rarity,energy_cost,damage_base,damage_upgraded,max_upgrade,...
Slimed,Status,Status,,,0,...
```

- **damage_base**: empty (no damage)
- **damage_upgraded**: empty (no upgrade path)
- **max_upgrade**: 0 (not upgradable)

## Troubleshooting

### Empty `card_variants_staging.csv`
- **This is expected for STS2**. Variants only appear for non-standard upgrades.
- If you see records here, it indicates unusual cards found (possibly enchantments or special states).

### Low Confidence in `confidence_score` Column
- Look for cards with `confidence_score < 0.75` in audit CSV
- These may need Roslyn review or manual inspection
- Typically due to missing `CanonicalVars` or unusual class structure

### Missing `on_play_source` or `on_upgrade_source`
- Some cards may have empty method sources
- This is normal for simple cards (e.g., Strike has no special OnPlay logic)
- Annotation pass will handle these with default tags

## File Sizes (Reference)

For a full character extraction (Ironclad):
- `cards_extract.csv`: ~300 rows, ~500 KB (includes method source code)
- `cards_ingestion.csv`: ~300 rows, ~150 KB (ingestion-ready)
- `card_variants_staging.csv`: ~0 rows, ~50 B (header only)
- `card_companion_actions_staging.csv`: ~5-10 rows, ~1 KB (if Osty present)

## Key Takeaways

✅ **Base/Upgraded Default**: All cards use two columns for values (not variants)
✅ **Variants for Exceptions**: Only non-standard upgrades → separate table
✅ **Companion Actions Split**: Separate CSV for one-to-many relationship
✅ **Annotation Deferred**: effect_tags, resources, scaling populated later
✅ **QA Trail Preserved**: Audit CSV for confidence review and Roslyn targeting
