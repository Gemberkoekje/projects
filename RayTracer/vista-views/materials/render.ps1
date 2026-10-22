# The M1.12 checks (vista-plan section 10, M1.12) on the local GPU, as sheets of at most -CellWidth pixels a cell:
#  - maps.png: each seed's materials from above (the dry run's material map: rock blue-grey or slate by its bed, scree sand, soil
#    brown, grass green, wet ground darker and bluer, over a hillshade), with the shares of the world grid each covers;
#  - ids.png: each seed from the aerial camera on a summer afternoon: the beauty, the material-id output and the tier output;
#  - with -Before (a main build's Vista.App.dll): views.png, the seed-42 views, main left and this build right;
#  - diffuse.png: Lambert's ground against rough ground (--diffuse-compare) at golden hour, with the ratio maps;
#  - loupes.png: windows of 3840x2160 frames near the camera (the scree cone, the rock face, the meadow), this build; with -Before,
#    main's scree cone beside it.
# Worlds come from -Worlds (default: the user's cache, %LOCALAPPDATA%\Vista\worlds, as previews read them; seeds missing there are
# eroded into it, about 40 s each). Build first: dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-materials'),
    [string]$App = '',
    [string]$Before = '',
    [string]$Worlds = '',
    [int]$CellWidth = 400,
    [int]$Spp = 64,
    [int]$MapSize = 320,
    [string]$Seeds = '42,7,1234,2026',
    # Which sheets to make (the maps need the worlds the ids' renders erode, so a first run makes both).
    [string]$Sheets = 'ids,maps,views,diffuse,loupes'
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell leaves $PSScriptRoot empty in the parameters' defaults when another parameter is given.
if ($App -eq '') { $App = Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll' }
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null
if ($Worlds -ne '') { $env:VISTA_WORLD_CACHE = $Worlds }
$views = Split-Path $PSScriptRoot -Parent
$golden = Join-Path $views 'mountains-golden.recipe.json'
$day = Join-Path $views 'mountains-day.recipe.json'
$aerial = Join-Path $views 'aerial.camera.json'
$seedList = $Seeds.Split(',')
$make = $Sheets.Split(',')

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

function Percent([double]$share) { [string]::Format([cultureinfo]::InvariantCulture, '{0:0}%', 100.0 * $share) }

# The ids: each seed from the aerial camera on a summer afternoon (the sun at 30 degrees), the beauty and its two debug outputs.
if ($make -contains 'ids') {
$tiles = @(); $labels = @()
New-Item -ItemType Directory -Force (Join-Path $Out 'ids') | Out-Null
foreach ($seed in $seedList) {
    $png = Join-Path $Out "ids\seed$seed.png"
    Run $App @('--preview', '--recipe', $day, '--sun-elevation', '30', '--seed', $seed, '--camera', $aerial, '--width', $CellWidth, '--spp', $Spp,
        '--debug', 'material,tier', '--out', $png)
    $tiles += @($png, (Join-Path $Out "ids\seed$seed.material.png"), (Join-Path $Out "ids\seed$seed.tier.png"))
    $labels += @("seed $seed · beauty", "seed $seed · material", "seed $seed · tier (all built in)")
}
Run $App (@('--sheet-from') + $tiles + @('--columns', '3', '--labels', ($labels -join ','),
    '--title', 'M1.12 four seeds from the aerial camera · the sun at 30 degrees · material ids in the legend colours of maps.png · tier 5 orange',
    '--out', (Join-Path $Out 'ids.png')))
}

# The maps: each seed's dry run writes its materials from above and the shares in its sidecar (after the renders above, which
# eroded any world the cache did not hold: a dry run cannot).
if ($make -contains 'maps') {
$tiles = @(); $labels = @()
New-Item -ItemType Directory -Force (Join-Path $Out 'maps') | Out-Null
foreach ($seed in $seedList) {
    $png = Join-Path $Out "maps\seed$seed.png"
    Run $App @('--preview', '--dry-run', '--recipe', $golden, '--seed', $seed, '--camera', $aerial, '--out', $png)
    $shares = (Get-Content (Join-Path $Out "maps\seed$seed.json") -Raw | ConvertFrom-Json).materials.shares
    $cell = Join-Path $Out "maps\cell$seed.png"
    Reduce (Join-Path $Out "maps\seed$seed.materials.png") $cell $MapSize
    $tiles += $cell
    $labels += "seed $seed · rock $(Percent ($shares.rockHard + $shares.rockSoft)) · scree $(Percent $shares.scree) · soil $(Percent $shares.soil) · grass $(Percent $shares.grass)"
}
Run $App (@('--sheet-from') + $tiles + @('--columns', '2', '--labels', ($labels -join ','),
    '--title', 'M1.12 materials from above · rock blue-grey (hard) or slate (soft) · scree sand · soil brown · grass green · wet bluer · north up',
    '--out', (Join-Path $Out 'maps.png')))
}

if ($Before -ne '' -and $make -contains 'views') {
    # The seed-42 views: main left, this build right, golden hour and a summer afternoon.
    $shots = @(
        @('default · golden hour', $golden, 'default.camera.json', @()),
        @('down the slope · golden hour', $golden, 'down-slope.camera.json', @()),
        @('range · afternoon', $day, 'atmosphere\range.camera.json', @('--sun-elevation', '25')),
        @('default · afternoon', $day, 'default.camera.json', @('--sun-elevation', '25'))
    )
    $tiles = @(); $labels = @()
    New-Item -ItemType Directory -Force (Join-Path $Out 'views') | Out-Null
    $k = 0
    foreach ($shot in $shots) {
        $k++
        foreach ($build in @(@('main', $Before, 'main (M1.11d)'), @('m112', $App, 'M1.12'))) {
            $png = Join-Path $Out "views\$($build[0])-$k.png"
            Run $build[1] (@('--preview', '--recipe', $shot[1], '--seed', '42', '--camera', (Join-Path $views $shot[2]), '--width', $CellWidth, '--spp', $Spp) + $shot[3] + @('--out', $png))
            $tiles += $png
            $labels += "$($shot[0]) · $($build[2])"
        }
    }
    Run $App (@('--sheet-from') + $tiles + @('--columns', '2', '--labels', ($labels -join ','),
        '--title', 'M1.12 the seed-42 views · main left (one grey material) · the ground''s materials by rule right', '--out', (Join-Path $Out 'views.png')))
}

# Lambert's ground against rough ground at golden hour.
if ($make -contains 'diffuse') {
$cameras = @((Join-Path $views 'atmosphere\away-from-sun.camera.json'), (Join-Path $views 'default.camera.json'), $aerial) -join ','
& dotnet $App --diffuse-compare --recipe $golden --cameras $cameras --width 320 --spp 128 --out (Join-Path $Out 'diffuse') *> (Join-Path $Out 'diffuse.log')
if ($LASTEXITCODE -ne 0) { throw 'The diffuse comparison failed.' }
Copy-Item (Join-Path $Out 'diffuse\diffuse.png') (Join-Path $Out 'diffuse.png') -Force
}

# The loupes: windows of 3840x2160 frames (exactly crops of them) near the camera, mid-morning in summer (the sun ahead and to the
# right of the east-facing views, so stones and ledges cast their shade toward the camera).
if ($make -contains 'loupes') {
$loupes = @(
    @('scree cone', 'scree-cone.camera.json', '1820,1171,520,292'),
    @('rock face', 'rock-face.camera.json', '1660,491,520,292'),
    @('meadow and soil', 'meadow.camera.json', '1060,671,520,292')
)
$tiles = @(); $labels = @()
New-Item -ItemType Directory -Force (Join-Path $Out 'loupes') | Out-Null
$k = 0
foreach ($loupe in $loupes) {
    $k++
    # The comma keeps a one-build list a list of lists (PowerShell flattens @(@(...)) into the inner array).
    $builds = @(, @('m112', $App, 'M1.12'))
    if ($Before -ne '' -and $k -eq 1) { $builds = @(, @('main', $Before, 'main (M1.11d)')) + $builds }
    foreach ($build in $builds) {
        $png = Join-Path $Out "loupes\$($build[0])-$k.png"
        Run $build[1] @('--preview', '--recipe', $day, '--time', '10:30', '--seed', '42', '--camera', (Join-Path $PSScriptRoot $loupe[1]), '--width', '3840',
            '--loupe', $loupe[2], '--spp', $Spp, '--out', $png)
        $tiles += $png
        $labels += "$($loupe[0]) · $($build[2]) · 3840x2160 window $($loupe[2].Replace(',', ' '))"
    }
}
Run $App (@('--sheet-from') + $tiles + @('--columns', '2', '--labels', ($labels -join ','),
    '--title', 'M1.12 loupes near the camera · 3840x2160 crops · 10:30 in July · rock crests and laminae · scree stones · meadow', '--out', (Join-Path $Out 'loupes.png')))
}

$env:VISTA_WORLD_CACHE = ''
Write-Output "Sheets in $Out ($(Join-Path $Out 'diffuse.log') has the diffuse ratios)"
