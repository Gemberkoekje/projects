param(
    [string]$Root = "sts2",
    [string]$Output = "synergy_edges_staging.csv",
    [int]$BatchSize = 50,
    [string]$ConnectionString = "",
    [string]$Model = ""
)

$args = @("build-synergy-edges", "--root", $Root, "--output", $Output, "--batch-size", $BatchSize)

if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
    $args += @("--connection-string", $ConnectionString)
}

if (-not [string]::IsNullOrWhiteSpace($Model)) {
    $args += @("--model", $Model)
}

dotnet run --project "sts2_Annotator/sts2_Annotator.csproj" -- @args
