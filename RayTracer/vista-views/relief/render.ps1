# The M1.11d checks (vista-plan section 10, M1.11d) on the local GPU:
#  - the measures of M1.11a's world (this build with M1.11a's uplift and no hardness: -M111a) and of this build's on four seeds:
#    --erosion-measure, with the drainage, the relief (local relief at 1 and 5 km, the summits), the slopes (the facets) and the
#    rock's hardness, and the uplift and hardness maps;
#  - the maps sheet: per seed, this build's uplift map and its hardness map over the eroded relief, reduced to -MapSize pixels;
#  - with -Before (a main build's Vista.App.dll): the four seeds from the aerial camera, and the seed-42 views (range, layered
#    ridges, then the default and down-slope cameras of -BeforeRef, main before M1.11d, against the re-placed ones), main left,
#    this build right, in a low
#    afternoon sun (the day recipe with the sun at 12 degrees, as M1.11a's sheets), cells -CellWidth wide.
# Worlds are cached in -Worlds (default: a folder under -Out, so the user's cache is untouched). Build first:
# dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-relief'),
    [string]$App = '',
    [string]$Before = '',
    [string]$BeforeRef = '1a741723',
    [string]$Worlds = '',
    [int]$CellWidth = 400,
    [int]$Spp = 64,
    [int]$MapSize = 256,
    [string]$Seeds = '42,7,1234,2026',
    # Reuse the measures (and their worlds) a run into the same -Out already made; only the sheets are made again.
    [switch]$SkipMeasures
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell leaves $PSScriptRoot empty in the parameters' defaults when another parameter is given.
if ($App -eq '') { $App = Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll' }
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null
if ($Worlds -eq '') { $Worlds = Join-Path $Out 'worlds' }
$views = Split-Path $PSScriptRoot -Parent
$golden = Join-Path $views 'mountains-golden.recipe.json'
$day = Join-Path $views 'mountains-day.recipe.json'
$seedList = $Seeds.Split(',')
# M1.11a's world from this build: its uplift (in proportion to the base terrain's height) and every rock the softest.
$m111a = 'UpliftOctaves=0;IncisionUplift=2;IncisionRate=0.03;HardErodibility=1;IncisionTalusDegrees=45;IncisionTalusHardDegrees=45;RockTalusDegrees=50;RockTalusHardDegrees=50'

function Run([string]$dll, [string[]]$arguments) {
    & dotnet $dll @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Vista.App $($arguments -join ' ') failed." }
}

function Reduce([string]$from, [string]$to, [int]$size) {
    $source = New-Object System.Drawing.Bitmap $from
    $target = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($target)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($source, 0, 0, $size, $size)
    $g.Dispose(); $source.Dispose()
    $target.Save($to, [System.Drawing.Imaging.ImageFormat]::Png)
    $target.Dispose()
}

# The measures, M1.11a's world and this build's. Each stores its worlds in its own cache, so the renders below read them.
if (-not $SkipMeasures) {
    $env:VISTA_WORLD_CACHE = Join-Path $Worlds 'm111a'
    & dotnet $App --erosion-measure --recipe $golden --seeds $Seeds --set $m111a --out (Join-Path $Out 'measure-m111a') *> (Join-Path $Out 'measure-m111a.log')
    if ($LASTEXITCODE -ne 0) { throw 'The M1.11a measure failed.' }
    $env:VISTA_WORLD_CACHE = Join-Path $Worlds 'm111d'
    & dotnet $App --erosion-measure --recipe $golden --seeds $Seeds --out (Join-Path $Out 'measure-m111d') *> (Join-Path $Out 'measure-m111d.log')
    if ($LASTEXITCODE -ne 0) { throw 'The M1.11d measure failed.' }
}

# The maps sheet: each seed's uplift map and hardness map.
$tiles = @(); $labels = @()
New-Item -ItemType Directory -Force (Join-Path $Out 'maps') | Out-Null
foreach ($seed in $seedList) {
    # Labels split at commas: none in them.
    foreach ($map in @(@('uplift', 'uplift map (white: the most)'), @('hardness', 'hardness (rust soft · blue-grey hard)'))) {
        $cell = Join-Path $Out "maps\$($map[0])-$seed.png"
        Reduce (Join-Path $Out "measure-m111d\seed$seed.$($map[0]).png") $cell $MapSize
        $tiles += $cell
        $labels += "seed $seed · $($map[1])"
    }
}
Run $App (@('--sheet-from') + $tiles + @('--columns', '4', '--labels', ($labels -join ','),
    '--title', 'M1.11d maps · the uplift map on the incision grid and the rock''s hardness at the eroded surface · north up',
    '--out', (Join-Path $Out 'maps-sheet.png')))

if ($Before -ne '') {
    # The four seeds from the aerial camera, then the seed-42 views: main left, this build right. Each build keeps its own worlds;
    # main's are M1.11a's. The default and down-slope rows compare main's cameras on main's world with the re-placed ones here.
    $sun = @('--recipe', $day, '--sun-elevation', '12', '--width', $CellWidth, '--spp', $Spp)
    $aerial = Join-Path $views 'aerial.camera.json'
    $shots = @()
    foreach ($seed in $seedList) { $shots += , @("seed $seed · aerial", $aerial, $aerial, $seed) }
    foreach ($camera in @('atmosphere\range.camera.json', 'aerial\layered-ridges.camera.json')) {
        $name = [System.IO.Path]::GetFileNameWithoutExtension([System.IO.Path]::GetFileNameWithoutExtension($camera))
        $shots += , @("seed 42 · $name", (Join-Path $views $camera), (Join-Path $views $camera), '42')
    }
    foreach ($camera in @('default.camera.json', 'down-slope.camera.json')) {
        $name = [System.IO.Path]::GetFileNameWithoutExtension([System.IO.Path]::GetFileNameWithoutExtension($camera))
        $old = Join-Path $Out "main-$camera"
        & git -C $views show "$($BeforeRef):RayTracer/vista-views/$camera" | Set-Content -Encoding ascii $old
        $shots += , @("seed 42 · $name", $old, (Join-Path $views $camera), '42')
    }
    $seedTiles = @(); $seedLabels = @(); $viewTiles = @(); $viewLabels = @()
    $k = 0
    New-Item -ItemType Directory -Force (Join-Path $Out 'renders') | Out-Null
    foreach ($shot in $shots) {
        $k++
        $pair = @()
        foreach ($build in @(@('main', $Before, 'M1.11a (main)', 1), @('m111d', $App, 'M1.11d', 2))) {
            $env:VISTA_WORLD_CACHE = Join-Path $Worlds $build[0]
            $png = Join-Path $Out "renders\$($build[0])-$k.png"
            Run $build[1] (@('--preview') + $sun + @('--camera', $shot[$build[3]], '--seed', $shot[3], '--out', $png))
            $pair += , @($png, "$($shot[0]) · $($build[2])")
        }
        if ($k -le $seedList.Count) {
            $seedTiles += $pair | ForEach-Object { $_[0] }; $seedLabels += $pair | ForEach-Object { $_[1] }
        } else {
            $viewTiles += $pair | ForEach-Object { $_[0] }; $viewLabels += $pair | ForEach-Object { $_[1] }
        }
    }
    Run $App (@('--sheet-from') + $seedTiles + @('--columns', '2', '--labels', ($seedLabels -join ','),
        '--title', 'M1.11d: four seeds from the aerial camera, the sun at 12 degrees · main left, uplift map and hardness right', '--out', (Join-Path $Out 'seeds-sheet.png')))
    Run $App (@('--sheet-from') + $viewTiles + @('--columns', '2', '--labels', ($viewLabels -join ','),
        '--title', 'M1.11d: the seed-42 views, the sun at 12 degrees · main left, this build right (default and down-slope re-placed)', '--out', (Join-Path $Out 'views-sheet.png')))
}
$env:VISTA_WORLD_CACHE = ''
Write-Output "Measures: $(Join-Path $Out 'measure-m111a.log'), $(Join-Path $Out 'measure-m111d.log'); sheets in $Out"
