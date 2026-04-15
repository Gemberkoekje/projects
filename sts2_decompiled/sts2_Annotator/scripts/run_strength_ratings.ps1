param(
    [string]$Root = "sts2",
    [string]$Output = "entity_strength_ratings_staging.csv"
)

dotnet run --project "sts2_Annotator/sts2_Annotator.csproj" -- generate-ratings --root $Root --output $Output
