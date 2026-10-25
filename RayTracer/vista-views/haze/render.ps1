# The M1.13a checks (vista-plan section 10, M1.13a) on the local GPU, as sheets of at most -CellWidth pixels a cell:
#  - check: --atmosphere-check --segments with and without a valley haze (its lid 450 m above the ground sphere, 0.1/km at 550 nm,
#    and a strong one of 0.5/km): the LUT air along segments of 1, 4 and 16 km against the reference, up from 2 m (inside the layer)
#    and down from 1.7 km (through it), the sun at +30, +5, -2 and -6 degrees; the strong one with SINGLE_SCATTERING_ONLY too;
#    lines to haze.log;
#  - morning.png: the two shoulder views over the valleys (misty-ridges, across-valley) on 15 October at Zermatt from dawn to noon,
#    the valley haze auto: the night's pool at its deepest at dawn, sinking and thinning as the sun breaks it up;
#  - winter.png: the misty ridges at 10:00, 12:00 and 14:00 on 15 January (the valley floor under snow) and 15 October;
#  - strengths.png: the shoulder views and the aerial view at 08:00 on 15 October with the valley haze off, auto and strong (6 km
#    under the lid);
#  - places.png: the misty ridges in autumn at six places, the sun 6 degrees up and rising: the pool from each place's own range;
#  - with -Photo (the user's own photo over the Alps, 11 April 2025, 11:08), photo.png: the photo beside the aerial view with a
#    strong layer and a view from the photo's height (haze/ten-km.camera.json), auto (no pool by then) and strong, in its light,
#    judged on how the band sits rather than on the phone's colours;
#  - with -Paintings (the user's own paintings, comma-separated), paintings.png: each beside a shoulder view with a strong layer;
#  - shadow.png: the misty ridges at 07:30 with a strong layer, through the LUTs, the reference and the reference with the terrain's
#    shadow in the air (the known limit: the LUT air in a mountain's shadow is lit), at one exposure; the light by rows to haze.log;
#  - with -Before (a main build's Vista.App.dll), cost: the four ledger views at 3840x2160, 64 spp, main, this build and this build
#    with a strong valley haze alternately, -Rounds rounds.
# Build first: dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-haze'),
    [string]$App = '',
    [string]$Before = '',
    [string]$Photo = '',
    [string]$Paintings = '',
    [int]$CellWidth = 320,
    [int]$Spp = 96,
    [int]$Rounds = 3,
    [string]$Sheets = 'check,morning,winter,strengths,places,photo,paintings,shadow,cost'
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell leaves $PSScriptRoot empty in the parameters' defaults when another parameter is given.
if ($App -eq '') { $App = Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll' }
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null
$views = Split-Path $PSScriptRoot -Parent
$beforeViews = $views
if ($Before -ne '') {
    $root = $Before
    for ($k = 0; $k -lt 5; $k++) { $root = Split-Path $root -Parent }
    if (Test-Path (Join-Path $root 'vista-views')) { $beforeViews = Join-Path $root 'vista-views' }
}
$golden = Join-Path $views 'mountains-golden.recipe.json'
$day = Join-Path $views 'mountains-day.recipe.json'
$october = Join-Path $views 'atmosphere\zermatt-october.recipe.json'
$ridges = Join-Path $PSScriptRoot 'misty-ridges.camera.json'
$across = Join-Path $PSScriptRoot 'across-valley.camera.json'
$aerial = Join-Path $views 'aerial.camera.json'
$strong = '6'
$make = $Sheets.Split(',')
$log = Join-Path $Out 'haze.log'

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

function Sheet([string[]]$tiles, [string[]]$labels, [int]$columns, [string]$title, [string]$name) {
    Run $App (@('--sheet-from') + $tiles + @('--columns', "$columns", '--labels', ($labels -join ','), '--title', $title, '--out', (Join-Path $Out $name)))
}

# The valley haze slider's value as the sidecar of a render records it, e.g. "25.2 km under a lid at 2054 m" or "none".
function Valley([string]$png) {
    (Get-Content ([IO.Path]::ChangeExtension($png, '.json')) -Raw -Encoding UTF8 | ConvertFrom-Json).sliders.values.valleyHaze.value
}

# A 16:9 cell of a photo or painting: the crop (x, y, w, h in its pixels) downscaled to the cell width, no metadata.
function Cell([string]$from, [int[]]$crop, [string]$to) {
    $source = New-Object System.Drawing.Bitmap $from
    $height = [int][Math]::Round($CellWidth * 9.0 / 16.0)
    $target = New-Object System.Drawing.Bitmap $CellWidth, $height
    $g = [System.Drawing.Graphics]::FromImage($target)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($source, (New-Object System.Drawing.Rectangle 0, 0, $CellWidth, $height), (New-Object System.Drawing.Rectangle $crop[0], $crop[1], $crop[2], $crop[3]),
        [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose(); $source.Dispose()
    $target.Save($to, [System.Drawing.Imaging.ImageFormat]::Png)
    $target.Dispose()
}

# The largest 16:9 crop of an image, centred.
function Middle([string]$from) {
    $image = New-Object System.Drawing.Bitmap $from
    $w = $image.Width; $h = $image.Height
    $image.Dispose()
    if ($w * 9 -ge $h * 16) { $cw = [int]($h * 16 / 9); return @([int](($w - $cw) / 2), 0, $cw, $h) }
    $ch = [int]($w * 9 / 16)
    return @(0, [int](($h - $ch) / 2), $w, $ch)
}

# The segment check: the LUT air against the reference, with and without a valley haze.
if ($make -contains 'check') {
    foreach ($case in @(@('none', @()), @('auto-like', @('--valley-haze', '450,0.1')), @('strong', @('--valley-haze', '450,0.5')))) {
        foreach ($dir in @(@('up', @('--observer-height', '2')), @('down', @('--observer-height', '1700', '--below-horizon')))) {
            # Single scattering alone for the strong layer: where its light is lost, the tables or the march.
            foreach ($defines in @('', 'SINGLE_SCATTERING_ONLY')) {
                if ($defines -ne '' -and $case[0] -ne 'strong') { continue }
                $env:VISTA_SHADER_DEFINES = $defines
                $name = "check-$($case[0])-$($dir[0])$(if ($defines -ne '') { '-single' })"
                & dotnet $App (@('--atmosphere-check', '--sun-elevations', '30,5,-2,-6', '--segments', '1000,4000,16000', '--paths', '1048576') + $dir[1] + $case[1] +
                    @('--out', (Join-Path $Out "$name.json"))) *> (Join-Path $Out "$name.log")
                if ($LASTEXITCODE -ne 0) { throw "The check $name failed." }
                foreach ($line in (Get-Content (Join-Path $Out "$name.log") | Where-Object { $_ -match '^sun ' })) { Say "$($name): $($line.Trim())" }
            }
        }
    }
    $env:VISTA_SHADER_DEFINES = ''
}

# The morning: the night's pool at dawn, broken up by the sun.
if ($make -contains 'morning') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'morning') | Out-Null
    $tiles = @(); $labels = @()
    foreach ($time in @('dawn', '07:00', '08:00', '10:00', '12:00')) {
        foreach ($camera in @(@('misty ridges', $ridges), @('across the valley', $across))) {
            $png = Join-Path $Out "morning\$(Slug $camera[0])-$(Slug $time).png"
            Run $App @('--preview', '--recipe', $october, '--seed', '42', '--camera', $camera[1], '--time', $time, '--width', $CellWidth, '--spp', $Spp, '--out', $png)
            $tiles += $png
            $labels += "$($camera[0]) · 15 Oct $time · $(Valley $png)"
        }
    }
    Sheet $tiles $labels 2 'M1.13a the morning at Zermatt on 15 October · the valley haze auto: the night''s cold pool, broken up by the sun · sunrise 06:31' 'morning.png'
}

# Winter: a snowy valley floor turns most of the sun away, and the pool outlasts noon.
if ($make -contains 'winter') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'winter') | Out-Null
    $tiles = @(); $labels = @()
    foreach ($date in @('2027-01-15', '2026-10-15')) {
        foreach ($time in @('10:00', '12:00', '14:00')) {
            $png = Join-Path $Out "winter\$date-$(Slug $time).png"
            Run $App @('--preview', '--recipe', $october, '--seed', '42', '--camera', $ridges, '--date', $date, '--time', $time, '--width', $CellWidth, '--spp', $Spp, '--out', $png)
            $tiles += $png
            $labels += "misty ridges · $date $time · $(Valley $png)"
        }
    }
    Sheet $tiles $labels 3 'M1.13a a snowy valley floor keeps its pool · 15 January (top) against 15 October (bottom) at Zermatt · the valley haze auto' 'winter.png'
}

# Off, auto and strong.
if ($make -contains 'strengths') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'strengths') | Out-Null
    $tiles = @(); $labels = @()
    foreach ($camera in @(@('misty ridges', $ridges), @('across the valley', $across), @('aerial', $aerial))) {
        foreach ($mode in @(@('off', @('--valley-haze', 'off')), @('auto', @()), @('strong', @('--valley-haze', $strong)))) {
            $png = Join-Path $Out "strengths\$(Slug $camera[0])-$($mode[0]).png"
            Run $App (@('--preview', '--recipe', $october, '--seed', '42', '--camera', $camera[1], '--time', '08:00', '--width', $CellWidth, '--spp', $Spp) + $mode[1] + @('--out', $png))
            $tiles += $png
            $labels += "$($camera[0]) · $($mode[0]) · $(Valley $png)"
        }
    }
    Sheet $tiles $labels 3 "M1.13a the valley haze off, auto and strong ($strong km under the lid) · 15 October 08:00 at Zermatt" 'strengths.png'
}

# The places: each place's own range sets its pool.
if ($make -contains 'places') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'places') | Out-Null
    $tiles = @(); $labels = @()
    foreach ($place in @('zermatt', 'lofoten', 'yosemite', 'banff', 'hokkaido', 'fiordland')) {
        $png = Join-Path $Out "places\$place.png"
        Run $App @('--preview', '--recipe', $october, '--seed', '42', '--camera', $ridges, '--location', $place, '--season', 'autumn', '--time', 'dawn', '--sun-elevation', '6',
            '--width', $CellWidth, '--spp', $Spp, '--out', $png)
        $tiles += $png
        $labels += "$place · autumn · sun 6° rising · $(Valley $png)"
    }
    Sheet $tiles $labels 3 'M1.13a the pool from each place''s climate · the misty ridges in autumn, the sun 6° up and rising · the valley haze auto' 'places.png'
}

# The user's photo over the Alps, the haze band's crisp top at the horizon, beside the high views in its light.
if ($Photo -ne '' -and $make -contains 'photo') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'photo') | Out-Null
    $photoCell = Join-Path $Out 'photo\photo.png'
    # The view out of the window, clear of its frame (the 1536x2048 phone photo 20250411_110803), at 16:9.
    Cell $Photo @(0, 520, 1530, 861) $photoCell
    $tiles = @($photoCell); $labels = @('the user''s photo · 11 April 2025 11:08 · 10 km up · a phone through a window')
    $light = @('--preview', '--recipe', $day, '--date', '2026-04-11', '--time', '10:20', '--seed', '42', '--width', $CellWidth, '--spp', $Spp)
    $tenKm = Join-Path $PSScriptRoot 'ten-km.camera.json'
    foreach ($shot in @(@('aerial', $aerial, 'strong', @('--valley-haze', $strong)), @('10 km up', $tenKm, 'auto', @()), @('10 km up', $tenKm, 'strong', @('--valley-haze', $strong)))) {
        $png = Join-Path $Out "photo\$(Slug $shot[0])-$($shot[2]).png"
        Run $App ($light + @('--camera', $shot[1]) + $shot[3] + @('--out', $png))
        $tiles += $png
        $labels += "seed 42 · $($shot[0]) · 11 April 10:20 solar · $($shot[2]) · $(Valley $png)"
    }
    Sheet $tiles $labels 2 'M1.13a the haze band against the user''s photo · by 10:20 the auto pool is gone; the strong layer is pinned' 'photo.png'
}

# The user's paintings, beside the shoulder views with a strong layer.
if ($Paintings -ne '' -and $make -contains 'paintings') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'paintings') | Out-Null
    $tiles = @(); $labels = @()
    $k = 0
    foreach ($painting in $Paintings.Split(',')) {
        $k++
        $cell = Join-Path $Out "paintings\painting-$k.png"
        Cell $painting (Middle $painting) $cell
        $camera = @(@('misty ridges', $ridges), @('across the valley', $across))[($k - 1) % 2]
        $png = Join-Path $Out "paintings\$(Slug $camera[0])-$k.png"
        Run $App @('--preview', '--recipe', $october, '--seed', '42', '--camera', $camera[1], '--time', '08:00', '--valley-haze', $strong, '--width', $CellWidth, '--spp', $Spp, '--out', $png)
        $tiles += $cell, $png
        $labels += "the user's painting $k (2019)", "$($camera[0]) · 15 Oct 08:00 · $(Valley $png)"
    }
    Sheet $tiles $labels 2 'M1.13a the mist at the mountains'' foot · the user''s paintings beside the shoulder views with a strong valley haze · judged on how the mist sits' 'paintings.png'
}

# The known limit: the LUT air in a mountain's shadow is lit.
if ($make -contains 'shadow') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'shadow') | Out-Null
    $tiles = @(); $labels = @()
    foreach ($mode in @(@('luts', '1024'), @('reference', '8192'), @('referenceShadowed', '8192'))) {
        $png = Join-Path $Out "shadow\$($mode[0]).png"
        Run $App @('--preview', '--recipe', $october, '--seed', '42', '--camera', $ridges, '--time', '07:30', '--valley-haze', $strong, '--atmosphere', $mode[0],
            '--width', '160', '--spp', $mode[1], '--out', $png)
        $tiles += $png
        $labels += "$($mode[0]) · $($mode[1]) spp"
    }
    Sheet $tiles $labels 3 "M1.13a haze in the ridges' shadow · 15 Oct 07:30 · $strong km · one exposure" 'shadow.png'
}

# The cost ledger: main, this build and this build with a strong valley haze, alternately, view by view.
if ($Before -ne '' -and $make -contains 'cost') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'cost') | Out-Null
    $ratios = @{}
    $builds = @(@('main', $Before, $beforeViews, @()), @('m113a', $App, $views, @()), @('strong', $App, $views, @('--valley-haze', $strong)))
    for ($round = 1; $round -le $Rounds; $round++) {
        foreach ($view in $ledger) {
            $slug = Slug $view[0]
            $ns = @{}
            foreach ($build in $builds) {
                $png = Join-Path $Out "cost\$($build[0])-$slug.png"
                Run $build[1] (@('--preview', '--recipe', $view[1], '--seed', '42', '--camera', (Join-Path $build[2] $view[2]), '--width', '3840', '--spp', '64') + $view[3] + $build[3] + @('--out', $png))
                $stats = (Get-Content ([IO.Path]::ChangeExtension($png, '.json')) -Raw | ConvertFrom-Json).render.stats
                $ns[$build[0]] = $stats.traceSeconds * 1e9 / $stats.pixelSamples
                $ns["$($build[0])-rays"] = $stats.raysTraced / $stats.pixelSamples
                # Only the timing is kept: a 4K frame's images are over 100 MB.
                Remove-Item $png, ([IO.Path]::ChangeExtension($png, '.exr')) -ErrorAction SilentlyContinue
            }
            foreach ($b in @('m113a', 'strong')) {
                $key = "$slug-$b"
                if (-not $ratios.ContainsKey($key)) { $ratios[$key] = @() }
                $ratios[$key] += $ns[$b] / $ns['main']
            }
            Say ([string]::Format([cultureinfo]::InvariantCulture, 'cost: round {0} {1,-11} main {2,6:0.00} ns ({3:0.00} rays)  M1.13a {4,6:0.00} ns ({5:0.00} rays)  strong {6,6:0.00} ns ({7:0.00} rays)',
                $round, $slug, $ns['main'], $ns['main-rays'], $ns['m113a'], $ns['m113a-rays'], $ns['strong'], $ns['strong-rays']))
        }
    }
    foreach ($view in $ledger) {
        foreach ($b in @('m113a', 'strong')) {
            $sorted = $ratios["$(Slug $view[0])-$b"] | Sort-Object
            Say ([string]::Format([cultureinfo]::InvariantCulture, 'cost: median ratio {0,-11} {1,-6} {2:0.000} ({3:0.000}-{4:0.000})', (Slug $view[0]), $b,
                $sorted[[int][math]::Floor($sorted.Count / 2)], $sorted[0], $sorted[-1]))
        }
    }
}

Write-Output "Sheets in $Out ($log has the checks, the shadow and the cost)"
