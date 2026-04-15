param(
    [string]$Root = "sts2",
    [string]$ConnectionString = "",
    [string]$Provider = "anthropic",
    [string]$Model = ""
)

$ErrorActionPreference = "Stop"

$args = @("discover-archetypes", "--root", $Root, "--all-characters", "--provider", $Provider)
if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
    $args += @("--connection-string", $ConnectionString)
}
if (-not [string]::IsNullOrWhiteSpace($Model)) {
    $args += @("--model", $Model)
}

dotnet run --project "sts2_Annotator/sts2_Annotator.csproj" -- @args
if ($LASTEXITCODE -ne 0) { throw "discover-archetypes failed" }
