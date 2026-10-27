# The M1.14 checks (vista-plan section 10, M1.14) on the local GPU, as sheets of at most -CellWidth pixels a cell:
#  - finalists.png: one auto-camera run (-Seed, -Recipe), its finalists as it ranked them, each rendered at -CellWidth with its scores;
#  - seeds-peak-a.png, -b, seeds-range-a.png, -b: -Seeds (eight by default, four a sheet) at golden hour, the default camera beside
#    the auto camera's pick for snow-capped mountains (seeds-peak) and for an alpine range (seeds-range); every pick's focal length,
#    near-field share and 4K triangles in its label, and its height above the ground and every score in the log;
#  - paintings.png: the user's own paintings (-Paintings, comma-separated; cropped to 16:9, no metadata) beside the picks that best
#    match their composition, judged on composition rather than on colour or style;
#  - repeat: the same auto camera twice (seed 42, peak): the same pick, its camera file byte for byte; the scoring's time in the log.
# Every auto-camera sidecar's scores, times and pick go to autocam.log. -Keep reuses the auto-camera renders already in -Out (to
# re-tile a sheet). Build first: dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-autocam'),
    [string]$App = '',
    [int]$CellWidth = 320,
    [int]$Spp = 64,
    [string]$Seeds = '1,2,3,4,5,6,7,42',
    [string]$Seed = '42',
    [string]$Recipe = '',
    [string]$Paintings = '',
    [string]$Sheets = 'finalists,seeds,paintings,repeat',
    [switch]$Keep
)

$ErrorActionPreference = 'Stop'
# Numbers in labels and the log with a decimal point whatever the machine's culture (the labels are comma-separated).
[Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::InvariantCulture
# Windows PowerShell leaves $PSScriptRoot empty in the parameters' defaults when another parameter is given.
if ($App -eq '') { $App = Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll' }
New-Item -ItemType Directory -Force $Out | Out-Null
$views = Split-Path $PSScriptRoot -Parent
$peak = Join-Path $views 'mountains-golden.recipe.json'
$range = Join-Path $PSScriptRoot 'range-golden.recipe.json'
if ($Recipe -eq '') { $Recipe = $peak }
$make = $Sheets.Split(',')
$log = Join-Path $Out 'autocam.log'
$height = [int][Math]::Round($CellWidth * 9 / 16)

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

function Sidecar([string]$png) { Get-Content ([IO.Path]::ChangeExtension($png, '.json')) -Raw -Encoding UTF8 | ConvertFrom-Json }

# A pick's or a finalist's line: where, the lens, the total and the scores.
function Line($p) {
    $c = $p.camera; $s = $p.scores
    function Maybe($v) { if ($null -ne $v) { '{0:0.00}' -f $v } else { '-' } }
    $tri = if ($null -ne $s.triangles) { '{0:0.0} M' -f ($s.triangles / 1e6) } else { '-' }
    '{0} #{1} {2:0} mm at ({3:0}, {4:0}, {5:0}) {22:0} m up, yaw {6:0.0} pitch {7:0.0} | total {8:0.000}: sky {9:0.00} horizon {10:0.00} peak {11:0.00} layers {12:0.00} framing {13:0.00} lower {14:0.00} line {15:0.00} sun {16:0.00} light {23} squint {17} values {18} | near {19:0.0000} triangles {20} {21}' -f `
        $p.origin, $p.index, $c.focalLengthMm, $c.position.x, $c.position.y, $c.position.z, $c.yawDegrees, $c.pitchDegrees, $p.total, $s.sky, $s.horizon,
        $s.peak, $s.layers, $s.framing, $s.lowerThird, $s.leadingLine, $s.sun, (Maybe $s.squint), (Maybe $s.values), $s.nearShare, $tri,
        $(if ($p.breaks) { "breaks $($p.breaks)" } else { '' }), $p.aboveGroundMetres, (Maybe $s.light)
}

# An auto-camera run at the cell's size (kept when -Keep and it is there already); its sidecar's search to the log.
function Auto([string]$recipe, [string]$seed, [string]$png, [string[]]$extra = @()) {
    if (-not ($Keep -and (Test-Path $png))) {
        Run (@('--preview', '--recipe', $recipe, '--seed', $seed, '--auto-camera', '--width', "$CellWidth", '--spp', "$Spp", '--out', $png) + $extra)
    }
    $a = (Sidecar $png).autoCamera
    $t = $a.seconds
    Say ("{0} seed {1}: {2} candidates ({3} keep the limits, {4} on shoulders), planted {5:0} m from the subject heading {6:0} (sun {7:0} deg off); plant {8:0.0} s, buffers {9:0.0} s, finalists {10:0.0} s, meshes {11:0.0} s" -f `
        (Split-Path $recipe -Leaf), $seed, $a.candidates, $a.feasible, $a.shoulders, $a.planted.distanceMetres, $a.planted.headingDegrees, $a.planted.sunRelativeDegrees,
        $t.plant, $t.buffers, $t.finalists, $t.meshes)
    Say ("  pick: " + (Line $a.pick))
    $a
}

function CameraFile($camera, [string]$path) { $camera | ConvertTo-Json -Depth 4 | Set-Content -Path $path -Encoding UTF8 }

# The finalists of one run, as it ranked them.
if ($make -contains 'finalists') {
    $dir = Join-Path $Out 'finalists'
    New-Item -ItemType Directory -Force $dir | Out-Null
    $a = Auto $Recipe $Seed (Join-Path $dir 'pick.png')
    $tiles = @(); $labels = @()
    $k = 0
    foreach ($f in $a.finalists) {
        Say ("  finalist {0}: {1}" -f $k, (Line $f))
        $camera = Join-Path $dir "finalist-$k.camera.json"
        CameraFile $f.camera $camera
        $png = Join-Path $dir "finalist-$k.png"
        Run @('--preview', '--recipe', $Recipe, '--seed', $Seed, '--camera', $camera, '--width', "$CellWidth", '--spp', "$Spp", '--out', $png)
        $tiles += $png
        $labels += ('{0}. {1:0} mm · total {2:0.00} · squint {3:0.00}{4}' -f ($k + 1), $f.camera.focalLengthMm, $f.total, $f.scores.squint, $(if ($f.breaks) { " · breaks $($f.breaks)" } else { '' }))
        $k++
    }
    Sheet $tiles $labels 3 "M1.14 the auto camera's finalists · seed $Seed · as ranked" 'finalists.png'
}

# Eight seeds: the default camera, the pick for a peak and the pick for a range.
if ($make -contains 'seeds') {
    $dir = Join-Path $Out 'seeds'
    New-Item -ItemType Directory -Force $dir | Out-Null
    $kinds = @(@('peak', $peak, 'snow-capped mountains'), @('range', $range, 'an alpine range'))
    $all = $Seeds.Split(',')
    # Two sheets a set piece, four seeds each, so a sheet of 320 px cells stays near 1 MB.
    $halves = @(@('a', $all[0..([Math]::Ceiling($all.Count / 2) - 1)]), @('b', $all[([Math]::Ceiling($all.Count / 2))..($all.Count - 1)]))
    foreach ($kind in $kinds) { foreach ($half in $halves) {
        $tiles = @(); $labels = @()
        foreach ($s in $half[1]) {
            $default = Join-Path $dir "default-$s.png"
            if (-not (Test-Path $default)) { Run @('--preview', '--recipe', $kind[1], '--seed', $s, '--width', "$CellWidth", '--spp', "$Spp", '--out', $default) }
            $png = Join-Path $dir "$($kind[0])-$s.png"
            $a = Auto $kind[1] $s $png
            $c = $a.pick.camera
            $tri = if ($null -ne $a.pick.scores.triangles) { '{0:0.0} M' -f ($a.pick.scores.triangles / 1e6) } else { '-' }
            $tiles += $default, $png
            $labels += "seed $s · the default camera", ('seed {0} · auto · {1:0} mm · near {2:0.0}% · 4K {3}' -f $s, $c.focalLengthMm, (100 * $a.pick.scores.nearShare), $tri)
        }
        Sheet $tiles $labels 4 "M1.14 the auto camera · $($kind[2]) at golden hour · the default camera and the pick, two seeds a row" "seeds-$($kind[0])-$($half[0]).png"
    } }
}

# The user's paintings beside picks, on composition.
if ($Paintings -ne '' -and $make -contains 'paintings') {
    $dir = Join-Path $Out 'paintings'
    New-Item -ItemType Directory -Force $dir | Out-Null
    Add-Type -AssemblyName System.Drawing
    $tiles = @(); $labels = @()
    $k = 0
    foreach ($painting in $Paintings.Split(',')) {
        $k++
        $cell = Join-Path $dir "painting-$k.png"
        $image = [Drawing.Image]::FromFile($painting)
        $w = $image.Width; $h = [int][Math]::Round($w * 9 / 16)
        if ($h -gt $image.Height) { $h = $image.Height; $w = [int][Math]::Round($h * 16 / 9) }
        $bitmap = New-Object Drawing.Bitmap $CellWidth, $height
        $g = [Drawing.Graphics]::FromImage($bitmap)
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.DrawImage($image, (New-Object Drawing.Rectangle 0, 0, $CellWidth, $height), (New-Object Drawing.Rectangle (($image.Width - $w) / 2), (($image.Height - $h) / 2), $w, $h), 'Pixel')
        $bitmap.Save($cell, [Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bitmap.Dispose(); $image.Dispose()
        $tiles += $cell; $labels += "the user's painting $k"
    }
    # The paintings are each of one dominant peak: beside them the two snow-capped picks (the seeds sheet's) with the best peak score.
    New-Item -ItemType Directory -Force (Join-Path $Out 'seeds') | Out-Null
    foreach ($s in $Seeds.Split(',')) {
        $png = Join-Path $Out "seeds\peak-$s.png"
        if (-not (Test-Path $png)) { $null = Auto $peak $s $png }
    }
    $best = $Seeds.Split(',') | Sort-Object { -(Sidecar (Join-Path $Out "seeds\peak-$_.png")).autoCamera.pick.scores.peak } | Select-Object -First 2
    foreach ($s in $best) {
        $png = Join-Path $Out "seeds\peak-$s.png"
        $a = (Sidecar $png).autoCamera
        $tiles += $png
        $labels += ('seed {0} · the auto camera''s pick · {1:0} mm · peak score {2:0.00}' -f $s, $a.pick.camera.focalLengthMm, $a.pick.scores.peak)
    }
    Sheet $tiles $labels 2 'M1.14 the user''s paintings beside the auto camera''s picks · judged on composition, not colour or style' 'paintings.png'
}

# The same pick twice.
if ($make -contains 'repeat') {
    $dir = Join-Path $Out 'repeat'
    New-Item -ItemType Directory -Force $dir | Out-Null
    $first = Join-Path $dir 'first.png'; $second = Join-Path $dir 'second.png'
    $null = Auto $peak '42' $first
    $null = Auto $peak '42' $second
    $same = (Get-FileHash ([IO.Path]::ChangeExtension($first, '.camera.json'))).Hash -eq (Get-FileHash ([IO.Path]::ChangeExtension($second, '.camera.json'))).Hash
    $image = (Get-FileHash $first).Hash -eq (Get-FileHash $second).Hash
    Say "repeat: the pick's camera file the same twice: $same; the image the same twice: $image"
}
