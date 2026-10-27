# The M1.13b checks (vista-plan section 10, M1.13b) on the local GPU, as sheets of at most -CellWidth pixels a cell:
#  - check: small, since a path through dense mist scatters hundreds of times: --pools-compare on the misty ridges at 07:30 on
#    15 October with a strong valley haze and mist pinned (96 px, the references 1024 spp), the march against the reference without
#    the terrain's shadow (LOW_AIR_NO_SHADOW), with single scattering alone too, and with the shadow against the plain and the
#    shadowed reference; and over the mist lake at Fiordland in autumn (mist-lake.camera.json), a deck pinned at 600 m to see with
#    the sun at 25° and at 6°, without the shadow (64 px, the reference 256 spp); lines to pools.log, and check.png: the march, the
#    reference and the shadowed reference at one exposure;
#  - dawn.png: peaks out of mist: the mist lake at Fiordland and the misty ridges at Hokkaido at autumn dawn (the pools and mist auto),
#    and at Zermatt with the mist pinned, beside M1.13a's flat layer (--pools flat) for the same air;
#  - morning.png: a pool and its mist from the night to their break-up: the mist lake at Fiordland on an autumn day;
#  - noise.png: the noise's strength: the misty ridges with the patches and the wisps off, as chosen, and stronger;
#  - places.png: the misty ridges at autumn dawn at six places, everything auto: which valleys hold mist;
#  - maps.png: the pools from above (the dry run's .pools.png) at those places;
#  - webcam.png: Zermatt at the two Sunnegga webcam frames' dates and times (vista-plan section 10: 12 Oct 2026 07:40 and 25 Oct 2026
#    17:00, Swiss time; 06:25 and 16:47 solar), the fog pinned at 200 m to see, from fog-sea.camera.json (a shoulder about 600 m above
#    a fogged valley, looking south-west as the webcam does). The frames are not ours: they stay local, beside it by hand;
#  - with -Before (a main build's Vista.App.dll), cost: the four ledger views at 3840x2160, 64 spp, main and this build
#    alternately, -Rounds rounds; and the misty ridges with mist against --pools flat, alternately.
# Build first: dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-pools'),
    [string]$App = '',
    [string]$Before = '',
    [int]$CellWidth = 320,
    [int]$Spp = 128,
    [int]$Rounds = 3,
    [string]$Sheets = 'check,dawn,morning,noise,places,maps,webcam,cost'
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell leaves $PSScriptRoot empty in the parameters' defaults when another parameter is given.
if ($App -eq '') { $App = Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll' }
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
$ridges = Join-Path $views 'haze\misty-ridges.camera.json'
$lake = Join-Path $PSScriptRoot 'mist-lake.camera.json'
$fogSea = Join-Path $PSScriptRoot 'fog-sea.camera.json'
$strong = @('--valley-haze', '6', '--mist', '0.4')
$make = $Sheets.Split(',')
$log = Join-Path $Out 'pools.log'

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

# Mid-autumn at a place: April in the southern hemisphere (Fiordland), October in the northern.
function Autumn([string]$place) { if ($place -eq 'fiordland') { @('--location', $place, '--date', '2026-04-15') } else { @('--location', $place, '--date', '2026-10-15') } }

# A line to the console and to the log (UTF-8; Tee-Object would write UTF-16 in Windows PowerShell).
function Say([string]$line) {
    Write-Host $line
    Add-Content -Path $log -Value $line -Encoding UTF8
}

function Sheet([string[]]$tiles, [string[]]$labels, [int]$columns, [string]$title, [string]$name) {
    Run $App (@('--sheet-from') + $tiles + @('--columns', "$columns", '--labels', ($labels -join ','), '--title', $title, '--out', (Join-Path $Out $name)))
}

# The mist slider's and the valley haze's values as the sidecar of a render records them, e.g. "300 m to see, 120 m deep".
function Readings([string]$png) {
    $values = (Get-Content ([IO.Path]::ChangeExtension($png, '.json')) -Raw -Encoding UTF8 | ConvertFrom-Json).sliders.values
    # The sheet's labels are comma-separated.
    "mist $($values.mist.value -replace ', ', ' · ')"
}

# The march against the reference: --pools-compare, its lines to the log (not "Compare": that is Compare-Object's alias, and aliases win).
function PoolsCompare([string]$name, [string[]]$arguments, [string]$defines) {
    $env:VISTA_SHADER_DEFINES = $defines
    & dotnet $App (@('--pools-compare') + $arguments + @('--out', (Join-Path $Out "check\$name.png"))) *> (Join-Path $Out "check\$name.log")
    if ($LASTEXITCODE -ne 0) { throw "The comparison $name failed." }
    $env:VISTA_SHADER_DEFINES = ''
    foreach ($line in (Get-Content (Join-Path $Out "check\$name.log") | Where-Object { $_ -match '(km|sky|frame|against|spp)' })) { Say "$($name): $($line.Trim())" }
}

if ($make -contains 'check') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'check') | Out-Null
    $misty = @('--recipe', $october, '--seed', '42', '--camera', $ridges, '--time', '07:30') + $strong + @('--width', '96', '--spp', '1024', '--reference-spp', '1024')
    PoolsCompare 'no-shadow' ($misty + @('--modes', 'luts,reference')) 'LOW_AIR_NO_SHADOW'
    PoolsCompare 'single' ($misty + @('--modes', 'luts,reference')) 'LOW_AIR_NO_SHADOW,SINGLE_SCATTERING_ONLY'
    PoolsCompare 'haze-only' (@('--recipe', $october, '--seed', '42', '--camera', $ridges, '--time', '07:30', '--valley-haze', '6', '--mist', 'off', '--width', '96',
        '--spp', '1024', '--reference-spp', '1024', '--modes', 'luts,reference')) 'LOW_AIR_NO_SHADOW'
    PoolsCompare 'shadow' $misty ''
    foreach ($sun in @('25', '6')) {
        PoolsCompare "deck-$sun" (@('--recipe', $october, '--seed', '42', '--camera', $lake) + (Autumn 'fiordland') + @('--time', 'dawn', '--sun-elevation', $sun,
            '--mist', '0.6', '--width', '64', '--spp', '1024', '--reference-spp', '256', '--modes', 'luts,reference')) 'LOW_AIR_NO_SHADOW'
    }
    $c = Join-Path $Out 'check'
    Sheet @("$c\shadow.luts.png", "$c\shadow.reference.png", "$c\shadow.referenceShadowed.png", "$c\no-shadow.luts.png") `
        @('the march · 1024 spp', 'the reference · 1024 spp', 'the shadowed reference · 1024 spp', 'the march without the terrain''s shadow') 2 `
        'M1.13b the pools against the reference · misty ridges, 15 Oct 07:30, a strong pool and mist pinned · one exposure' 'check.png'
}

# Peaks out of mist: auto where the dawn saturates, pinned at Zermatt; beside M1.13a's flat layer for the same air.
if ($make -contains 'dawn') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'dawn') | Out-Null
    $tiles = @(); $labels = @()
    $shots = @(
        @('fiordland autumn', $lake, ((Autumn 'fiordland') + @('--time', 'dawn', '--sun-elevation', '4'))),
        @('hokkaido autumn', $ridges, ((Autumn 'hokkaido') + @('--time', 'dawn', '--sun-elevation', '4'))),
        @('zermatt · mist pinned', $ridges, (@('--time', '07:30') + $strong))
    )
    foreach ($shot in $shots) {
        foreach ($mode in @('flat', 'on')) {
            $png = Join-Path $Out "dawn\$(Slug $shot[0])-$mode.png"
            Run $App (@('--preview', '--recipe', $october, '--seed', '42', '--camera', $shot[1]) + $shot[2] + @('--pools', $mode, '--width', $CellWidth, '--spp', $Spp, '--out', $png))
            $tiles += $png
            $labels += "$($shot[0]) · $(if ($mode -eq 'flat') { 'M1.13a: one flat layer' } else { 'M1.13b: each valley''s pool' }) · $(Readings $png)"
        }
    }
    Sheet $tiles $labels 2 'M1.13b peaks out of mist · each valley''s pool on its own floor, mist where it saturates · left M1.13a''s flat layer for the same air' 'dawn.png'
}

# A pool and its mist from the night to their break-up.
if ($make -contains 'morning') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'morning') | Out-Null
    $tiles = @(); $labels = @()
    foreach ($elevation in @('-2', '1', '4', '8', '13', '18')) {
        $png = Join-Path $Out "morning\sun-$(Slug $elevation).png"
        Run $App (@('--preview', '--recipe', $october, '--seed', '42', '--camera', $lake) + (Autumn 'fiordland') + @('--time', 'dawn',
            '--sun-elevation', $elevation, '--width', $CellWidth, '--spp', $Spp, '--out', $png))
        $tiles += $png
        $labels += "the sun at $elevation° rising · $(Readings $png)"
    }
    Sheet $tiles $labels 3 'M1.13b a morning at Fiordland in autumn · over the mist lake · the pools and mist auto, from before sunrise to their break-up' 'morning.png'
}

# The noise: off, as chosen, stronger.
if ($make -contains 'noise') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'noise') | Out-Null
    $tiles = @(); $labels = @()
    foreach ($noise in @(@('no noise', 'PatchStrength=0;WispAmplitudeMetres=0'), @('as chosen', ''), @('patches stronger', 'PatchStrength=1.6'),
            @('wisps stronger', 'WispAmplitudeMetres=60'))) {
        $env:VISTA_LOW_AIR = $noise[1]
        $png = Join-Path $Out "noise\$(Slug $noise[0]).png"
        Run $App (@('--preview', '--recipe', $october, '--seed', '42', '--camera', $ridges, '--time', '07:30') + $strong + @('--width', $CellWidth, '--spp', $Spp, '--out', $png))
        $tiles += $png
        $labels += "$($noise[0])$(if ($noise[1] -ne '') { " · $($noise[1])" })"
    }
    $env:VISTA_LOW_AIR = ''
    Sheet $tiles $labels 2 'M1.13b the noise''s strength · misty ridges, 15 Oct 07:30, a strong pool and mist pinned' 'noise.png'
}

# Which valleys hold mist at each place's autumn dawn.
if ($make -contains 'places') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'places') | Out-Null
    $tiles = @(); $labels = @()
    foreach ($place in @('zermatt', 'scottishHighlands', 'iceland', 'yellowstone', 'hokkaido', 'fiordland')) {
        $png = Join-Path $Out "places\$place.png"
        Run $App (@('--preview', '--recipe', $october, '--seed', '42', '--camera', $ridges) + (Autumn $place) + @('--time', 'dawn', '--sun-elevation', '4',
            '--width', $CellWidth, '--spp', $Spp, '--out', $png))
        $tiles += $png
        $labels += "$place · autumn · sun 4° rising · $(Readings $png)"
    }
    Sheet $tiles $labels 3 'M1.13b the pools and mist from each place''s climate · the misty ridges at autumn dawn · everything auto' 'places.png'
}

# The pools from above: the dry run's map at the same places.
if ($make -contains 'maps') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'maps') | Out-Null
    $tiles = @(); $labels = @()
    foreach ($place in @('zermatt', 'iceland', 'hokkaido', 'fiordland')) {
        $png = Join-Path $Out "maps\$place.png"
        Run $App (@('--preview', '--recipe', $october, '--seed', '42') + (Autumn $place) + @('--time', 'dawn', '--sun-elevation', '4', '--dry-run', '--out', $png))
        $tiles += [IO.Path]::ChangeExtension($png, '.pools.png')
        $labels += "$place · autumn dawn · $(Readings $png)"
    }
    Sheet $tiles $labels 2 'M1.13b the pools from above · the seed-42 world, 32.8 km · blue: a pool''s haze over the ground, white: mist' 'maps.png'
}

# Zermatt at the webcam frames' dates and times, the fog pinned (Zermatt's auto dawn holds none).
if ($make -contains 'webcam') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'webcam') | Out-Null
    $tiles = @(); $labels = @()
    foreach ($frame in @(@('12-10-2026-0740', '2026-10-12', '06:25', 'first light over a sea of valley fog'),
            @('25-10-2026-1700', '2026-10-25', '16:47', 'overcast in the frame and clear here (clouds are M4''s)'))) {
        $png = Join-Path $Out "webcam\$($frame[0]).png"
        Run $App @('--preview', '--recipe', $october, '--seed', '42', '--camera', $fogSea, '--location', 'zermatt', '--date', $frame[1], '--time', $frame[2],
            '--mist', '0.2', '--width', $CellWidth, '--spp', $Spp, '--out', $png)
        $tiles += $png
        $labels += "the webcam's $($frame[0]) · $($frame[2]) solar · $($frame[3]) · $(Readings $png)"
    }
    Sheet $tiles $labels 2 'M1.13b Zermatt at the Sunnegga webcam frames'' dates and times · fog pinned · the frames themselves stay local' 'webcam.png'
}

# The cost ledger: main and this build alternately, view by view; and a misty view against the flat layer.
if ($Before -ne '' -and $make -contains 'cost') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'cost') | Out-Null
    $ratios = @{}
    for ($round = 1; $round -le $Rounds; $round++) {
        foreach ($view in $ledger) {
            $slug = Slug $view[0]
            $ns = @{}
            foreach ($build in @(@('main', $Before, $beforeViews), @('m113b', $App, $views))) {
                $png = Join-Path $Out "cost\$($build[0])-$slug.png"
                Run $build[1] (@('--preview', '--recipe', $view[1], '--seed', '42', '--camera', (Join-Path $build[2] $view[2]), '--width', '3840', '--spp', '64') + $view[3] + @('--out', $png))
                $stats = (Get-Content ([IO.Path]::ChangeExtension($png, '.json')) -Raw | ConvertFrom-Json).render.stats
                $ns[$build[0]] = $stats.traceSeconds * 1e9 / $stats.pixelSamples
                $ns["$($build[0])-rays"] = $stats.raysTraced / $stats.pixelSamples
                Remove-Item $png, ([IO.Path]::ChangeExtension($png, '.exr')) -ErrorAction SilentlyContinue
            }
            if (-not $ratios.ContainsKey($slug)) { $ratios[$slug] = @() }
            $ratios[$slug] += $ns['m113b'] / $ns['main']
            Say ([string]::Format([cultureinfo]::InvariantCulture, 'cost: round {0} {1,-11} main {2,6:0.00} ns ({3:0.00} rays)  M1.13b {4,6:0.00} ns ({5:0.00} rays)',
                $round, $slug, $ns['main'], $ns['main-rays'], $ns['m113b'], $ns['m113b-rays']))
        }
        # A misty view: the pools and mist against M1.13a's flat layer for the same air.
        $ns = @{}
        foreach ($mode in @('flat', 'on')) {
            $png = Join-Path $Out "cost\misty-$mode.png"
            Run $App (@('--preview', '--recipe', $october, '--seed', '42', '--camera', $ridges, '--time', '07:30') + $strong + @('--pools', $mode, '--width', '1920', '--spp', '64', '--out', $png))
            $stats = (Get-Content ([IO.Path]::ChangeExtension($png, '.json')) -Raw | ConvertFrom-Json).render.stats
            $ns[$mode] = $stats.traceSeconds * 1e9 / $stats.pixelSamples
        }
        if (-not $ratios.ContainsKey('misty')) { $ratios['misty'] = @() }
        $ratios['misty'] += $ns['on'] / $ns['flat']
        Say ([string]::Format([cultureinfo]::InvariantCulture, 'cost: round {0} misty ridges flat {1,6:0.00} ns  pools {2,6:0.00} ns', $round, $ns['flat'], $ns['on']))
    }
    foreach ($key in @('sky', 'aerial', 'default', 'down-slope', 'misty')) {
        $sorted = $ratios[$key] | Sort-Object
        Say ([string]::Format([cultureinfo]::InvariantCulture, 'cost: median ratio {0,-11} {1:0.000} ({2:0.000}-{3:0.000})', $key,
            $sorted[[int][math]::Floor($sorted.Count / 2)], $sorted[0], $sorted[-1]))
    }
}

Write-Output "Sheets in $Out ($log has the checks and the cost)"
