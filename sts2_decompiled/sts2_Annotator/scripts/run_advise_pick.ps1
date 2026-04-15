param(
    [string]$Archetype = "",
    [string]$Deck = "",
    [string]$Relics = "",
    [string]$Options = "",
    [switch]$Explain,
    [string]$ConnectionString = "",
    [string]$Model = "",
    [string]$Provider = ""
)

$runArgs = @("advise-pick", "--options", $Options)

if (-not [string]::IsNullOrWhiteSpace($Archetype)) {
    $runArgs += @("--archetype", $Archetype)
}

if (-not [string]::IsNullOrWhiteSpace($Deck)) {
    $runArgs += @("--deck", $Deck)
}

if (-not [string]::IsNullOrWhiteSpace($Relics)) {
    $runArgs += @("--relics", $Relics)
}

if ($Explain) {
    $runArgs += "--explain"
}

if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
    $runArgs += @("--connection-string", $ConnectionString)
}

if (-not [string]::IsNullOrWhiteSpace($Model)) {
    $runArgs += @("--model", $Model)
}

if (-not [string]::IsNullOrWhiteSpace($Provider)) {
    $runArgs += @("--provider", $Provider)
}

dotnet run --project "sts2_Annotator/sts2_Annotator.csproj" -- @runArgs
