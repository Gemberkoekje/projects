param(
    [string]$Root = "sts2",
    [int]$BatchSize = 25,
    [string]$ConnectionString = "",
    [string]$Provider = "anthropic",
    [string]$Model = ""
)

$ErrorActionPreference = "Stop"

$args = @("score-affinities", "--root", $Root, "--all-characters", "--provider", $Provider, "--batch-size", $BatchSize)
if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
    $args += @("--connection-string", $ConnectionString)
}
if (-not [string]::IsNullOrWhiteSpace($Model)) {
    $args += @("--model", $Model)
}

dotnet run --project "sts2_Annotator/sts2_Annotator.csproj" -- @args
if ($LASTEXITCODE -ne 0) { throw "score-affinities failed" }
