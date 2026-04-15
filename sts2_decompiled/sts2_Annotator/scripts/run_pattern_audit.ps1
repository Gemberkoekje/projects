param(
    [string]$Root = "sts2",
    [string]$Output = "pattern_audit.csv",
    [int]$Cards = 20,
    [int]$Relics = 10,
    [int]$Powers = 10,
    [decimal]$Threshold = 0.85
)

dotnet run --project "sts2_Annotator/sts2_Annotator.csproj" -- audit --root $Root --output $Output --cards $Cards --relics $Relics --powers $Powers --threshold $Threshold
