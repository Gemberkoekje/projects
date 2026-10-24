# The M1.12c checks (vista-plan section 10, M1.12c) on the local GPU, as sheets of at most -CellWidth pixels a cell:
#  - measures: --erosion-measure on each seed, M1.12b's rock (this build with M1.12c's strata off, the same bits as main) and
#    M1.12c's, one seed at a time: the footing, the contour-line measure (the beds' bands and the ledges), the relief and facets, the
#    drainage, and the materials' shares at the anchored altitude; the logs go to -Out, the lines that matter to strata.log;
#  - maps.png: each seed's rock hardness at the eroded surface from above, M1.12b left and M1.12c right (from the measures);
#  - choice.png: the over-forest view in April light with the world's base at -LowAltitude, for the strata variants the defaults
#    were chosen from (main's M1.12b first); each variant's world is eroded into -Worlds and deleted after;
#  - with -Before (a main build's Vista.App.dll), views.png: the seed-42 views at each build's own altitude (main's height 0 at
#    Zermatt's valley floor, this build's lowest valleys there), main left and this build right;
#  - with -Before and -Photo (one of the user's own photos over the Alps, 11 April 2025), photo.png: the photo, cropped clear of the
#    window and downscaled (no metadata survives the re-encoding), above the over-forest view in April light: main at its own
#    altitude, main with its base at -LowAltitude (M1.12b's photo sheet), and this build at its own (the anchored) altitude;
#  - with -Before, cost: the four ledger views at 3840x2160, 64 spp, main and this build alternately, view by view, -Rounds rounds;
#    prints each render's ns per pixel-sample and the median ratio per view (a shared GPU's load moves from minute to minute).
# The views and the cost read each build's seed-42 world from the default cache (%LOCALAPPDATA%\Vista\worlds), eroding it there
# if it is missing. A world is 285 MB: the measures and the choice keep at most one in -Worlds at a time.
# Build first: dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-strata'),
    [string]$App = '',
    [string]$Before = '',
    [string]$Photo = '',
    [string]$Worlds = '',
    [int]$CellWidth = 400,
    [int]$Spp = 64,
    [int]$MapSize = 200,
    [int]$LowAltitude = 1000,
    [int]$Rounds = 3,
    [string]$Seeds = '42,7,1234,2026',
    [string]$Sheets = 'measures,maps,choice,views,photo,cost'
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
$overForest = Join-Path $views 'forest\over-forest.camera.json'
$seedList = $Seeds.Split(',')
$make = $Sheets.Split(',')
$log = Join-Path $Out 'strata.log'
# M1.12b's rock from this build: none of M1.12c's regional dip, folds, formations or massive regions.
$even = 'StrataRegionalDipDegrees=0;StrataFoldMetres=0;StrataFormationMetres=0;StrataMassiveShare=0'

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

# Empties -Worlds: the disk this was built on had room for a world or two.
function Clear-Worlds {
    if (Test-Path $Worlds) { Get-ChildItem $Worlds -File | Remove-Item -Force }
}

# The measures: each seed, M1.12b's rock then M1.12c's, each world deleted after its measure.
if ($make -contains 'measures') {
    New-Item -ItemType Directory -Force $Worlds | Out-Null
    $env:VISTA_WORLD_CACHE = $Worlds
    foreach ($seed in $seedList) {
        foreach ($build in @(@('m112b', @('--set', $even)), @('m112c', @()))) {
            $name = "$($build[0])-$seed"
            & dotnet $App (@('--erosion-measure', '--recipe', $golden, '--seeds', $seed) + $build[1] + @('--out', (Join-Path $Out $name))) *> (Join-Path $Out "$name.log")
            if ($LASTEXITCODE -ne 0) { throw "The measure $name failed." }
            Clear-Worlds
            foreach ($line in (Get-Content (Join-Path $Out "$name.log") | Where-Object { $_ -match 'hash |footing|bands at|ledges at 8|eroded relief|eroded channels|hardness at|^  at ' })) {
                Say "$($name): $($line.Trim())"
            }
        }
    }
    $env:VISTA_WORLD_CACHE = ''
}

# The maps: each seed's rock hardness at its surface, M1.12b's and M1.12c's (from the measures).
if ($make -contains 'maps') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'maps') | Out-Null
    $tiles = @(); $labels = @()
    foreach ($seed in $seedList) {
        foreach ($build in @(@('m112b', 'M1.12b'), @('m112c', 'M1.12c'))) {
            $cell = Join-Path $Out "maps\$($build[0])-$seed.png"
            Reduce (Join-Path $Out "$($build[0])-$seed\seed$seed.hardness.png") $cell $MapSize
            $tiles += $cell
            $labels += "seed $seed · $($build[1])"
        }
    }
    Sheet $tiles $labels 4 'M1.12c the rock''s hardness at the surface · rust soft · blue-grey hard · M1.12b left · M1.12c right · north up' 'maps.png'
}

# The choice: the over-forest view in April light at -LowAltitude, for each variant of the strata.
if ($make -contains 'choice') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'choice'), $Worlds | Out-Null
    $variants = @(
        @('main · M1.12b · level beds', $Before, ''),
        @('regional dip 20° · folds 300 m', $App, 'StrataRegionalDipDegrees=20;StrataFoldMetres=300;StrataMassiveShare=0'),
        @('regional dip 40° · folds 600 m · no massive rock', $App, 'StrataMassiveShare=0'),
        @('shipped · dip 40° · folds 600 m · 30% massive', $App, ''),
        @('regional dip 50° · folds 900 m at 4 km', $App, 'StrataRegionalDipDegrees=50;StrataFoldMetres=900;StrataFoldWavelengthMetres=4000'),
        @('half massive', $App, 'StrataMassiveShare=0.5')
    )
    $tiles = @(); $labels = @()
    $k = 0
    foreach ($variant in $variants) {
        $k++
        if ($variant[1] -eq '') { continue }
        $env:VISTA_WORLD_CACHE = $Worlds
        $env:VISTA_EROSION_SETTINGS = $variant[2]
        $png = Join-Path $Out "choice\variant-$k.png"
        Run $variant[1] @('--preview', '--recipe', $day, '--date', '2026-04-11', '--time', '10:15', '--seed', '42', '--camera', $overForest, '--width', $CellWidth,
            '--spp', $Spp, '--altitude', "$LowAltitude", '--out', $png)
        $env:VISTA_EROSION_SETTINGS = ''
        Clear-Worlds
        $tiles += $png
        $labels += $variant[0]
    }
    $env:VISTA_WORLD_CACHE = ''
    Sheet $tiles $labels 2 "M1.12c the strata the defaults were chosen from · the over-forest view · 11 April 10:15 · base $LowAltitude m" 'choice.png'
}

# The views at each build's own altitude: main left, this build right.
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
        foreach ($build in @(@('main', $Before, 'main (M1.12b)'), @('m112c', $App, 'M1.12c'))) {
            $png = Join-Path $Out "views\$($build[0])-$slug.png"
            # Ten cells: 320 px keeps the sheet under 1 MB (at 400 px it was 1.3 MB).
            Run $build[1] (@('--preview', '--recipe', $shot[1], '--seed', '42', '--camera', (Join-Path $views $shot[2]), '--width', [Math]::Min($CellWidth, 320), '--spp', $Spp) + $shot[3] + @('--out', $png))
            $tiles += $png
            $labels += "$($shot[0]) · $($build[2])"
        }
    }
    Sheet $tiles $labels 2 'M1.12c the seed-42 views at each build''s own altitude · main left (height 0 at the valley floor) · M1.12c right (the lowest valleys there; the default and down-slope views re-placed)' 'views.png'
}

# The user's photo over the Alps beside the over-forest view in April light.
if ($Before -ne '' -and $Photo -ne '' -and $make -contains 'photo') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'photo') | Out-Null
    $source = New-Object System.Drawing.Bitmap $Photo
    # The view out of the window, clear of its frame (the 1536x2048 phone photo 20250411_110438), at 16:9.
    $crop = New-Object System.Drawing.Rectangle 240, 720, 1060, 596
    $target = New-Object System.Drawing.Bitmap 512, 288
    $g = [System.Drawing.Graphics]::FromImage($target)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($source, (New-Object System.Drawing.Rectangle 0, 0, 512, 288), $crop, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose(); $source.Dispose()
    $photoCell = Join-Path $Out 'photo\photo.png'
    $target.Save($photoCell, [System.Drawing.Imaging.ImageFormat]::Png)
    $target.Dispose()
    $tiles = @($photoCell); $labels = @('the user''s photo · 11 April 2025 · 10 km up · a phone through a window')
    $april = @('--preview', '--recipe', $day, '--date', '2026-04-11', '--time', '10:15', '--seed', '42', '--camera', $overForest, '--width', '512', '--spp', $Spp)
    foreach ($shot in @(
            @('main-own', $Before, @(), 'main (M1.12b) · height 0 at the valley floor'),
            @('main-low', $Before, @('--altitude', "$LowAltitude"), "main (M1.12b) · base $LowAltitude m (its photo sheet)"),
            @('m112c', $App, @(), 'M1.12c · the lowest valleys at the valley floor · folded strata'))) {
        $png = Join-Path $Out "photo\$($shot[0]).png"
        Run $shot[1] ($april + $shot[2] + @('--out', $png))
        $tiles += $png
        $labels += "seed 42 · 11 April 10:15 · $($shot[3]) · no snow (M1.13)"
    }
    Sheet $tiles $labels 1 'M1.12c the beds and the footing against the user''s photo' 'photo.png'
}

# The cost ledger: main and this build alternately, view by view.
if ($Before -ne '' -and $make -contains 'cost') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'cost') | Out-Null
    $ratios = @{}
    for ($round = 1; $round -le $Rounds; $round++) {
        foreach ($view in $ledger) {
            $slug = Slug $view[0]
            $ns = @{}
            foreach ($build in @(@('main', $Before), @('m112c', $App))) {
                $png = Join-Path $Out "cost\$($build[0])-$slug.png"
                Run $build[1] (@('--preview', '--recipe', $view[1], '--seed', '42', '--camera', (Join-Path $views $view[2]), '--width', '3840', '--spp', '64') + $view[3] + @('--out', $png))
                $stats = (Get-Content ([IO.Path]::ChangeExtension($png, '.json')) -Raw | ConvertFrom-Json).render.stats
                $ns[$build[0]] = $stats.traceSeconds * 1e9 / $stats.pixelSamples
                $ns["$($build[0])-rays"] = $stats.raysTraced / $stats.pixelSamples
                # Only the timing is kept: a 4K frame's images are over 100 MB.
                Remove-Item $png, ([IO.Path]::ChangeExtension($png, '.exr')) -ErrorAction SilentlyContinue
            }
            $ratio = $ns['m112c'] / $ns['main']
            if (-not $ratios.ContainsKey($slug)) { $ratios[$slug] = @() }
            $ratios[$slug] += $ratio
            Say ([string]::Format([cultureinfo]::InvariantCulture, 'cost: round {0} {1,-11} main {2,6:0.00} ns ({3:0.00} rays)  M1.12c {4,6:0.00} ns ({5:0.00} rays)  ratio {6:0.000}',
                $round, $slug, $ns['main'], $ns['main-rays'], $ns['m112c'], $ns['m112c-rays'], $ratio))
        }
    }
    foreach ($view in $ledger) {
        $slug = Slug $view[0]
        $sorted = $ratios[$slug] | Sort-Object
        Say ([string]::Format([cultureinfo]::InvariantCulture, 'cost: median ratio {0,-11} {1:0.000} ({2:0.000}-{3:0.000})', $slug, $sorted[[int][math]::Floor($sorted.Count / 2)],
            $sorted[0], $sorted[-1]))
    }
}

Write-Output "Sheets in $Out ($log has the measures and the cost)"
