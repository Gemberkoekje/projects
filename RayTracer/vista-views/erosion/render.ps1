# The M1.7 erosion checks (vista-plan section 10, M1.7) on the local GPU: the HLSL passes against their C# reference and the
# erosion of four seeds on the world grid (GPU time, memory, top-down maps: --erosion-measure --parity); what the measurement
# views see on grids of 16.4, 32.8 and 65.5 km (--sight-lines, the grid's extent); a before/after sheet of the four seeds from
# the aerial camera (before = a main build when -Before names its Vista.App.dll, then this build un-eroded, then eroded); the
# seed-42 views un-eroded and eroded; and the seeds' erosion maps from dry runs (which read the worlds the renders cached).
# Worlds are cached in -Worlds (default: a folder under -Out, so the user's cache is untouched). Build first:
# dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-erosion'),
    [string]$App = (Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll'),
    [string]$Before = '',
    [string]$Worlds = '',
    [int]$Width = 640,
    [int]$Spp = 64,
    [string]$Seeds = '42,7,1234,2026'
)

New-Item -ItemType Directory -Force $Out | Out-Null
if ($Worlds -eq '') { $Worlds = Join-Path $Out 'worlds' }
$env:VISTA_WORLD_CACHE = $Worlds
$views = Join-Path $PSScriptRoot '..'
$golden = Join-Path $views 'mountains-golden.recipe.json'
$aerial = Join-Path $views 'aerial.camera.json'
$seedList = $Seeds.Split(',')

function Run([string]$app, [string[]]$arguments) {
    & dotnet $app @arguments
    if ($LASTEXITCODE -ne 0) { throw "Vista.App $($arguments -join ' ') failed." }
}

# Parity, time, memory and maps of the four seeds.
Run $App @('--erosion-measure', '--parity', '--recipe', $golden, '--seeds', $Seeds, '--out', (Join-Path $Out 'measure'))

# What the views see, by the grid's extent.
$cameras = @('aerial.camera.json', 'default.camera.json', 'down-slope.camera.json', 'aerial\layered-ridges.camera.json',
    'atmosphere\range.camera.json', 'atmosphere\toward-sun.camera.json') | ForEach-Object { Join-Path $views $_ }
Run $App @('--erosion-measure', '--recipe', $golden, '--sight-lines', ($cameras -join ','), '--sight-grids', '1025x16,1025x32,2049x32', '--seeds', '')

# Four seeds from the aerial camera: before (main), not eroded, eroded. Labels hold no commas: --labels is a comma-separated list.
$tiles = @()
$labels = @()
foreach ($seed in $seedList) {
    $common = @('--preview', '--recipe', $golden, '--camera', $aerial, '--seed', $seed, '--width', $Width, '--spp', $Spp)
    if ($Before -ne '') {
        Run $Before ($common + @('--out', (Join-Path $Out "seeds\main-$seed.png")))
        $tiles += Join-Path $Out "seeds\main-$seed.png"
        $labels += "seed $seed · M1.6 · 16.4 km at 16 m"
    }
    Run $App ($common + @('--erosion', 'off', '--out', (Join-Path $Out "seeds\off-$seed.png")))
    Run $App ($common + @('--out', (Join-Path $Out "seeds\on-$seed.png")))
    $tiles += @((Join-Path $Out "seeds\off-$seed.png"), (Join-Path $Out "seeds\on-$seed.png"))
    $labels += @("seed $seed · 32.8 km at 8 m · not eroded", "seed $seed · eroded")
}
$columns = if ($Before -ne '') { 3 } else { 2 }
Run $App (@('--sheet-from') + $tiles + @('--columns', $columns, '--labels', ($labels -join ','), '--title', 'M1.7 erosion: four seeds from the aerial camera, golden hour',
    '--out', (Join-Path $Out 'seeds-sheet.png')))

# The seed-42 views, not eroded and eroded.
$tiles = @()
$labels = @()
foreach ($camera in @('default.camera.json', 'down-slope.camera.json', 'aerial\layered-ridges.camera.json', 'atmosphere\range.camera.json')) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension([System.IO.Path]::GetFileNameWithoutExtension($camera))
    $common = @('--preview', '--recipe', $golden, '--camera', (Join-Path $views $camera), '--seed', '42', '--width', $Width, '--spp', $Spp)
    if ($Before -ne '') {
        Run $Before ($common + @('--out', (Join-Path $Out "views\main-$name.png")))
        $tiles += Join-Path $Out "views\main-$name.png"
        $labels += "$name · M1.6"
    }
    Run $App ($common + @('--erosion', 'off', '--out', (Join-Path $Out "views\off-$name.png")))
    Run $App ($common + @('--out', (Join-Path $Out "views\on-$name.png")))
    $tiles += @((Join-Path $Out "views\off-$name.png"), (Join-Path $Out "views\on-$name.png"))
    $labels += @("$name · not eroded", "$name · eroded")
}
Run $App (@('--sheet-from') + $tiles + @('--columns', $columns, '--labels', ($labels -join ','), '--title', 'M1.7 erosion: the seed-42 views, golden hour',
    '--out', (Join-Path $Out 'views-sheet.png')))

# The erosion maps of the seeds, from dry runs that read the cached worlds.
$tiles = @()
foreach ($seed in $seedList) {
    Run $App @('--preview', '--dry-run', '--recipe', $golden, '--camera', $aerial, '--seed', $seed, '--out', (Join-Path $Out "maps\seed$seed.png"))
    $tiles += Join-Path $Out "maps\seed$seed.erosion.png"
}
Run $App (@('--sheet-from') + $tiles + @('--labels', (($seedList | ForEach-Object { "seed $_" }) -join ','),
    '--title', 'M1.7 erosion maps · loose material tan · discharge blue · standing water dark blue · north up · the camera red', '--out', (Join-Path $Out 'maps-sheet.png')))
Write-Output "Sheets: $(Join-Path $Out 'seeds-sheet.png'), $(Join-Path $Out 'views-sheet.png'), $(Join-Path $Out 'maps-sheet.png')"
