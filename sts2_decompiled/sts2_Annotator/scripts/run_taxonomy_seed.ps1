param(
    [string]$Root = "sts2",
    [string]$Output = "effect_taxonomy_seed.csv"
)

dotnet run --project "sts2_Annotator/sts2_Annotator.csproj" -- seed-taxonomy --root $Root --output $Output
