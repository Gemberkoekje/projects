param(
    [string]$Root = "sts2",
    [string]$Output = "output/postgres_sync",
    [string]$ConnectionString = "",
    [switch]$AllCharacters,
    [switch]$Annotate
)

$args = @("sync-postgres", "--root", $Root, "--output", $Output)
if ($AllCharacters.IsPresent) {
    $args += "--all-characters"
}

if ($Annotate.IsPresent) {
    $args += "--annotate"
}

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    dotnet run --project "sts2_Annotator/sts2_Annotator.csproj" -- @args
}
else {
    dotnet run --project "sts2_Annotator/sts2_Annotator.csproj" -- @args --connection-string $ConnectionString
}
