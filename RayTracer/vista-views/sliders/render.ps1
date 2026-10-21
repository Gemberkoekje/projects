# The M1.11 linked-slider checks (vista-plan section 10, M1.11) on the local GPU:
#  - the golden-hour height sheet: three views at Zermatt in July (and the layered ridges in October) with the setting sun at
#    +2, +4 and +6 degrees, printing each cell's sun, the reddening of its sunlight and how warm and saturated its brightest tenth is;
#  - the month sheet: Zermatt at solar noon on the 15th of every month from the aerial camera, every slider auto but the date, printing
#    the season, the sun, the temperature, humidity, haze and snowline the chain chose;
#  - with -Before (a main build's Vista.App.dll): the four measurement views and the layered ridges at golden hour, main against this
#    build, tiled as a before/after.
# Build first: dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-sliders'),
    [string]$App = (Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll'),
    [string]$Before = '',
    [int]$Width = 480,
    [int]$Spp = 64
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null
$views = Split-Path $PSScriptRoot -Parent
$day = Join-Path $views 'mountains-day.recipe.json'
$golden = Join-Path $views 'mountains-golden.recipe.json'
$october = Join-Path $views 'atmosphere\zermatt-october.recipe.json'
$heights = '2,4,6'

function Run([string]$dll, [string[]]$arguments) {
    & dotnet $dll @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Vista.App $($arguments -join ' ') failed." }
}

# The brightest tenth of a PNG's pixels (by their largest channel): its mean red, green and blue (0-255), its warmth (mean red over
# mean blue) and its mean saturation, (max - min) / max.
function Brightest([string]$png) {
    $bitmap = [System.Drawing.Bitmap]::FromFile($png)
    try {
        $rect = New-Object System.Drawing.Rectangle(0, 0, $bitmap.Width, $bitmap.Height)
        $data = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
        $bytes = New-Object byte[] ($data.Stride * $bitmap.Height)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
        $bitmap.UnlockBits($data)
        $pixels = $bitmap.Width * $bitmap.Height
        $key = New-Object double[] $pixels
        $index = New-Object int[] $pixels
        $p = 0
        for ($y = 0; $y -lt $bitmap.Height; $y++) {
            $row = $y * $data.Stride
            for ($x = 0; $x -lt $bitmap.Width; $x++) {
                $i = $row + 3 * $x
                $key[$p] = [Math]::Max($bytes[$i + 2], [Math]::Max($bytes[$i + 1], $bytes[$i]))
                $index[$p] = $i
                $p++
            }
        }
        [Array]::Sort($key, $index)
        $first = [int]($pixels * 0.9)
        $r = 0.0; $g = 0.0; $b = 0.0; $s = 0.0
        for ($k = $first; $k -lt $pixels; $k++) {
            $i = $index[$k]
            $bb = $bytes[$i]; $gg = $bytes[$i + 1]; $rr = $bytes[$i + 2]
            $r += $rr; $g += $gg; $b += $bb
            $max = [Math]::Max($rr, [Math]::Max($gg, $bb)); $min = [Math]::Min($rr, [Math]::Min($gg, $bb))
            if ($max -gt 0) { $s += ($max - $min) / $max }
        }
        $n = $pixels - $first
        return @{ R = $r / $n; G = $g / $n; B = $b / $n; Warmth = $r / [Math]::Max($b, 1.0); Saturation = $s / $n }
    } finally { $bitmap.Dispose() }
}

'Golden-hour height sheet (the setting sun at +2, +4, +6 deg):'
$rows = @(
    @{ Name = 'down-slope July'; Recipe = $golden; Camera = (Join-Path $views 'down-slope.camera.json') },
    @{ Name = 'ridges July'; Recipe = $golden; Camera = (Join-Path $views 'aerial\layered-ridges.camera.json') },
    @{ Name = 'default July'; Recipe = $golden; Camera = (Join-Path $views 'default.camera.json') },
    @{ Name = 'ridges October'; Recipe = $october; Camera = (Join-Path $views 'aerial\layered-ridges.camera.json') }
)
$cells = @()
$labels = @()
$k = 0
foreach ($row in $rows) {
    $k++
    $sheet = Join-Path $Out "height-$k.png"
    Run $App @('--sheet', '--recipe', $row.Recipe, '--camera', $row.Camera, '--seed', '42', '--width', $Width, '--spp', $Spp,
               '--time', 'goldenHour', '--vary', "sunElevation=$heights", '--columns', '3', '--out', $sheet)
    $h = $heights.Split(',')
    for ($c = 1; $c -le 3; $c++) {
        $cell = Join-Path $Out ("height-$k.cells\cell{0:00}.png" -f $c)
        $s = Get-Content ([IO.Path]::ChangeExtension($cell, '.json')) -Raw | ConvertFrom-Json
        $t = $s.sun.transmittanceToCamera
        $m = Brightest $cell
        '  {0,-20} +{1} deg  {2} solar, azimuth {3,5:0.0}  sunlight 650/450 nm {4,5:0.00}  exposure {5,6:+0.00;-0.00} EV  brightest tenth rgb {6,3:0} {7,3:0} {8,3:0}  warmth {9:0.00}  saturation {10:0.00}' -f
            $row.Name, $h[$c - 1], $s.sun.solarTime, $s.sun.azimuthDegrees, ($t.at650Nm / [Math]::Max($t.at450Nm, 1e-9)), $s.exposure.appliedEv,
            $m.R, $m.G, $m.B, $m.Warmth, $m.Saturation
        $cells += $cell
        $labels += "$($row.Name) +$($h[$c - 1]) deg"
    }
}
Run $App (@('--sheet-from') + $cells + @('--labels', ($labels -join ','), '--columns', '3',
           '--title', 'golden hour by sun height: the setting sun at +2, +4, +6 deg, Zermatt, seed 42', '--out', (Join-Path $Out 'golden-hour-heights.png')))

'Month sheet (Zermatt, solar noon, the 15th of each month; every slider auto but the date):'
$pieces = Join-Path $Out 'mountains.recipe.json'
Set-Content -Path $pieces -Value '{ "schema": 4, "name": "snow-capped mountains", "setPieces": ["snowCappedMountains"] }' -Encoding utf8
$dates = (1..12 | ForEach-Object { '2026-{0:00}-15' -f $_ }) -join ','
$sheet = Join-Path $Out 'months.png'
Run $App @('--sheet', '--recipe', $pieces, '--camera', (Join-Path $views 'aerial.camera.json'), '--seed', '42', '--width', $Width, '--spp', $Spp,
           '--location', 'zermatt', '--time', 'day', '--vary', "date=$dates", '--columns', '4', '--out', $sheet)
for ($c = 1; $c -le 12; $c++) {
    $s = Get-Content (Join-Path $Out ("months.cells\cell{0:00}.json" -f $c)) -Raw | ConvertFrom-Json
    $v = $s.sliders.values
    '  {0}  {1,-6} sun {2,5:0.0} deg  {3,6:0.0} C  {4,4:0}% (day {5,3:0}%)  haze {6:0.000}  visibility {7,5:0} km  snowline {8,5:0} m  exposure {9,6:+0.00;-0.00} EV' -f
        $v.date.value, $v.season.value, $s.sun.elevationDegrees, $v.temperature.number, $v.humidity.number, $s.sliders.weather.hazeHumidityPercent,
        $s.sliders.weather.aerosolOpticalDepth550, $v.haze.number, $v.snow.number, $s.exposure.appliedEv
}

if ($Before -ne '') {
    'Main (-Before) against this build: the measurement views and the layered ridges at golden hour:'
    $list = @(
        @{ Name = 'sky'; Recipe = $day; Camera = (Join-Path $views 'sky.camera.json') },
        @{ Name = 'aerial'; Recipe = $day; Camera = (Join-Path $views 'aerial.camera.json') },
        @{ Name = 'default'; Recipe = $day; Camera = (Join-Path $views 'default.camera.json') },
        @{ Name = 'down-slope'; Recipe = $golden; Camera = (Join-Path $views 'down-slope.camera.json') },
        @{ Name = 'ridges-golden'; Recipe = $golden; Camera = (Join-Path $views 'aerial\layered-ridges.camera.json') }
    )
    $pairs = @()
    $names = @()
    foreach ($v in $list) {
        # Not $before: PowerShell's names ignore case, and $Before is main's build.
        $mainPng = Join-Path $Out "main-$($v.Name).png"
        $branchPng = Join-Path $Out "branch-$($v.Name).png"
        Run $Before @('--preview', '--recipe', $v.Recipe, '--camera', $v.Camera, '--seed', '42', '--width', $Width, '--spp', $Spp, '--out', $mainPng)
        Run $App @('--preview', '--recipe', $v.Recipe, '--camera', $v.Camera, '--seed', '42', '--width', $Width, '--spp', $Spp, '--out', $branchPng)
        $a = Get-Content ([IO.Path]::ChangeExtension($mainPng, '.json')) -Raw | ConvertFrom-Json
        $b = Get-Content ([IO.Path]::ChangeExtension($branchPng, '.json')) -Raw | ConvertFrom-Json
        '  {0,-14} sun {1,5:0.00} -> {2,5:0.00} deg  haze {3:0.000} -> {4:0.000}  exposure {5,6:+0.00;-0.00} -> {6,6:+0.00;-0.00} EV' -f
            $v.Name, $a.sun.elevationDegrees, $b.sun.elevationDegrees, $a.atmosphere.model.aerosolOpticalDepth550,
            $b.atmosphere.model.aerosolOpticalDepth550, $a.exposure.appliedEv, $b.exposure.appliedEv
        $pairs += $mainPng, $branchPng
        $names += "before: $($v.Name)", "after: $($v.Name)"
    }
    Run $App (@('--sheet-from') + $pairs + @('--labels', ($names -join ','), '--columns', '2',
               '--title', 'before: main | after: M1.11 (golden hour at +2 deg; haze from the day''s humidity)', '--out', (Join-Path $Out 'before-after.png')))
}
"Sheets in $Out"
