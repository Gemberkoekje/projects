# The M1.12b checks (vista-plan section 10, M1.12b) on the local GPU, as sheets of at most -CellWidth pixels a cell:
#  - with -Before (a main build's Vista.App.dll), same: the four ledger views rendered by main and by this build's --forest off,
#    640 px at 16 spp; prints whether each PNG and EXR is byte-identical;
#  - mesh: every committed view's 4K mesh with the forest and without (dry runs), the triangles below and beyond the grid;
#  - maps: each seed's materials from above with the forest's share (the dry run's material map; forest dark green), at the place's
#    valley floor (Zermatt, 1608 m) and with the world -LowAltitude metres up (default 1000; vista-log/M1.12b.md says why);
#  - ids: each seed from the over-forest camera at -LowAltitude: the beauty and the material-id output;
#  - with -Before, views.png: the ledger and M1.12 views at the valley floor, main left and this build right;
#  - with -Before, low.png: the aerial, range, over-forest and layered-ridges views at -LowAltitude, main left and this build right;
#  - with -Photo (one of the user's own photos over the Alps, 11 April 2025), photo.png: the photo, cropped clear of the window and
#    downscaled (no metadata survives the re-encoding), beside the over-forest view in April light at -LowAltitude, forest off and on;
#  - canopy.png: the forest as rough diffuse ground against its canopy's kernels (--canopy-compare), the sun 25 degrees up, with the
#    ratio maps; the ratios go to forest.log;
#  - loupe.png: a 640x360 window of a 3840x2160 frame, a forested crest against the sky about 2 km off, forest off and on;
#  - with -Before, cost: the four ledger views at 3840x2160, 64 spp, main and this build alternately, view by view, -Rounds rounds;
#    prints each render's ns per pixel-sample and the median ratio per view (a shared GPU's load moves from minute to minute).
# Build first: dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-forest'),
    [string]$App = '',
    [string]$Before = '',
    [string]$Photo = '',
    [int]$CellWidth = 400,
    [int]$Spp = 64,
    [int]$MapSize = 200,
    [int]$LowAltitude = 1000,
    [int]$Rounds = 3,
    [string]$Seeds = '42,7,1234,2026',
    [string]$Sheets = 'same,mesh,maps,ids,views,low,photo,canopy,loupe,cost'
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell leaves $PSScriptRoot empty in the parameters' defaults when another parameter is given.
if ($App -eq '') { $App = Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll' }
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null
$views = Split-Path $PSScriptRoot -Parent
$golden = Join-Path $views 'mountains-golden.recipe.json'
$day = Join-Path $views 'mountains-day.recipe.json'
$aerial = Join-Path $views 'aerial.camera.json'
$overForest = Join-Path $PSScriptRoot 'over-forest.camera.json'
$low = @('--altitude', "$LowAltitude")
$seedList = $Seeds.Split(',')
$make = $Sheets.Split(',')
$log = Join-Path $Out 'forest.log'

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

function Percent([double]$share) { [string]::Format([cultureinfo]::InvariantCulture, '{0:0.0}%', 100.0 * $share) }

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

# main against this build's --forest off, byte for byte.
if ($Before -ne '' -and $make -contains 'same') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'same') | Out-Null
    foreach ($view in $ledger) {
        $slug = Slug $view[0]
        $common = @('--preview', '--recipe', $view[1], '--seed', '42', '--camera', (Join-Path $views $view[2]), '--width', '640', '--spp', '16') + $view[3]
        Run $Before ($common + @('--out', (Join-Path $Out "same\main-$slug.png")))
        Run $App ($common + @('--forest', 'off', '--out', (Join-Path $Out "same\off-$slug.png")))
        foreach ($ext in @('png', 'exr')) {
            $a = (Get-FileHash (Join-Path $Out "same\main-$slug.$ext")).Hash
            $b = (Get-FileHash (Join-Path $Out "same\off-$slug.$ext")).Hash
            $verdict = if ($a -eq $b) { 'byte-identical' } else { 'DIFFERENT' }
            Say "same: $($view[0]) $($ext): $verdict"
        }
    }
}

# Every committed view's 4K mesh, with the forest and without.
if ($make -contains 'mesh') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'mesh') | Out-Null
    $meshViews = @('sky', 'default', 'down-slope', 'aerial', 'atmosphere\range', 'aerial\layered-ridges', 'materials\scree-cone', 'materials\rock-face',
        'materials\meadow', 'forest\over-forest', 'forest\forest-crest')
    foreach ($view in $meshViews) {
        foreach ($altitude in @(@('valley floor', @()), @("$LowAltitude m", $low))) {
            foreach ($forest in @('off', 'on')) {
                $png = Join-Path $Out "mesh\$(Slug $view)-$(Slug $altitude[0])-$forest.png"
                $line = & dotnet $App (@('--preview', '--dry-run', '--recipe', $golden, '--seed', '42', '--camera', (Join-Path $views "$view.camera.json"),
                    '--width', '3840', '--forest', $forest) + $altitude[1] + @('--out', $png)) | Where-Object { $_ -match '^mesh:' }
                Say "mesh: $view, $($altitude[0]), forest $($forest): $($line -replace '^mesh: view-dependent at 1 px, ', '')"
            }
        }
    }
}

# The maps: each seed's dry run writes its materials from above and the shares in its sidecar, at the valley floor and lower.
if ($make -contains 'maps') {
    $tiles = @(); $labels = @()
    New-Item -ItemType Directory -Force (Join-Path $Out 'maps') | Out-Null
    foreach ($seed in $seedList) {
        foreach ($altitude in @(@('valley floor 1608 m', @()), @("base $LowAltitude m", $low))) {
            $slug = "seed$seed-$(Slug $altitude[0])"
            $png = Join-Path $Out "maps\$slug.png"
            Run $App (@('--preview', '--dry-run', '--recipe', $golden, '--seed', $seed, '--camera', $aerial) + $altitude[1] + @('--out', $png))
            $shares = (Get-Content (Join-Path $Out "maps\$slug.json") -Raw | ConvertFrom-Json).materials.shares
            $cell = Join-Path $Out "maps\cell-$slug.png"
            Reduce (Join-Path $Out "maps\$slug.materials.png") $cell $MapSize
            $tiles += $cell
            $labels += "seed $seed · $($altitude[0]) · forest $(Percent $shares.forest)"
            Say "maps: seed $seed, $($altitude[0]): forest $(Percent $shares.forest), grass $(Percent $shares.grass), soil $(Percent $shares.soil), scree $(Percent $shares.scree), rock $(Percent ($shares.rockHard + $shares.rockSoft))"
        }
    }
    Sheet $tiles $labels 2 "M1.12b from above · forest dark green · left valley floor · right base $LowAltitude m" 'maps.png'
}

# The ids: each seed from the over-forest camera with the world lower, the beauty and its material ids.
if ($make -contains 'ids') {
    $tiles = @(); $labels = @()
    New-Item -ItemType Directory -Force (Join-Path $Out 'ids') | Out-Null
    foreach ($seed in $seedList) {
        $png = Join-Path $Out "ids\seed$seed.png"
        Run $App (@('--preview', '--recipe', $day, '--sun-elevation', '35', '--seed', $seed, '--camera', $overForest, '--width', '320', '--spp', $Spp,
            '--debug', 'material') + $low + @('--out', $png))
        $tiles += @($png, (Join-Path $Out "ids\seed$seed.material.png"))
        $labels += @("seed $seed · beauty", "seed $seed · material")
    }
    Sheet $tiles $labels 2 "M1.12b four seeds from the over-forest camera · base $LowAltitude m · the sun at 35 degrees · ids in the legend colours of maps.png" 'ids.png'
}

# Before and after: main left, this build right.
function Before-After($shots, [string]$name, [string]$title) {
    $tiles = @(); $labels = @()
    New-Item -ItemType Directory -Force (Join-Path $Out $name) | Out-Null
    $k = 0
    foreach ($shot in $shots) {
        $k++
        foreach ($build in @(@('main', $Before, 'main (M1.12a)'), @('m112b', $App, 'M1.12b'))) {
            $png = Join-Path $Out "$name\$($build[0])-$k.png"
            Run $build[1] (@('--preview', '--recipe', $shot[1], '--seed', '42', '--camera', (Join-Path $views $shot[2]), '--width', $CellWidth, '--spp', $Spp) + $shot[3] + @('--out', $png))
            $tiles += $png
            $labels += "$($shot[0]) · $($build[2])"
        }
    }
    Sheet $tiles $labels 2 $title "$name.png"
}

if ($Before -ne '' -and $make -contains 'views') {
    Before-After @(
        @('aerial · day', $day, 'aerial.camera.json', @()),
        @('default · day', $day, 'default.camera.json', @()),
        @('down the slope · golden hour', $golden, 'down-slope.camera.json', @()),
        @('range · afternoon', $day, 'atmosphere\range.camera.json', @('--sun-elevation', '25'))
    ) 'views' 'M1.12b the seed-42 views at Zermatt''s valley floor · main left · forest below the tree line right (little of this world lies below it)'
}

if ($Before -ne '' -and $make -contains 'low') {
    Before-After @(
        @("aerial · day · base $LowAltitude m", $day, 'aerial.camera.json', $low),
        @("range · afternoon · base $LowAltitude m", $day, 'atmosphere\range.camera.json', @('--sun-elevation', '25') + $low),
        @("over the forest · afternoon · base $LowAltitude m", $day, 'forest\over-forest.camera.json', @('--sun-elevation', '35') + $low),
        @("layered ridges · day · base $LowAltitude m", $day, 'aerial\layered-ridges.camera.json', $low)
    ) 'low' "M1.12b the seed-42 views with the world $LowAltitude m up (a quarter of it below the tree line) · main left · forest right"
}

# The user's photo over the Alps beside the over-forest view in April light, forest off and on.
if ($Photo -ne '' -and $make -contains 'photo') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'photo') | Out-Null
    $source = New-Object System.Drawing.Bitmap $Photo
    # The view out of the window, clear of its frame (the 1536x2048 phone photo 20250411_110438), at 16:9.
    $rect = New-Object System.Drawing.Rectangle 240, 720, 1060, 596
    $target = New-Object System.Drawing.Bitmap 512, 288
    $g = [System.Drawing.Graphics]::FromImage($target)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($source, (New-Object System.Drawing.Rectangle 0, 0, 512, 288), $rect, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose(); $source.Dispose()
    $crop = Join-Path $Out 'photo\photo.png'
    $target.Save($crop, [System.Drawing.Imaging.ImageFormat]::Png)
    $target.Dispose()
    $tiles = @($crop); $labels = @('the user''s photo · 11 April 2025 · 10 km up · a phone through a window')
    foreach ($forest in @('off', 'on')) {
        $png = Join-Path $Out "photo\forest-$forest.png"
        Run $App (@('--preview', '--recipe', $day, '--date', '2026-04-11', '--time', '10:15', '--seed', '42', '--camera', $overForest, '--width', '512', '--spp', $Spp,
            '--forest', $forest) + $low + @('--out', $png))
        $tiles += $png
        $labels += "seed 42 · 11 April 10:15 · base $LowAltitude m · forest $forest · no snow (M1.13)"
    }
    Sheet $tiles $labels 1 'M1.12b forest against open ground, not the colours' 'photo.png'
}

# The forest as rough diffuse ground against its canopy's kernels, the sun 25 degrees up.
if ($make -contains 'canopy') {
    $cameras = @((Join-Path $PSScriptRoot 'toward-sun.camera.json'), (Join-Path $PSScriptRoot 'away-from-sun.camera.json'), $overForest) -join ','
    $lines = & dotnet $App --canopy-compare --recipe $day --cameras $cameras --width 320 --spp 128 --sun-elevation 25 --altitude $LowAltitude --out (Join-Path $Out 'canopy')
    if ($LASTEXITCODE -ne 0) { throw 'The canopy comparison failed.' }
    $lines | ForEach-Object { Say "canopy: $_" }
    Copy-Item (Join-Path $Out 'canopy\canopy.png') (Join-Path $Out 'canopy.png') -Force
}

# The loupe: a forested crest against the sky, about 2 km off, in a 3840x2160 frame (a crop of it), mid-morning.
if ($make -contains 'loupe') {
    $tiles = @(); $labels = @()
    New-Item -ItemType Directory -Force (Join-Path $Out 'loupe') | Out-Null
    foreach ($forest in @('off', 'on')) {
        $png = Join-Path $Out "loupe\crest-$forest.png"
        Run $App (@('--preview', '--recipe', $day, '--time', '10:30', '--seed', '42', '--camera', (Join-Path $PSScriptRoot 'forest-crest.camera.json'), '--width', '3840',
            '--loupe', '2300,520,640,360', '--spp', $Spp, '--forest', $forest) + $low + @('--out', $png))
        $tiles += $png
        $labels += "a crest about 2 km off · 3840x2160 window 2300 520 640 360 · 10:30 · base $LowAltitude m · forest $forest"
    }
    Sheet $tiles $labels 1 'M1.12b a forest edge on a ridge at 4K · the crowns bump the skyline · close up still not trees (M5)' 'loupe.png'
}

# The cost ledger: main and this build alternately, view by view.
if ($Before -ne '' -and $make -contains 'cost') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'cost') | Out-Null
    $ratios = @{}
    for ($round = 1; $round -le $Rounds; $round++) {
        foreach ($view in $ledger) {
            $slug = Slug $view[0]
            $ns = @{}
            foreach ($build in @(@('main', $Before), @('m112b', $App))) {
                $png = Join-Path $Out "cost\$($build[0])-$slug.png"
                Run $build[1] (@('--preview', '--recipe', $view[1], '--seed', '42', '--camera', (Join-Path $views $view[2]), '--width', '3840', '--spp', '64') + $view[3] + @('--out', $png))
                $stats = (Get-Content ([IO.Path]::ChangeExtension($png, '.json')) -Raw | ConvertFrom-Json).render.stats
                $ns[$build[0]] = $stats.traceSeconds * 1e9 / $stats.pixelSamples
                $ns["$($build[0])-rays"] = $stats.raysTraced / $stats.pixelSamples
            }
            $ratio = $ns['m112b'] / $ns['main']
            if (-not $ratios.ContainsKey($slug)) { $ratios[$slug] = @() }
            $ratios[$slug] += $ratio
            Say ([string]::Format([cultureinfo]::InvariantCulture, 'cost: round {0} {1,-11} main {2,6:0.00} ns ({3:0.00} rays)  M1.12b {4,6:0.00} ns ({5:0.00} rays)  ratio {6:0.000}',
                $round, $slug, $ns['main'], $ns['main-rays'], $ns['m112b'], $ns['m112b-rays'], $ratio))
        }
    }
    foreach ($view in $ledger) {
        $slug = Slug $view[0]
        $sorted = $ratios[$slug] | Sort-Object
        Say ([string]::Format([cultureinfo]::InvariantCulture, 'cost: median ratio {0,-11} {1:0.000} ({2:0.000}-{3:0.000})', $slug, $sorted[[int][math]::Floor($sorted.Count / 2)],
            $sorted[0], $sorted[-1]))
    }
}

Write-Output "Sheets in $Out ($log has the checks, the meshes, the shares, the canopy's ratios and the cost)"
