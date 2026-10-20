# The M1.5 sun and sky sampling checks (vista-plan section 10, M1.5) on the local GPU: M1.4's estimator against MIS on a
# dawn, two golden-hour and a day view (agreement at 4096 spp over 32 independent renders, variance at equal samples, time
# per sample, and the Russian-roulette sweep), the null test (MIS against itself on other streams), and a 16 spp pair of
# each view to look at. Build first: dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-sampling'),
    [string]$App = (Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll'),
    [int]$Replicates = 32,
    [int]$Spp = 128
)

New-Item -ItemType Directory -Force $Out | Out-Null
$views = Join-Path $PSScriptRoot '..'
$cases = @(
    @{ Name = 'dawn';               Recipe = (Join-Path $PSScriptRoot 'mountains-dawn.recipe.json'); Camera = (Join-Path $views 'default.camera.json') },
    @{ Name = 'golden-down-slope';  Recipe = (Join-Path $views 'mountains-golden.recipe.json');      Camera = (Join-Path $views 'down-slope.camera.json') },
    @{ Name = 'golden-default';     Recipe = (Join-Path $views 'mountains-golden.recipe.json');      Camera = (Join-Path $views 'default.camera.json') },
    @{ Name = 'day';                Recipe = (Join-Path $views 'mountains-day.recipe.json');         Camera = (Join-Path $views 'default.camera.json') }
)

function Run([string[]]$arguments) {
    & dotnet $App @arguments
    if ($LASTEXITCODE -ne 0) { throw "Vista.App $($arguments -join ' ') failed." }
}

foreach ($case in $cases) {
    # Agreement, variance at equal samples, time per sample, and where Russian roulette should start.
    Run @('--sampling-compare', '--recipe', $case.Recipe, '--camera', $case.Camera, '--spp', $Spp, '--replicates', $Replicates,
          '--rr-starts', '0,1,2,3,4,64', '--out', (Join-Path $Out $case.Name))
    # One render of each estimator at 16 spp, 640 px wide, to look at.
    Run @('--sampling-compare', '--recipe', $case.Recipe, '--camera', $case.Camera, '--spp', '16', '--replicates', '2', '--width', '640',
          '--out', (Join-Path $Out "$($case.Name)-16spp"))
}

# The null test: what the statistics look like when nothing differs.
Run @('--sampling-compare', '--recipe', $cases[1].Recipe, '--camera', $cases[1].Camera, '--spp', $Spp, '--replicates', $Replicates,
      '--baseline', 'mis', '--out', (Join-Path $Out 'null-test'))
