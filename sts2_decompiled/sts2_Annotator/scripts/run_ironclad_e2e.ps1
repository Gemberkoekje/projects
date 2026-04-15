param(
    [string]$Root = "sts2",
    [string]$OutputDir = "output/ironclad_e2e",
    [string]$ConnectionString = "",
    [string]$Provider = "anthropic"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Ironclad End-to-End Pipeline ===" -ForegroundColor Cyan

# Step 1: Extract cards scoped to Ironclad
Write-Host "`n[1/6] Extracting Ironclad cards..." -ForegroundColor Yellow
dotnet run --project "sts2_Annotator/sts2_Annotator.csproj" -- extract-cards `
    --root $Root `
    --output "$OutputDir/card_extract_preview.csv" `
    --character ironclad
if ($LASTEXITCODE -ne 0) { throw "extract-cards failed" }

# Step 2: Extract colorless cards (character_id = NULL in DB; universally draftable)
Write-Host "`n[2/6] Extracting colorless cards..." -ForegroundColor Yellow
dotnet run --project "sts2_Annotator/sts2_Annotator.csproj" -- extract-cards `
    --root $Root `
    --output "$OutputDir/card_colorless_extract_preview.csv" `
    --character colorless
if ($LASTEXITCODE -ne 0) { throw "extract-cards (colorless) failed" }

# Step 3: Extract relics scoped to Ironclad
Write-Host "`n[3/6] Extracting Ironclad relics..." -ForegroundColor Yellow
dotnet run --project "sts2_Annotator/sts2_Annotator.csproj" -- extract-relics `
    --root $Root `
    --output "$OutputDir/relic_extract_preview.csv" `
    --character ironclad
if ($LASTEXITCODE -ne 0) { throw "extract-relics failed" }

# Step 4: Extract potions scoped to Ironclad
Write-Host "`n[4/6] Extracting Ironclad potions..." -ForegroundColor Yellow
dotnet run --project "sts2_Annotator/sts2_Annotator.csproj" -- extract-potions `
    --root $Root `
    --output "$OutputDir/potion_extract_preview.csv" `
    --character ironclad
if ($LASTEXITCODE -ne 0) { throw "extract-potions failed" }

# Step 5: Generate strength ratings from extracted data
Write-Host "`n[5/6] Generating entity strength ratings..." -ForegroundColor Yellow
dotnet run --project "sts2_Annotator/sts2_Annotator.csproj" -- generate-ratings `
    --root $Root `
    --output "$OutputDir/entity_strength_ratings_staging.csv" `
    --provider $Provider
if ($LASTEXITCODE -ne 0) { throw "generate-ratings failed" }

# Step 6: Sync to PostgreSQL
Write-Host "`n[6/6] Syncing to PostgreSQL..." -ForegroundColor Yellow
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    dotnet run --project "sts2_Annotator/sts2_Annotator.csproj" -- sync-postgres `
        --root $Root `
        --output $OutputDir
} else {
    dotnet run --project "sts2_Annotator/sts2_Annotator.csproj" -- sync-postgres `
        --root $Root `
        --output $OutputDir `
        --connection-string $ConnectionString
}
if ($LASTEXITCODE -ne 0) { throw "sync-postgres failed" }

Write-Host "`n=== Ironclad pipeline complete ===" -ForegroundColor Green
Write-Host "Output files in: $OutputDir" -ForegroundColor Green
Write-Host ""
Write-Host "Spot-check tags on key cards via psql:" -ForegroundColor Cyan
Write-Host "  SELECT id, effect_tags FROM cards WHERE id IN ('Bash','Inflame','Metallicize','Whirlwind');" -ForegroundColor Gray
Write-Host ""
Write-Host "To annotate on_play_source fields (optional, costs LLM tokens):" -ForegroundColor Cyan
Write-Host "  dotnet run --project sts2_Annotator/sts2_Annotator.csproj -- annotate --provider $Provider --task mechanical --prompt-file <prompt.txt>" -ForegroundColor Gray
