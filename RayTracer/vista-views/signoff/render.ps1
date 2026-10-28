# The M1.17 sign-off's look sheets (vista-plan section 10, M1.17) on the local GPU, as sheets of -CellWidth pixels a cell. Each sheet
# shows one of the look's open choices, rendered once and displayed under each candidate (display options never re-render):
#  - dusk.png: the hero view (an alpine range at golden hour, seed 42, hero.camera.json: the auto camera's pick) as the sun sets, at
#    the sun heights of the user's two dusk series (+2.7, +0.6, -0.9, -1.9, -3.7 and -4.5 degrees), the exposure following the light
#    all the way (the meter, today), half way, and not at all below the card's light with the sun about 2 degrees up (--adaptation);
#  - sunset.png: the sky toward the sunset (atmosphere/toward-sun.camera.json, 4.2 km up over the snow-capped range on a July evening)
#    at the 28-10 series' heights (+0.6, -0.8, -1.7, -4.5), one exposure for all as the series had (--adaptation 0), the display's own
#    white and, as the series' later photos were taken, 4400 K;
#  - balance.png: M1.12's views and the hero under the display's own white (today) and a camera's daylight settings (5500 K, 5000 K);
#  - photo.png: with -Photo (M1.13's photo sheet, whose first cell is the user's own photo over the Alps), the photo beside the
#    over-forest view in its light with the haze lowered to the photo's, under each white balance and the base look;
#  - sky.png: the deep clear sky (the sky view at noon) and the hero under the punchy look's margins (--sky-margin 0, 0.03, 0.06, 0.1),
#    with each cell's share of pixels whose red is 0;
#  - shadows.png: the hero and the down-slope view at golden hour at +1 EV (today), +1.5 and +2, and the base look at +1, with each
#    cell's mean;
#  - webcam.png: Zermatt at the Sunnegga webcam frames' dates and times (vista-plan section 10), from pools/fog-sea.camera.json (looking
#    south-west as the webcam does): renders only, the frames are not ours and stay local.
#  - with -Before (a build's Vista.App.dll from before M1.17's stop 2), before-after.png: that build's defaults against Vista's camera
#    (stop 2: 5500 K, +1.5 EV, half of dusk's fall, a 0.03 margin) on the hero at golden hour and at dusk, the four ledger views,
#    alpenglow at dusk and April's snow.
# The user's own dusk photos are never committed: with -Photos (a folder of them, DD-MM-YYYY-HHMMSS.jpg), dusk-photos.png and
# sunset-photos.png put them beside the renders, for showing locally.
# Build first: dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-signoff'),
    [string]$App = '',
    [string]$Photo = '',
    [string]$Photos = '',
    [string]$Before = '',
    [int]$CellWidth = 320,
    [int]$Spp = 64,
    [string]$Sheets = 'dusk,sunset,balance,photo,sky,shadows,webcam,before-after'
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell leaves $PSScriptRoot empty in the parameters' defaults when another parameter is given.
if ($App -eq '') { $App = Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll' }
Add-Type -AssemblyName System.Drawing
[System.Threading.Thread]::CurrentThread.CurrentCulture = [cultureinfo]::InvariantCulture
New-Item -ItemType Directory -Force $Out | Out-Null
$views = Split-Path $PSScriptRoot -Parent
$golden = Join-Path $views 'mountains-golden.recipe.json'
$day = Join-Path $views 'mountains-day.recipe.json'
$range = Join-Path $views 'autocam\range-golden.recipe.json'
$october = Join-Path $views 'atmosphere\zermatt-october.recipe.json'
$hero = Join-Path $PSScriptRoot 'hero.camera.json'
$july = Join-Path $views 'atmosphere\zermatt-july.recipe.json'
$towardSun = Join-Path $views 'atmosphere\toward-sun.camera.json'
$fogSea = Join-Path $views 'pools\fog-sea.camera.json'
$make = $Sheets.Split(',')
$log = Join-Path $Out 'signoff.log'

function Run([string[]]$arguments) {
    & dotnet $App @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Vista.App $($arguments -join ' ') failed." }
}

# A line to the console and to the log (UTF-8; Tee-Object would write UTF-16 in Windows PowerShell).
function Say([string]$line) {
    Write-Host $line
    Add-Content -Path $log -Value $line -Encoding UTF8
}

function Sheet([string[]]$tiles, [string[]]$labels, [int]$columns, [string]$title, [string]$name) {
    Run (@('--sheet-from') + $tiles + @('--columns', "$columns", '--labels', ($labels -join ','), '--title', $title, '--out', (Join-Path $Out $name)))
}

# One render displayed under each value of a display axis (--sheet re-exposes without rendering again): the cells' PNGs, in order.
function Variants([string]$folder, [string]$name, [string[]]$arguments, [string]$axis) {
    $dir = Join-Path $Out $folder
    New-Item -ItemType Directory -Force $dir | Out-Null
    $png = Join-Path $dir "$name.png"
    Run (@('--sheet') + $arguments + @('--width', "$CellWidth", '--spp', "$Spp", '--vary', $axis, '--out', $png))
    $count = $axis.Split('=')[1].Split(',').Count
    return @(1..$count | ForEach-Object { Join-Path $dir ("$name.cells\cell{0:00}.png" -f $_) })
}

function Sidecar([string]$png) { Get-Content ([IO.Path]::ChangeExtension($png, '.json')) -Raw -Encoding UTF8 | ConvertFrom-Json }

# The mean of a PNG's luma (sRGB-encoded, 0-255), and the share of its pixels whose red is 0 in its top -SkyRows rows.
function Stats([string]$png, [int]$skyRows) {
    $bitmap = New-Object System.Drawing.Bitmap $png
    $sum = 0.0; $count = 0; $red0 = 0; $sky = 0
    for ($y = 0; $y -lt $bitmap.Height; $y += 2) {
        for ($x = 0; $x -lt $bitmap.Width; $x += 2) {
            $c = $bitmap.GetPixel($x, $y)
            $sum += 0.2126 * $c.R + 0.7152 * $c.G + 0.0722 * $c.B; $count++
            if ($y -lt $skyRows) { $sky++; if ($c.R -eq 0) { $red0++ } }
        }
    }
    $bitmap.Dispose()
    return @{ Mean = $sum / $count; Red0 = $(if ($sky -gt 0) { $red0 / $sky } else { 0.0 }) }
}

# A photo cropped to 16:9 about its centre line and scaled to a cell.
function PhotoCell([string]$from, [string]$to, [double]$top) {
    $source = New-Object System.Drawing.Bitmap $from
    $height = [int]($source.Width * 9 / 16)
    $y = [int][Math]::Min([Math]::Max(0, $top * $source.Height), $source.Height - $height)
    $target = New-Object System.Drawing.Bitmap $CellWidth, ([int]($CellWidth * 9 / 16))
    $g = [System.Drawing.Graphics]::FromImage($target)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($source, (New-Object System.Drawing.Rectangle 0, 0, $target.Width, $target.Height), (New-Object System.Drawing.Rectangle 0, $y, $source.Width, $height),
        [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose(); $source.Dispose()
    $target.Save($to, [System.Drawing.Imaging.ImageFormat]::Png)
    $target.Dispose()
}

# Copies of cells scaled to <width> pixels across, for a sheet that would otherwise pass the plan's 1 MB a sheet.
function Small([string[]]$tiles, [int]$width) {
    foreach ($tile in $tiles) {
        $source = New-Object System.Drawing.Bitmap $tile
        $target = New-Object System.Drawing.Bitmap $width, ([int][Math]::Round($source.Height * $width / $source.Width))
        $g = [System.Drawing.Graphics]::FromImage($target)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.DrawImage($source, 0, 0, $target.Width, $target.Height)
        $g.Dispose(); $source.Dispose()
        $small = [IO.Path]::ChangeExtension($tile, ".w$width.png")
        $target.Save($small, [System.Drawing.Imaging.ImageFormat]::Png)
        $target.Dispose()
        $small
    }
}

$heroArgs = @('--recipe', $range, '--seed', '42', '--camera', $hero)

# Dusk: the hero as the sun sets, the exposure following the light all the way, half way and not at all.
$duskHeights = @('2.7', '0.6', '-0.9', '-1.9', '-3.7', '-4.5')
$adaptations = @('1', '0.5', '0')
if ($make -contains 'dusk') {
    $cells = @{}
    foreach ($e in $duskHeights) {
        $variants = Variants 'dusk' "sun$e" ($heroArgs + @('--sun-elevation', $e)) ('adaptation=' + ($adaptations -join ','))
        for ($k = 0; $k -lt $adaptations.Count; $k++) { $cells["$e|$($adaptations[$k])"] = $variants[$k] }
        $first = Sidecar $variants[0]
        $last = Sidecar $variants[-1]
        Say ("dusk: sun {0} deg: the card {1:0} lux, applied {2:0.00} EV by the meter, {3:0.00} with the adaptation at 0 ({4:0.00} held back); means {5}" -f $e,
            $first.exposure.incident.illuminanceLux, $first.exposure.appliedEv, $last.exposure.appliedEv, $last.exposure.adaptationEv,
            (($variants | ForEach-Object { '{0:0.0}' -f (Stats $_ 0).Mean }) -join ' / '))
    }
    $tiles = @(); $labels = @()
    foreach ($a in $adaptations) {
        $name = switch ($a) { '1' { 'follows the light (today)' } '0' { 'set at golden hour' } default { "follows $a of the drop" } }
        foreach ($e in $duskHeights) {
            $tiles += $cells["$e|$a"]
            $labels += "sun $($e)° · $name"
        }
    }
    Sheet (Small $tiles 256) $labels $duskHeights.Count 'M1.17 the exposure at dusk · the hero as the sun sets · top: the meter follows the light (today) · middle: half the drop · bottom: an exposure set with the sun 2° up, as a camera at fixed settings' 'dusk.png'
    if ($Photos -ne '') {
        $photoTiles = @(); $photoLabels = @()
        foreach ($p in @(@('27-10-2026-165859', '+2.7°'), @('28-10-2026-171219', '+0.6°'), @('27-10-2026-172416', '-0.9°'), @('27-10-2026-173103', '-1.9°'),
                @('27-10-2026-174344', '-3.7°'), @('28-10-2026-174648', '-4.5°'))) {
            $cell = Join-Path $Out "dusk\photo-$($p[0]).png"
            PhotoCell (Join-Path $Photos "$($p[0]).jpg") $cell 0.25
            $photoTiles += $cell; $photoLabels += "the user's photo · sun $($p[1])"
        }
        Sheet ($photoTiles + $tiles) ($photoLabels + $labels) $duskHeights.Count 'M1.17 the exposure at dusk · top row: the user''s photos (local only) · then the hero, the meter following the light all the way, half way, not at all' 'dusk-photos.png'
    }
}

# The sunset sky: toward the sun from high over the range, at the 28-10 series' heights, one exposure for all, the display's white and 4400 K.
$sunsetHeights = @('0.6', '-0.8', '-1.7', '-4.5')
if ($make -contains 'sunset') {
    $tiles = @(); $labels = @()
    foreach ($e in $sunsetHeights) {
        $variants = Variants 'sunset' "sun$e" @('--recipe', $july, '--seed', '42', '--camera', $towardSun, '--sun-elevation', $e, '--adaptation', '0') 'whiteBalance=none,4400K'
        $tiles += $variants; $labels += "sun $($e)° · the display's white", "sun $($e)° · 4400 K"
    }
    Sheet $tiles $labels 4 'M1.17 the sunset sky · the range view toward the sun 4.2 km up on a July evening at the user''s 28-10 series'' sun heights · one exposure for all, as the series'' · the display''s own white and 4400 K (the series'' setting from 17:27) · no clouds (M4)' 'sunset.png'
    if ($Photos -ne '') {
        $photoTiles = @(); $photoLabels = @()
        foreach ($p in @(@('28-10-2026-171219', '+0.6°'), @('28-10-2026-172153', '-0.8°'), @('28-10-2026-172757', '-1.7°'), @('28-10-2026-174648', '-4.5°'))) {
            $cell = Join-Path $Out "sunset\photo-$($p[0]).png"
            PhotoCell (Join-Path $Photos "$($p[0]).jpg") $cell 0.15
            $photoTiles += $cell; $photoLabels += "the user's photo · sun $($p[1])"
        }
        $ordered = @(); $orderedLabels = @()
        for ($k = 0; $k -lt $sunsetHeights.Count; $k++) {
            $ordered += $photoTiles[$k], $tiles[2 * $k], $tiles[2 * $k + 1]
            $orderedLabels += $photoLabels[$k], $labels[2 * $k], $labels[2 * $k + 1]
        }
        Sheet $ordered $orderedLabels 3 'M1.17 the sunset sky · the user''s 28-10 photos (local only) beside the sky toward the sun at their sun heights · one exposure for all' 'sunset-photos.png'
    }
}

# The white balance: the display's own white (D65, today) against a camera's daylight settings.
$balances = 'whiteBalance=none,5500K,5000K'
if ($make -contains 'balance') {
    $tiles = @(); $labels = @()
    $shots = @(
        @('hero · golden hour', $heroArgs),
        @('default · golden hour', @('--recipe', $golden, '--seed', '42', '--camera', (Join-Path $views 'default.camera.json'))),
        @('range · afternoon (25°)', @('--recipe', $day, '--seed', '42', '--camera', (Join-Path $views 'atmosphere\range.camera.json'), '--sun-elevation', '25')),
        @('rock face · noon', @('--recipe', $day, '--seed', '42', '--camera', (Join-Path $views 'materials\rock-face.camera.json'))),
        @('scree cone · noon', @('--recipe', $day, '--seed', '42', '--camera', (Join-Path $views 'materials\scree-cone.camera.json'))))
    foreach ($shot in $shots) {
        $slug = ($shot[0] -replace '[^a-z0-9]+', '-').Trim('-')
        $variants = Variants 'balance' $slug $shot[1] $balances
        $tiles += $variants
        $labels += "$($shot[0]) · the display's white (today)", "$($shot[0]) · 5500 K (daylight)", "$($shot[0]) · 5000 K"
    }
    Sheet (Small $tiles 256) $labels 3 'M1.17 the white balance · left: the display''s own white, D65 (today) · middle and right: a camera''s daylight settings, a 5500 K and a 5000 K light rendered neutral' 'balance.png'
}

# The user's April photo beside the over-forest view in its light, the haze lowered to the photo's.
if ($Photo -ne '' -and $make -contains 'photo') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'photo') | Out-Null
    # M1.13's photo sheet (vista-views/sheets/m1-13-photo.png): its first cell, 400x225 at (6, 23) under the title, is the user's photo.
    $sheet = New-Object System.Drawing.Bitmap $Photo
    $photoCell = Join-Path $Out 'photo\photo.png'
    $target = New-Object System.Drawing.Bitmap $CellWidth, ([int]($CellWidth * 9 / 16))
    $g = [System.Drawing.Graphics]::FromImage($target)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($sheet, (New-Object System.Drawing.Rectangle 0, 0, $target.Width, $target.Height), (New-Object System.Drawing.Rectangle 6, 23, 400, 225), [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose(); $sheet.Dispose()
    $target.Save($photoCell, [System.Drawing.Imaging.ImageFormat]::Png)
    $target.Dispose()
    $light = @('--recipe', $day, '--date', '2026-04-11', '--time', '10:15', '--seed', '42', '--camera', (Join-Path $views 'forest\over-forest.camera.json'), '--visibility', '25')
    $punchy = Variants 'photo' 'punchy' $light $balances
    $base = Variants 'photo' 'base' ($light + @('--look', 'base')) $balances
    Sheet (@($photoCell) + $punchy + @($photoCell) + $base) @("the user's photo · 11 April 2025", "punchy · the display's white (today)", 'punchy · 5500 K', 'punchy · 5000 K',
        "the user's photo", "base look · the display's white", 'base look · 5500 K', 'base look · 5000 K') 4 'M1.17 the white balance and the look beside the user''s April photo · the over-forest view on 11 April at 10:15, the haze lowered to 25 km' 'photo.png'
}

# The deep clear sky: the punchy look's margin short of the gamut's edge.
$margins = 'skyMargin=0,0.03,0.06,0.1'
if ($make -contains 'sky') {
    $tiles = @(); $labels = @()
    $shots = @(@('sky · noon', @('--recipe', $day, '--seed', '42', '--camera', (Join-Path $views 'sky.camera.json')), 180),
        @('hero · golden hour', $heroArgs, 60))
    foreach ($shot in $shots) {
        $slug = ($shot[0] -replace '[^a-z0-9]+', '-').Trim('-')
        $variants = Variants 'sky' $slug $shot[1] $margins
        $values = $margins.Split('=')[1].Split(',')
        for ($k = 0; $k -lt $variants.Count; $k++) {
            $s = Stats $variants[$k] ([int]($shot[2] * $CellWidth / 320))
            Say ("sky: {0} margin {1}: red 0 in {2:0.0}% of the sky rows" -f $shot[0], $values[$k], (100 * $s.Red0))
            $labels += ("{0} · margin {1}{2} · red 0 in {3:0}%" -f $shot[0], $values[$k], $(if ($values[$k] -eq '0') { ' (today)' } else { '' }), (100 * $s.Red0))
        }
        $tiles += $variants
    }
    Sheet $tiles $labels 4 'M1.17 the deep clear sky · the punchy look''s saturation held back just enough (today) or a margin short of the gamut''s edge' 'sky.png'
}

# The punchy look's shadows: the exposure and the look at golden hour.
if ($make -contains 'shadows') {
    $tiles = @(); $labels = @()
    $shots = @(@('hero', $heroArgs), @('down the slope', @('--recipe', $golden, '--seed', '42', '--camera', (Join-Path $views 'down-slope.camera.json'))))
    foreach ($shot in $shots) {
        $slug = ($shot[0] -replace '[^a-z0-9]+', '-').Trim('-')
        $variants = Variants 'shadows' $slug $shot[1] 'compensation=1,1.5,2'
        $base = Variants 'shadows' "$slug-base" ($shot[1] + @('--look', 'base')) 'compensation=1'
        $names = @('punchy +1 EV (today)', 'punchy +1.5 EV', 'punchy +2 EV', 'base look +1 EV')
        $all = $variants + $base
        for ($k = 0; $k -lt $all.Count; $k++) {
            $mean = (Stats $all[$k] 0).Mean
            Say ("shadows: {0} {1}: mean {2:0.0} of 255" -f $shot[0], $names[$k], $mean)
            $labels += ("{0} · {1} · mean {2:0}" -f $shot[0], $names[$k], $mean)
        }
        $tiles += $all
    }
    Sheet $tiles $labels 4 'M1.17 the dark shadows at golden hour · the punchy look at +1 EV (today), +1.5 and +2, and the base look at +1' 'shadows.png'
}

# Before and after Vista's camera (stop 2): the same renders under the build before it and this one's defaults.
if ($Before -ne '' -and $make -contains 'before-after') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'before-after') | Out-Null
    $tiles = @(); $labels = @()
    $shots = @(
        @('hero · golden hour', $heroArgs),
        @('hero · dusk (sun -1.9°)', ($heroArgs + @('--sun-elevation', '-1.9'))),
        @('sky · noon', @('--recipe', $day, '--seed', '42', '--camera', (Join-Path $views 'sky.camera.json'))),
        @('aerial · noon', @('--recipe', $day, '--seed', '42', '--camera', (Join-Path $views 'aerial.camera.json'))),
        @('default · noon', @('--recipe', $day, '--seed', '42', '--camera', (Join-Path $views 'default.camera.json'))),
        @('down the slope · golden hour', @('--recipe', $golden, '--seed', '42', '--camera', (Join-Path $views 'down-slope.camera.json'))),
        @('alpenglow · sun -0.5°', @('--recipe', (Join-Path $views 'atmosphere\zermatt-july.recipe.json'), '--seed', '42', '--camera', (Join-Path $views 'atmosphere\range.camera.json'), '--sun-elevation', '-0.5')),
        @('April · noon', @('--recipe', (Join-Path $views 'snow\mountains-april.recipe.json'), '--seed', '42', '--camera', (Join-Path $views 'atmosphere\range.camera.json'))))
    foreach ($shot in $shots) {
        $slug = ($shot[0] -replace '[^a-z0-9]+', '-').Trim('-')
        foreach ($build in @(@('before', $Before, 'before (M1.16''s look)'), @('after', $App, 'Vista''s camera'))) {
            $png = Join-Path $Out "before-after\$($build[0])-$slug.png"
            & dotnet $build[1] (@('--preview') + $shot[1] + @('--width', "$CellWidth", '--spp', "$Spp", '--out', $png)) | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "The $($build[0]) render of $($shot[0]) failed." }
            $tiles += $png
            $labels += "$($shot[0]) · $($build[2]) · mean $('{0:0}' -f (Stats $png 0).Mean)"
        }
    }
    Sheet (Small $tiles 256) $labels 4 'M1.17 Vista''s camera · before (left of each pair: the display''s white, +1 EV, the meter following dusk) and after (right: 5500 K, +1.5 EV, half of dusk''s fall, a 0.03 margin)' 'before-after.png'
}

# The webcam frames' dates and times at Zermatt, looking south-west as the webcam does.
if ($make -contains 'webcam') {
    New-Item -ItemType Directory -Force (Join-Path $Out 'webcam') | Out-Null
    $tiles = @(); $labels = @()
    foreach ($frame in @(@('23-08-2026-0630', '2026-08-23', '04:58', 'before first light (sun -2.2°)'),
            @('23-08-2026-0640', '2026-08-23', '05:08', 'first light (-0.6°)'),
            @('12-10-2026-0740', '2026-10-12', '06:24', 'first light (-1.1°)'),
            @('07-05-2026-0600', '2026-05-07', '04:34', 'spring dawn · skylight only (-2.0°)'),
            @('23-08-2026-2010', '2026-08-23', '18:38', 'low evening sun (+1.5°)'),
            @('12-10-2026-1820', '2026-10-12', '17:04', 'flat light (+4.0° but behind the ridge)'),
            @('12-10-2026-1810', '2026-10-12', '16:54', 'into the sun (+5.7°)'))) {
        $png = Join-Path $Out "webcam\$($frame[0]).png"
        Run @('--preview', '--recipe', $october, '--seed', '42', '--camera', $fogSea, '--location', 'zermatt', '--date', $frame[1], '--time', $frame[2],
            '--width', "$CellWidth", '--spp', "$Spp", '--out', $png)
        $tiles += $png
        $labels += "webcam $($frame[0]) · $($frame[2]) solar · $($frame[3])"
    }
    Sheet $tiles $labels 4 'M1.17 Zermatt at the Sunnegga webcam frames'' dates and times · looking south-west as the webcam does · the frames themselves stay local' 'webcam.png'
}
