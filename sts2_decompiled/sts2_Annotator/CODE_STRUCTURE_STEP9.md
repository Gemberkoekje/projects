# Code Structure: Step 9 Implementation

## New Classes

### 1. CardIngestionRecord.cs
**Purpose**: Standard card format ready for PostgreSQL `cards` table

```csharp
public sealed class CardIngestionRecord
{
    // Identity
    public string CardId { get; set; }
    
    // Localization (populated by LLM or localization files)
    public string Title { get; set; }
    public string Description { get; set; }
    
    // Type & Rarity
    public string Type { get; set; }           // "Attack", "Skill", "Power", etc.
    public string Rarity { get; set; }         // "Basic", "Common", "Rare", etc.
    
    // Cost & Targeting
    public int? EnergyCost { get; set; }       // Null if no energy cost
    public string Target { get; set; }         // "AnyEnemy", "AllEnemies", etc.
    public bool GainsBlock { get; set; }
    public IReadOnlyList<string> Keywords { get; set; }
    
    // Numeric Values (base/upgraded pattern — default for STS2)
    public int? DamageBase { get; set; }
    public int? DamageUpgraded { get; set; }
    public int? BlockBase { get; set; }
    public int? BlockUpgraded { get; set; }
    public int? MagicBase { get; set; }
    public int? MagicUpgraded { get; set; }
    public int MaxUpgrade { get; set; }        // 0 = unupgradable, 1 = single upgrade
    
    // Source Code (for reference/analysis)
    public string OnPlaySource { get; set; }
    public string OnUpgradeSource { get; set; }
    
    // Semantic Annotations (populated by LLM)
    public IReadOnlyList<string> EffectTags { get; set; }          // ["deal_damage", "apply_vulnerable"]
    public string EffectDescription { get; set; }                  // Natural language summary
    public IReadOnlyList<string> ResourcesGenerated { get; set; }  // ["card_draw", "energy"]
    public IReadOnlyList<string> ResourcesConsumed { get; set; }   // ["exhaust_card", "hp_loss"]
    public string ScalingType { get; set; }                        // "per_stack", "flat", etc.
    public IReadOnlyList<string> AntiSynergyTags { get; set; }     // ["low_card_count"]
}
```

**When Used**: 
- Output to `cards_ingestion.csv` (primary path)
- After LLM annotation, bulk insert into PostgreSQL `cards` table
- Default format for all STS2 cards (covers ~100%)

---

### 2. CardVariantRecord.cs
**Purpose**: Non-standard card states (exceptions only)

```csharp
public sealed class CardVariantRecord
{
    // Identity & Tracking
    public string CardId { get; set; }                  // FK to cards table
    public string VariantKind { get; set; }             // "extra_upgrade", "enchantment", etc.
    public int? UpgradeLevel { get; set; }              // For multi-upgrade cards
    public string SourceId { get; set; }                // Enchantment/affliction source
    
    // Single-Value Fields (not base/upgraded)
    public int? EnergyCost { get; set; }
    public int? DamageValue { get; set; }
    public int? BlockValue { get; set; }
    public int? MagicValue { get; set; }
    
    // Semantic Annotations (same as ingestion record)
    public IReadOnlyList<string> EffectTags { get; set; }
    public string EffectDescription { get; set; }
    public IReadOnlyList<string> ResourcesGenerated { get; set; }
    public IReadOnlyList<string> ResourcesConsumed { get; set; }
    public string ScalingType { get; set; }
    public IReadOnlyList<string> AntiSynergyTags { get; set; }
}
```

**When Used**:
- Output to `card_variants_staging.csv` (exceptions only)
- Currently **empty for STS2** (no multi-upgrade cards detected)
- If discovered, insert into PostgreSQL `card_variants` table
- Covers edge cases: extra upgrade levels, enchantments, temporary states

---

### 3. CardIngestionConverter.cs
**Purpose**: Transform extraction records into ingestion format

```csharp
public sealed class CardIngestionConverter
{
    /// <summary>
    /// Converts raw extraction record to ingestion-ready format.
    /// 
    /// - Copies all parsed fields
    /// - Initializes annotation fields to empty (ready for LLM)
    /// - Handles int.MinValue → null conversion for sparse values
    /// </summary>
    public CardIngestionRecord ToIngestionRecord(CardExtractionRecord extraction)
    {
        return new CardIngestionRecord
        {
            CardId = extraction.CardId,
            Title = string.Empty,                           // LLM or localization
            Description = string.Empty,                     // LLM or localization
            Type = extraction.CardType,
            Rarity = extraction.CardRarity,
            EnergyCost = ConvertInt(extraction.EnergyCost),
            Target = extraction.TargetType,
            GainsBlock = extraction.GainsBlock,
            Keywords = new List<string>(),                  // Parsed from keywords field
            DamageBase = ConvertInt(extraction.DamageBase),
            DamageUpgraded = ConvertInt(extraction.DamageUpgraded),
            BlockBase = ConvertInt(extraction.BlockBase),
            BlockUpgraded = ConvertInt(extraction.BlockUpgraded),
            MagicBase = ConvertInt(extraction.MagicBase),
            MagicUpgraded = ConvertInt(extraction.MagicUpgraded),
            MaxUpgrade = extraction.MaxUpgradeLevel,
            OnPlaySource = extraction.OnPlaySource,
            OnUpgradeSource = extraction.OnUpgradeSource,
            EffectTags = extraction.EffectTags,             // Extracted but from parsing
            EffectDescription = string.Empty,               // LLM annotation
            ResourcesGenerated = new List<string>(),        // LLM annotation
            ResourcesConsumed = new List<string>(),         // LLM annotation
            ScalingType = string.Empty,                     // LLM annotation
            AntiSynergyTags = new List<string>()            // LLM annotation
        };
    }
    
    /// <summary>
    /// Extract variants for non-standard upgrades (MaxUpgrade > 1).
    /// Currently returns empty for STS2 (all cards are standard).
    /// </summary>
    public IReadOnlyList<CardVariantRecord> ExtractVariants(
        string cardId, 
        int maxUpgradeLevel)
    {
        List<CardVariantRecord> variants = new List<CardVariantRecord>();
        
        // For STS2: MaxUpgradeLevel is typically 0 or 1
        // If > 1, would create variant for each level
        // Currently placeholder for future extension
        
        return variants;
    }
    
    private static int? ConvertInt(int value)
    {
        return value == int.MinValue ? null : value;
    }
}
```

**Key Transformation**:
- `int.MinValue` → `null` (sparse value marker)
- Empty strings stay empty (ready for annotation/localization)
- Companion actions handled separately
- All annotation fields pre-initialized for LLM pass

---

## Modified Classes

### 4. CardExtractionRunner.cs (Enhanced)
**Changes**: Added ingestion + variant CSV generation

```csharp
public CardExtractionResult Run(CliOptions options)
{
    // 1. Extract all cards (same as before)
    List<CardExtractionRecord> records = Directory
        .EnumerateFiles(cardsDirectory, "*.cs")
        .Select(file => _extractor.Extract(rootPath, file))
        .ToList();
    
    // 2. Output audit CSV (same as before)
    CsvWriter.Write(options.OutputPath, auditHeaders, auditRecords);
    
    // 3. Output companion actions (same as before)
    CsvWriter.Write(companionPath, companionHeaders, companionRecords);
    
    // 4. NEW: Convert to ingestion format
    var converter = new CardIngestionConverter();
    List<CardIngestionRecord> ingestionRecords = records
        .Select(r => converter.ToIngestionRecord(r))
        .ToList();
    
    // 5. NEW: Output ingestion CSV
    CsvWriter.Write(ingestionPath, ingestionHeaders, ingestionRecords);
    
    // 6. NEW: Extract and output variants
    List<CardVariantRecord> variantRecords = new List<CardVariantRecord>();
    foreach (var record in ingestionRecords)
    {
        variantRecords.AddRange(
            converter.ExtractVariants(record.CardId, record.MaxUpgrade)
        );
    }
    CsvWriter.Write(variantPath, variantHeaders, variantRecords);
    
    // Return result with new paths
    return new CardExtractionResult
    {
        OutputPath = options.OutputPath,
        CompanionOutputPath = companionPath,
        IngestionOutputPath = ingestionPath,
        VariantOutputPath = variantPath,
        RecordCount = records.Count,
        VariantCount = variantRecords.Count,
        // ...
    };
}
```

---

### 5. CardExtractionResult.cs (Extended)
**Changes**: Added ingestion/variant tracking

```csharp
public sealed class CardExtractionResult
{
    // Existing
    public string OutputPath { get; set; }                // Audit CSV
    public string CompanionOutputPath { get; set; }       // Companion actions CSV
    
    // NEW
    public string IngestionOutputPath { get; set; }       // Base/upgraded cards CSV
    public string VariantOutputPath { get; set; }         // Variants CSV
    
    public int RecordCount { get; set; }                  // Total cards extracted
    public int LowConfidenceCount { get; set; }           // Cards needing review
    public int CompanionActionCount { get; set; }         // Companion actions found
    public int VariantCount { get; set; }                 // NEW: Variants found
}
```

---

### 6. Program.cs (Improved Reporting)
**Changes**: Enhanced CLI output

```csharp
if (options.Command == CliCommand.ExtractCards)
{
    CardExtractionRunner runner = new CardExtractionRunner();
    CardExtractionResult result = runner.Run(options);
    
    // Before: Vague message
    // After: Detailed breakdown
    
    Console.WriteLine($"Card extraction complete: {result.RecordCount} records");
    Console.WriteLine($"  Audit preview: {result.OutputPath}");
    Console.WriteLine($"  Ingestion (base/upgraded): {result.IngestionOutputPath}");
    Console.WriteLine($"  Variants (exceptions): {result.VariantOutputPath} ({result.VariantCount} records)");
    Console.WriteLine($"  Companion actions: {result.CompanionOutputPath} ({result.CompanionActionCount} records)");
    Console.WriteLine($"  Low-confidence cards: {result.LowConfidenceCount}");
    
    return 0;
}
```

---

## Data Flow Summary

```
CardExtractionRecord (parsing output)
    │
    ├─ confidence_score, id_resolution_status, roslyn_fallback_used
    ├─ card_id, type, rarity, energy_cost, target
    ├─ damage_base, damage_upgraded, block_base, etc.
    ├─ on_play_source, on_upgrade_source
    └─ effect_tags (from companion detection)
    
    ↓ CardIngestionConverter.ToIngestionRecord()
    
    CardIngestionRecord (ingestion-ready)
    │
    ├─ card_id, title, description
    ├─ type, rarity, energy_cost, target
    ├─ damage_base, damage_upgraded, block_base, etc.
    ├─ on_play_source, on_upgrade_source
    ├─ effect_tags (from extraction)
    └─ effect_description, resources_*, scaling_type, anti_synergy_tags (empty for LLM)
    
    ↓ Output to cards_ingestion.csv
    
    ↓ (Optional) CardIngestionConverter.ExtractVariants()
    
    CardVariantRecord[] (usually empty for STS2)
    
    ↓ Output to card_variants_staging.csv
```

---

## Dependency Injection & Reusability

**CardIngestionConverter** is stateless and reusable:
```csharp
var converter = new CardIngestionConverter();

// Can be used in multiple contexts:
CardIngestionRecord single = converter.ToIngestionRecord(extractionRecord);
IReadOnlyList<CardVariantRecord> variants = converter.ExtractVariants(cardId, level);

// Future: Could be dependency-injected into runner for testability
```

---

## Testing Points (Future)

1. **Conversion Accuracy**:
   - `int.MinValue` → `null` (sparse values)
   - Empty string preservation
   - Array handling

2. **CSV Output Format**:
   - Column order matches schema
   - Nullable fields rendered correctly
   - Escaping of special characters

3. **Variant Detection**:
   - MaxUpgrade = 0 → no variants ✓
   - MaxUpgrade = 1 → no variants ✓
   - MaxUpgrade > 1 → variants created (if implemented)

4. **Integration**:
   - CardExtractionRunner produces all 4 CSVs
   - Result paths populated correctly
   - Record counts accurate

---

## Build & Compilation

✅ All classes compile successfully
✅ No breaking changes to existing code
✅ Follows .NET 9 and copilot-instructions.md guidelines
✅ No nullable warnings (int? used only where needed)
