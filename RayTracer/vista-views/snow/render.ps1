# The M1.13 checks (vista-plan section 10, M1.13) on the local GPU, as sheets of at most -CellWidth pixels a cell:
#  - measures: --erosion-measure on each seed, M1.12c's world (this build with the glaciers off, the same bits as main) and M1.13's,
#    one world at a time in -Worlds: the footing, the relief and facets, the drainage, the glaciers, and the materials' shares at
#    Zermatt in the recipe's July (snow, firn and ice among them); the lines that matter go to snow.log;
#  - valleys: --valley-measure on each seed: the troughs where glaciers run, carved against uncarved, and valleys.png of their
#    sections (seeds 42 and 7);
#  - maps.png: each seed's materials from above at Zermatt in July, M1.12c left and M1.13 right (from the measures);
#  - with -Before (a main build's Vista.App.dll), views.png: the seed-42 views in July, main left and this build right;
#  - seasons.png: the default view at noon on the 15th of January, April, July and October, the snow slider auto (the season's
#    snowline from Zermatt's own precipitation), and the aerial view on the same days;
#  - alpenglow.png: M1.10a's alpenglow sweep (the range view 3.4 km up, looking east at the summits a July evening's sun lights),
#    the sun setting from 4 degrees above the horizon to 3 below, the default quadrature (the blend);
#  - redend.png: the same view with the sun at the horizon and 0.5 and 1 degrees down, M1.12a's equal bins against the blend (the
#    red end: the last summits lit, in the reddest light that reaches this world's ground);
#  - slopes: the ledger's near views and the aerial view in April with the material and normal outputs; each sidecar's share of
#    white ground by slope (bare steep faces) to snow.log, and slopes.png of the April views with their material ids;
#  - with -Before and -Photo (the user's own photo over the Alps, 11 April 2025), photo.png: the photo beside the over-forest view in
#    its light, main and this build (snow on the open ground, the forest dark), and this build with the snowline at 2400 m (the
#    photo's ground lies lower than Zermatt's: snow on its summits alone);
#  - rr: --sampling-compare on the April default view (bright snow) with Russian roulette from bounces 1, 2, 3 and never;
#  - with -Before, cost: the four ledger views at 3840x2160, 64 spp, main and this build alternately, -Rounds rounds.
# Build first: dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-snow'),
    [string]$App = '',
    [string]$Before = '',
    [string]$Photo = '',
    [string]$Worlds = '',
    [int]$CellWidth = 400,
    [int]$Spp = 64,
    [int]$MapSize = 200,
    [int]$Rounds = 3,
    [string]$Seeds = '42,7,1234,2026',
    [string]$Sheets = 'measures,valleys,maps,views,seasons,alpenglow,redend,slopes,photo,rr,cost'
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell leaves $PSScriptRoot empty in the parameters' defaults when another parameter is given.
if ($App -eq '') { $App = Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll' }
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null
if ($Worlds -eq '') { $Worlds = Join-Path $Out 'worlds' }
$views = Split-Path $PSScriptRoot -Parent
# Main's own cameras (M1.13 re-placed the default and down-slope views on its ground): the vista-views beside the -Before build.
$beforeViews = $views
if ($Before -ne '') {
    $root = $Before
    for ($k = 0; $k -lt 5; $k++) { $root = Split-Path $root -Parent }
    if (Test-Path (Join-Path $root 'vista-views')) { $beforeViews = Join-Path $root 'vista-views' }
}
$golden = Join-Path $views 'mountains-golden.recipe.json'
$day = Join-Path $views 'mountains-day.recipe.json'
$april = Join-Path $PSScriptRoot 'mountains-april.recipe.json'
$default = Join-Path $views 'default.camera.json'
$evening = Join-Path $views 'atmosphere\zermatt-july.recipe.json'
$range = Join-Path $views 'atmosphere\range.camera.json'
$overForest = Join-Path $views 'forest\over-forest.camera.json'
$seedList = $Seeds.Split(',')
$make = $Sheets.Split(',')
$log = Join-Path $Out 'snow.log'
# M1.12c's world from this build: none of M1.13's glacial carving or glaciers.
$unglaciated = 'GlacialSteps=0;GlacierElaMetres=0'

$ledger = @(
    @('sky', $day, 'sky.camera.json', @()),
    @('aerial', $day, 'aerial.camera.json', @()),
    @('default', $day, 'default.camera.json', @()),
    @('down-slope', $golden, 'down-slope.camera.json', @())
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

function Sheet([string[]]$tiles, [string[]]$labels, [int]$columns, [string]$title, [string]$name) {
    Run $App (@('--sheet-from') + $tiles + @('--columns', "$columns", '--labels', ($labels -join ','), '--title', $title, '--out', (Join-Path $Out $name)))
}

function Clear-Worlds {
    if (Test-Path $Worlds) { Get-ChildItem $Worlds -File | Remove-Item -Force }
}

# The measures: each seed, M1.12c's world then M1.13's, each world deleted after its measure.
if ($make -contains 'measures') {
    New-Item -ItemType Directory -Force $Worlds | Out-Null
    $env:VISTA_WORLD_CACHE = $Worlds
    foreach ($seed in $seedList) {
        foreach ($build in @(@('m112c', @('--set', $unglaciated)), @('m113', @()))) {
            $name = "$($build[0])-$seed"
            & dotnet $App (@('--erosion-measure', '--recipe', $golden, '--seeds', $seed) + $build[1] + @('--out', (Join-Path $Out $name))) *> (Join-Path $Out "$name.log")
            if ($LASTEXITCODE -ne 0) { throw "The measure $name failed." }
            Clear-Worlds
            foreach ($line in (Get-Content (Join-Path $Out "$name.log") | Where-Object { $_ -match 'hash |footing|eroded relief|eroded channels|glaciers:|^  at ' })) {
                Say "$($name): $($line.Trim())"
            }
        }
    }
    $env:VISTA_WORLD_CACHE = ''
}

# The valleys: the troughs where glaciers run, carved against uncarved, on the incision's grid.
if ($make -contains 'valleys') {
    & dotnet $App @('--valley-measure', '--seeds', $Seeds, '--out', (Join-Path $Out 'valleys')) *> (Join-Path $Out 'valleys.log')
    if ($LASTEXITCODE -ne 0) { throw 'The valley measure failed.' }
    foreach ($line in (Get-Content (Join-Path $Out 'valleys.log') | Where-Object { $_ -match '^seed|median' })) { Say "valleys: $($line.Trim())" }
    $tiles = @(); $labels = @()
    foreach ($seed in @('42', '7')) {
        $tiles += Join-Path $Out "valleys\valleys-$seed.png"
        $labels += "seed $seed · 24 sections across glaciers' valleys · grey M1.12c · blue M1.13 · 5 km across a cell · true proportions"
    }
    Sheet $tiles $labels 2 'M1.13 the troughs where glaciers run · carved (blue) against the rivers'' alone (grey) · the incision''s 64 m grid' 'valleys.png'
}

# The maps: each seed's materials from above at Zermatt in July, M1.12c's and M1.13's (from the measures).
if ($make -contains 'maps') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'maps') | Out-Null
    $tiles = @(); $labels = @()
    foreach ($seed in $seedList) {
        foreach ($build in @(@('m112c', 'M1.12c'), @('m113', 'M1.13'))) {
            $cell = Join-Path $Out "maps\$($build[0])-$seed.png"
            Reduce (Join-Path $Out "$($build[0])-$seed\seed$seed.materials.png") $cell $MapSize
            $tiles += $cell
            $labels += "seed $seed · $($build[1])"
        }
    }
    Sheet $tiles $labels 4 'M1.13 the materials from above at Zermatt on 15 July · white snow · cream firn · blue glacier ice · green forest and meadow · grey rock · north up' 'maps.png'
}

# The views in July: main left, this build right.
if ($Before -ne '' -and $make -contains 'views') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'views') | Out-Null
    $shots = @(
        @('aerial · noon', $day, 'aerial.camera.json', @()),
        @('range · afternoon', $day, 'atmosphere\range.camera.json', @('--sun-elevation', '25')),
        @('layered ridges · low sun', $day, 'aerial\layered-ridges.camera.json', @('--sun-elevation', '12')),
        @('default · golden hour', $golden, 'default.camera.json', @()),
        @('down the slope · golden hour', $golden, 'down-slope.camera.json', @())
    )
    $tiles = @(); $labels = @()
    foreach ($shot in $shots) {
        $slug = Slug $shot[0]
        foreach ($build in @(@('main', $Before, 'main (M1.12c)', $beforeViews), @('m113', $App, 'M1.13', $views))) {
            $png = Join-Path $Out "views\$($build[0])-$slug.png"
            Run $build[1] (@('--preview', '--recipe', $shot[1], '--seed', '42', '--camera', (Join-Path $build[3] $shot[2]), '--width', [Math]::Min($CellWidth, 320), '--spp', $Spp) + $shot[3] + @('--out', $png))
            $tiles += $png
            $labels += "$($shot[0]) · $($build[2])"
        }
    }
    Sheet $tiles $labels 2 'M1.13 the seed-42 views on 15 July · main left · M1.13 right (glaciers carved and laid, the season''s snow above 3040 m; the default and down-slope views re-placed, main''s at its own places)' 'views.png'
}

# The seasons: the snow slider auto, from Zermatt's own precipitation.
if ($make -contains 'seasons') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'seasons') | Out-Null
    $tiles = @(); $labels = @()
    foreach ($camera in @(@('default', $default), @('aerial', (Join-Path $views 'aerial.camera.json')))) {
        foreach ($date in @('2026-01-15', '2026-04-15', '2026-07-15', '2026-10-15')) {
            $png = Join-Path $Out "seasons\$($camera[0])-$date.png"
            Run $App @('--preview', '--recipe', $day, '--date', $date, '--seed', '42', '--camera', $camera[1], '--width', $CellWidth, '--spp', $Spp, '--out', $png)
            $snow = (Get-Content ([IO.Path]::ChangeExtension($png, '.json')) -Raw | ConvertFrom-Json).sliders.values.snow.value
            $tiles += $png
            $labels += "$($camera[0]) · $date noon · snow from $snow"
        }
    }
    Sheet $tiles $labels 4 'M1.13 the seasons at Zermatt from the defaults alone · the snowline from a degree-day snowpack on Zermatt''s own precipitation' 'seasons.png'
}

# Alpenglow: a July evening, the sun setting to 3 degrees below the horizon, the summits it lights last.
if ($make -contains 'alpenglow') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'alpenglow') | Out-Null
    $tiles = @(); $labels = @()
    foreach ($e in @('4', '2', '1', '0', '-0.5', '-1', '-1.5', '-1.8', '-2', '-3')) {
        $png = Join-Path $Out "alpenglow\sun$e.png"
        Run $App @('--preview', '--recipe', $evening, '--sun-elevation', $e, '--seed', '42', '--camera', $range, '--width', $CellWidth, '--spp', $Spp, '--out', $png)
        $tiles += $png
        $labels += "sun $e° · setting"
    }
    Sheet $tiles $labels 5 'M1.13 alpenglow · the range view 3.4 km up on a 15 July evening · no code knows about alpenglow: the light through the air' 'alpenglow.png'
}

# The red end: the same evening with the sun at the horizon and 0.5 and 1 degrees down (at 1 no summit in view is lit any more),
# M1.12a's equal bins against the blend.
if ($make -contains 'redend') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'redend') | Out-Null
    $tiles = @(); $labels = @()
    foreach ($e in @('0', '-0.5', '-1')) {
        foreach ($q in @('bins', 'blend')) {
            $png = Join-Path $Out "redend\$q$e.png"
            Run $App @('--preview', '--recipe', $evening, '--sun-elevation', $e, '--seed', '42', '--camera', $range, '--width', $CellWidth, '--spp', '200',
                '--quadrature', $q, '--out', $png)
            $tiles += $png
            $labels += "sun $e° · $q"
        }
    }
    Sheet $tiles $labels 2 'M1.13 the red end · the evening summits with M1.12a''s equal bins (left) and the blend (right) · 200 spp, every wavelength four times' 'redend.png'
}

# Bare steep faces: the April views with their material ids and normals.
if ($make -contains 'slopes') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'slopes') | Out-Null
    $tiles = @(); $labels = @()
    foreach ($camera in @(@('default', 'default.camera.json'), @('range', 'atmosphere\range.camera.json'), @('aerial', 'aerial.camera.json'), @('layered-ridges', 'aerial\layered-ridges.camera.json'))) {
        $png = Join-Path $Out "slopes\$($camera[0]).png"
        Run $App @('--preview', '--recipe', $april, '--seed', '42', '--camera', (Join-Path $views $camera[1]), '--width', $CellWidth, '--spp', $Spp, '--debug', 'material,normal', '--out', $png)
        $bands = (Get-Content ([IO.Path]::ChangeExtension($png, '.json')) -Raw | ConvertFrom-Json).materials.snowBySlope
        $text = ($bands | ForEach-Object { [string]::Format([cultureinfo]::InvariantCulture, '{0:0}-{1:0}°: {2:0.0}% white of {3} px', $_.fromDegrees, $_.toDegrees, 100 * $_.whiteShare, $_.pixels) }) -join '; '
        Say "slopes: $($camera[0]) in April: $text"
        $tiles += $png, ([IO.Path]::ChangeExtension($png, '.material.png'))
        $labels += "$($camera[0]) · 15 April noon", "$($camera[0]) · material ids"
    }
    Sheet $tiles $labels 4 'M1.13 bare steep faces · 15 April at Zermatt (snow from about 1780 m) · the beauty and its material ids' 'slopes.png'
}

# The user's photo over the Alps beside the over-forest view in its light.
if ($Before -ne '' -and $Photo -ne '' -and $make -contains 'photo') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'photo') | Out-Null
    $source = New-Object System.Drawing.Bitmap $Photo
    # The view out of the window, clear of its frame (the 1536x2048 phone photo 20250411_110438), at 16:9.
    $crop = New-Object System.Drawing.Rectangle 240, 720, 1060, 596
    $target = New-Object System.Drawing.Bitmap 400, 225
    $g = [System.Drawing.Graphics]::FromImage($target)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($source, (New-Object System.Drawing.Rectangle 0, 0, 400, 225), $crop, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose(); $source.Dispose()
    $photoCell = Join-Path $Out 'photo\photo.png'
    $target.Save($photoCell, [System.Drawing.Imaging.ImageFormat]::Png)
    $target.Dispose()
    $tiles = @($photoCell); $labels = @('the user''s photo · 11 April 2025 · 10 km up · a phone through a window')
    $light = @('--preview', '--recipe', $day, '--date', '2026-04-11', '--time', '10:15', '--seed', '42', '--camera', $overForest, '--width', '400', '--spp', $Spp)
    $shots = @(
        @('main', $Before, @(), 'main (M1.12c) · no snow'),
        @('m113', $App, @(), 'M1.13 · the season''s snow · glaciers'),
        @('m113-2400', $App, @('--snowline', '2400'), 'M1.13 · snow from 2400 m'))
    foreach ($shot in $shots) {
        $png = Join-Path $Out "photo\$($shot[0]).png"
        Run $shot[1] ($light + $shot[2] + @('--out', $png))
        $tiles += $png
        $labels += "seed 42 · 11 April 10:15 · $($shot[3])"
    }
    Sheet $tiles $labels 2 'M1.13 the snow and the forest against the user''s photo · Zermatt''s April snowline is 1780 m; the photo''s ground lies lower' 'photo.png'
}

# Russian roulette on bright snow: the April default view, from bounces 1, 2, 3 and never.
if ($make -contains 'rr') {
    & dotnet $App @('--sampling-compare', '--recipe', $april, '--camera', $default, '--spp', '128', '--replicates', '16', '--rr-starts', '1,2,3,64', '--baseline', 'mis',
        '--out', (Join-Path $Out 'rr')) *> (Join-Path $Out 'rr.log')
    if ($LASTEXITCODE -ne 0) { throw 'The roulette sweep failed.' }
    foreach ($line in (Get-Content (Join-Path $Out 'rr.log') | Where-Object { $_ -match 'roulette|rr|bounce' })) { Say "rr: $($line.Trim())" }
}

# The cost ledger: main and this build alternately, view by view.
if ($Before -ne '' -and $make -contains 'cost') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'cost') | Out-Null
    $ratios = @{}
    for ($round = 1; $round -le $Rounds; $round++) {
        foreach ($view in $ledger) {
            $slug = Slug $view[0]
            $ns = @{}
            foreach ($build in @(@('main', $Before, $beforeViews), @('m113', $App, $views))) {
                $png = Join-Path $Out "cost\$($build[0])-$slug.png"
                Run $build[1] (@('--preview', '--recipe', $view[1], '--seed', '42', '--camera', (Join-Path $build[2] $view[2]), '--width', '3840', '--spp', '64') + $view[3] + @('--out', $png))
                $stats = (Get-Content ([IO.Path]::ChangeExtension($png, '.json')) -Raw | ConvertFrom-Json).render.stats
                $ns[$build[0]] = $stats.traceSeconds * 1e9 / $stats.pixelSamples
                $ns["$($build[0])-rays"] = $stats.raysTraced / $stats.pixelSamples
                # Only the timing is kept: a 4K frame's images are over 100 MB.
                Remove-Item $png, ([IO.Path]::ChangeExtension($png, '.exr')) -ErrorAction SilentlyContinue
            }
            $ratio = $ns['m113'] / $ns['main']
            if (-not $ratios.ContainsKey($slug)) { $ratios[$slug] = @() }
            $ratios[$slug] += $ratio
            Say ([string]::Format([cultureinfo]::InvariantCulture, 'cost: round {0} {1,-11} main {2,6:0.00} ns ({3:0.00} rays)  M1.13 {4,6:0.00} ns ({5:0.00} rays)  ratio {6:0.000}',
                $round, $slug, $ns['main'], $ns['main-rays'], $ns['m113'], $ns['m113-rays'], $ratio))
        }
    }
    foreach ($view in $ledger) {
        $slug = Slug $view[0]
        $sorted = $ratios[$slug] | Sort-Object
        Say ([string]::Format([cultureinfo]::InvariantCulture, 'cost: median ratio {0,-11} {1:0.000} ({2:0.000}-{3:0.000})', $slug, $sorted[[int][math]::Floor($sorted.Count / 2)],
            $sorted[0], $sorted[-1]))
    }
}

Write-Output "Sheets in $Out ($log has the measures, the slopes, the roulette and the cost)"
