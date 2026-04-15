param(
    [string]$Root = "sts2",
    [string]$Output = "output/postgres_sync",
    [int]$BatchSize = 50,
    [string]$ConnectionString = "",
    [string]$Model = "",
    [switch]$Resume
)

$ErrorActionPreference = "Stop"

Write-Host "[1/1] Syncing extractor outputs and rebuilding synergy + affinity data..." -ForegroundColor Cyan
# sync-postgres now regenerates:
# - entity_synergy_edges
# - entity_strength_ratings
# - archetypes/synergy_clusters
# - discover-archetypes + score-affinities (entity_archetype_affinity)

$args = @("sync-postgres", "--root", $Root, "--output", $Output, "--batch-size", $BatchSize)
if ($Resume.IsPresent) {
    $args += "--resume"
}
if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
    $args += @("--connection-string", $ConnectionString)
}
if (-not [string]::IsNullOrWhiteSpace($Model)) {
    $args += @("--model", $Model)
}

dotnet run --project "sts2_Annotator/sts2_Annotator.csproj" -- @args
if ($LASTEXITCODE -ne 0) { throw "sync-postgres failed" }

Write-Host "Synergy refresh complete." -ForegroundColor Green
Write-Host "Staging output: $Output" -ForegroundColor Green
