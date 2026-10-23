# The M1.12a checks (vista-plan section 10, M1.12a) on the local GPU:
#  - with -Before (a main build's Vista.App.dll), same: the four ledger views rendered by main and by this build's --quadrature engine,
#    640 px at 16 spp; prints whether each PNG and EXR is byte-identical;
#  - ledger.png and closeups.png: the ledger views and M1.12's views through the engine's table (before) and the bins (after), each with
#    its own incident meter, and a map of every pixel's dE76 between them (--quadrature-compare; 100 spp, so every pixel completes
#    the 50-wavelength cycle twice and the map shows the quadrature, not the noise); the shifts, exposures and red channels go to
#    quadrature.log;
#  - sky: the sky view at M1.11c's settings (480 px, 64 spp) for its share of red channels at 0, before and after;
#  - with -Before, cost: the four ledger views at 3840x2160, 64 spp, main and this build alternately, view by view, -Rounds rounds;
#    prints each render's ns per pixel-sample and the median ratio per view (a shared GPU's load moves from minute to minute).
# Build first: dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-quadrature'),
    [string]$App = '',
    [string]$Before = '',
    [int]$CellWidth = 320,
    [int]$Spp = 100,
    [int]$Rounds = 3,
    [string]$Sheets = 'same,ledger,closeups,sky,cost'
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell leaves $PSScriptRoot empty in the parameters' defaults when another parameter is given.
if ($App -eq '') { $App = Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll' }
New-Item -ItemType Directory -Force $Out | Out-Null
$views = Split-Path $PSScriptRoot -Parent
$day = Join-Path $views 'mountains-day.recipe.json'
$golden = Join-Path $views 'mountains-golden.recipe.json'
$make = $Sheets.Split(',')
$log = Join-Path $Out 'quadrature.log'

$ledger = @(
    @('sky', $day, 'sky.camera.json', @()),
    @('aerial', $day, 'aerial.camera.json', @()),
    @('default', $day, 'default.camera.json', @()),
    @('down-slope', $golden, 'down-slope.camera.json', @())
)
$closeups = @(
    @('range · afternoon', $day, 'atmosphere\range.camera.json', @('--sun-elevation', '25')),
    @('scree cone · 10:30', $day, 'materials\scree-cone.camera.json', @('--time', '10:30')),
    @('rock face · 10:30', $day, 'materials\rock-face.camera.json', @('--time', '10:30')),
    @('meadow · 10:30', $day, 'materials\meadow.camera.json', @('--time', '10:30'))
)

function Run([string]$dll, [string[]]$arguments) {
    & dotnet $dll @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Vista.App $($arguments -join ' ') failed." }
}

function Slug([string]$name) { ($name -replace '[^a-z0-9]+', '-').Trim('-') }

# A line to the console and to the log (UTF-8; Tee-Object would write UTF-16 in Windows PowerShell).
function Say([string]$line) {
    Write-Host $line
    Add-Content -Path $log -Value $line -Encoding UTF8
}

# main against this build's --quadrature engine, byte for byte.
if ($Before -ne '' -and $make -contains 'same') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'same') | Out-Null
    foreach ($view in $ledger) {
        $slug = Slug $view[0]
        $common = @('--preview', '--recipe', $view[1], '--seed', '42', '--camera', (Join-Path $views $view[2]), '--width', '640', '--spp', '16') + $view[3]
        Run $Before ($common + @('--out', (Join-Path $Out "same\main-$slug.png")))
        Run $App ($common + @('--quadrature', 'engine', '--out', (Join-Path $Out "same\engine-$slug.png")))
        foreach ($ext in @('png', 'exr')) {
            $a = (Get-FileHash (Join-Path $Out "same\main-$slug.$ext")).Hash
            $b = (Get-FileHash (Join-Path $Out "same\engine-$slug.$ext")).Hash
            $verdict = if ($a -eq $b) { 'byte-identical' } else { 'DIFFERENT' }
            Say "$($view[0]) $($ext): $verdict"
        }
    }
}

# Each view through both quadratures, and the dE76 map; the cells of one sheet.
function Compare-Views($list, [string]$sheet, [string]$title) {
    $tiles = @(); $labels = @()
    New-Item -ItemType Directory -Force (Join-Path $Out $sheet) | Out-Null
    foreach ($view in $list) {
        $slug = Slug $view[0]
        $png = Join-Path $Out "$sheet\$slug.png"
        $arguments = @('--quadrature-compare', '--recipe', $view[1], '--seed', '42', '--camera', (Join-Path $views $view[2]), '--width', $CellWidth,
            '--spp', $Spp) + $view[3] + @('--out', $png)
        $lines = & dotnet $App @arguments
        if ($LASTEXITCODE -ne 0) { throw "The comparison of $($view[0]) failed." }
        $lines | Where-Object { $_ -match "^$slug" } | ForEach-Object { Say $_ }
        $mean = (Get-Content $log | Where-Object { $_ -match "^${slug}: dE76 per pixel" } | Select-Object -Last 1) -replace '^.*the frame''s mean colour ([0-9.]+).*$', '$1'
        $tiles += @((Join-Path $Out "$sheet\$slug.engine.png"), (Join-Path $Out "$sheet\$slug.bins.png"), (Join-Path $Out "$sheet\$slug.deltaE.png"))
        $labels += @("$($view[0]) · engine (before)", "$($view[0]) · bins (after)", "$($view[0]) · dE76 · the mean colour $mean")
    }
    Run $App (@('--sheet-from') + $tiles + @('--columns', '3', '--labels', ($labels -join ','), '--title', $title, '--out', (Join-Path $Out "$sheet.png")))
}

if ($make -contains 'ledger') {
    Compare-Views $ledger 'ledger' "M1.12a the ledger views · the engine's wavelengths left · the bins middle · dE76 right: black 0, blue 1.5, magenta 3, orange 4.5, white 6"
}
if ($make -contains 'closeups') {
    Compare-Views $closeups 'closeups' "M1.12a M1.12's views · the engine's wavelengths left · the bins middle · dE76 right: black 0, blue 1.5, magenta 3, orange 4.5, white 6"
}

# The sky view's red channel at M1.11c's settings.
if ($make -contains 'sky') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'sky') | Out-Null
    $lines = & dotnet $App --quadrature-compare --recipe $day --seed 42 --camera (Join-Path $views 'sky.camera.json') --width 480 --spp 64 --out (Join-Path $Out 'sky\sky-480.png')
    if ($LASTEXITCODE -ne 0) { throw 'The sky comparison failed.' }
    $lines | Where-Object { $_ -match '^sky-480' } | ForEach-Object { Say $_ }
    # The shifts the CI's spectra predict, printed by every comparison.
    $lines | Where-Object { $_ -match '^(The shift|  )' } | ForEach-Object { Say $_ }
}

# The cost ledger: main and this build alternately, view by view.
if ($Before -ne '' -and $make -contains 'cost') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'cost') | Out-Null
    $ratios = @{}
    for ($round = 1; $round -le $Rounds; $round++) {
        foreach ($view in $ledger) {
            $slug = Slug $view[0]
            $ns = @{}
            foreach ($build in @(@('main', $Before), @('m112a', $App))) {
                $png = Join-Path $Out "cost\$($build[0])-$slug.png"
                Run $build[1] (@('--preview', '--recipe', $view[1], '--seed', '42', '--camera', (Join-Path $views $view[2]), '--width', '3840', '--spp', '64') + $view[3] + @('--out', $png))
                $stats = (Get-Content ([IO.Path]::ChangeExtension($png, '.json')) -Raw | ConvertFrom-Json).render.stats
                $ns[$build[0]] = $stats.traceSeconds * 1e9 / $stats.pixelSamples
                $ns["$($build[0])-rays"] = $stats.raysTraced / $stats.pixelSamples
            }
            $ratio = $ns['m112a'] / $ns['main']
            if (-not $ratios.ContainsKey($slug)) { $ratios[$slug] = @() }
            $ratios[$slug] += $ratio
            Say ([string]::Format([cultureinfo]::InvariantCulture, 'round {0} {1,-11} main {2,6:0.00} ns ({3:0.00} rays)  M1.12a {4,6:0.00} ns ({5:0.00} rays)  ratio {6:0.000}',
                $round, $slug, $ns['main'], $ns['main-rays'], $ns['m112a'], $ns['m112a-rays'], $ratio))
        }
    }
    foreach ($view in $ledger) {
        $slug = Slug $view[0]
        $sorted = $ratios[$slug] | Sort-Object
        Say ([string]::Format([cultureinfo]::InvariantCulture, 'median ratio {0,-11} {1:0.000} ({2:0.000}-{3:0.000})', $slug, $sorted[[int][math]::Floor($sorted.Count / 2)],
            $sorted[0], $sorted[-1]))
    }
}
