# The M1 cost ledger (vista-log/cost-ledger.md): renders the four measurement views at 3840x2160, 64 spp, seed 42 on
# the local GPU and prints the mean and worst-dispatch cost in nanoseconds per pixel-sample, the numbers the
# ledger records after each slice. Build first: dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-ledger'),
    [string]$App = (Join-Path $PSScriptRoot '..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll')
)

$views = @(
    @{ Name = 'sky';        Recipe = 'mountains-day';    Camera = 'sky' },
    @{ Name = 'aerial';     Recipe = 'mountains-day';    Camera = 'aerial' },
    @{ Name = 'default';    Recipe = 'mountains-day';    Camera = 'default' },
    @{ Name = 'down-slope'; Recipe = 'mountains-golden'; Camera = 'down-slope' }
)

New-Item -ItemType Directory -Force $Out | Out-Null
foreach ($view in $views) {
    $png = Join-Path $Out "$($view.Name).png"
    & dotnet $App --preview --seed 42 --width 3840 --spp 64 --out $png `
        --recipe (Join-Path $PSScriptRoot "$($view.Recipe).recipe.json") `
        --camera (Join-Path $PSScriptRoot "$($view.Camera).camera.json") | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Rendering $($view.Name) failed." }
    $stats = (Get-Content (Join-Path $Out "$($view.Name).json") -Raw | ConvertFrom-Json).render.stats
    $mean = $stats.traceSeconds * 1e9 / $stats.pixelSamples
    [string]::Format([cultureinfo]::InvariantCulture,
        '{0,-11} {1,6:0.00} ns mean  {2,6:0.00} ns worst dispatch  {3,5:0.00} rays per sample',
        $view.Name, $mean, $stats.maxCostNsPerPixelSample, ($stats.raysTraced / $stats.pixelSamples))
}
