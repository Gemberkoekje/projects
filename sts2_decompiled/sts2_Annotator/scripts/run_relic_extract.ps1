param(
    [string]$Root = "sts2",
    [string]$Output = "relic_extract_preview.csv",
    [decimal]$Threshold = 0.85
)

dotnet run --project "sts2_Annotator/sts2_Annotator.csproj" -- extract-relics --root $Root --output $Output --threshold $Threshold
