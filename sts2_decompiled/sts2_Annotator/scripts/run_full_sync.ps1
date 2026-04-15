param(
    [string]$Root = "sts2",
    [string]$Output = "output/full_sync",
    [string]$ConnectionString = ""
)

$ErrorActionPreference = "Stop"

Write-Host "Starting full multi-character sync..." -ForegroundColor Cyan

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    dotnet run --project "sts2_Annotator/sts2_Annotator.csproj" -- sync-postgres --root $Root --output $Output --all-characters
}
else {
    dotnet run --project "sts2_Annotator/sts2_Annotator.csproj" -- sync-postgres --root $Root --output $Output --all-characters --connection-string $ConnectionString
}

if ($LASTEXITCODE -ne 0) {
    throw "full sync failed"
}

Write-Host "Full multi-character sync complete." -ForegroundColor Green
Write-Host "Staging output: $Output" -ForegroundColor Green
