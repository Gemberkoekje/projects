# The M1.10a exposure and display checks (vista-plan section 10, M1.10a) on the local GPU: the four measurement views, a
# golden-hour view and the alpenglow sweep (+2 to -1.5 degrees) under {average, incident} x {ACES, AgX}. Each view's four PNGs
# come from one EXR (rendered by two sheets, one per meter, compared byte for byte); the script prints each variant's exposure and
# how much of its frame clips. With -Before (a main build's Vista.App.dll) it renders the same views with main, which must be
# byte-identical to this build's --meter average --display aces (PNG and EXR), and tiles the before/after sheets. Build first:
# dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-exposure'),
    [string]$App = (Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll'),
    [string]$Before = '',
    [int]$Width = 480,
    [int]$SweepWidth = 320,
    [int]$Spp = 64
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null
$views = Split-Path $PSScriptRoot -Parent
$day = Join-Path $views 'mountains-day.recipe.json'
$golden = Join-Path $views 'mountains-golden.recipe.json'
$july = Join-Path $views 'atmosphere\zermatt-july.recipe.json'
$range = Join-Path $views 'atmosphere\range.camera.json'
$elevations = '2,1,0,-0.5,-1,-1.5'

$list = @(
    @{ Name = 'sky'; Recipe = $day; Camera = (Join-Path $views 'sky.camera.json') },
    @{ Name = 'aerial'; Recipe = $day; Camera = (Join-Path $views 'aerial.camera.json') },
    @{ Name = 'default'; Recipe = $day; Camera = (Join-Path $views 'default.camera.json') },
    @{ Name = 'down-slope'; Recipe = $golden; Camera = (Join-Path $views 'down-slope.camera.json') },
    @{ Name = 'ridges-golden'; Recipe = $golden; Camera = (Join-Path $views 'aerial\layered-ridges.camera.json') }
)

function Run([string]$dll, [string[]]$arguments) {
    & dotnet $dll @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Vista.App $($arguments -join ' ') failed." }
}

function Hash([string]$path) { (Get-FileHash -Algorithm SHA256 $path).Hash }

# The share of a PNG's pixels with a channel at 255 (clipped), and the brightest tenth of its pixels (by their largest channel):
# their mean largest channel (0-255) and mean saturation, (max - min) / max. A sunlit face washed out to cream has a high one and
# a low other.
function Clipping([string]$png) {
    $bitmap = [System.Drawing.Bitmap]::FromFile($png)
    try {
        $rect = New-Object System.Drawing.Rectangle(0, 0, $bitmap.Width, $bitmap.Height)
        $data = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
        $bytes = New-Object byte[] ($data.Stride * $bitmap.Height)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
        $bitmap.UnlockBits($data)
        $pixels = $bitmap.Width * $bitmap.Height
        $top = New-Object double[] $pixels
        $saturation = New-Object double[] $pixels
        $clipped = 0; $p = 0
        for ($y = 0; $y -lt $bitmap.Height; $y++) {
            $row = $y * $data.Stride
            for ($x = 0; $x -lt $bitmap.Width; $x++) {
                $i = $row + 3 * $x
                $b = $bytes[$i]; $g = $bytes[$i + 1]; $r = $bytes[$i + 2]
                $max = [Math]::Max($r, [Math]::Max($g, $b)); $min = [Math]::Min($r, [Math]::Min($g, $b))
                if ($max -eq 255) { $clipped++ }
                $top[$p] = $max
                $saturation[$p] = $(if ($max -gt 0) { ($max - $min) / $max } else { 0.0 })
                $p++
            }
        }
        [Array]::Sort($top, $saturation)
        $first = [int]($pixels * 0.9)
        $brightness = 0.0; $chroma = 0.0
        for ($k = $first; $k -lt $pixels; $k++) { $brightness += $top[$k]; $chroma += $saturation[$k] }
        $n = $pixels - $first
        return @{ Clipped = $clipped / $pixels; Bright = $brightness / $n; Saturation = $chroma / $n }
    } finally { $bitmap.Dispose() }
}

function Describe([string]$json) {
    $s = Get-Content $json -Raw | ConvertFrom-Json
    $e = $s.exposure
    if ($e.meter -eq 'incident') {
        $reading = 'card {0:N0} lux (sun {1:N0}, sky {2:N0}) at {3:0.0} deg' -f $e.incident.illuminanceLux, $e.incident.sunLux, $e.incident.skyLux, $e.incident.cardElevationDegrees
    } else {
        $reading = 'frame {0:G3} cd/m2' -f $e.average.logAverageLuminanceCdM2
    }
    return @{ Ev = [double]$e.appliedEv; Reading = $reading }
}

# One view under the four variants: a sheet per meter varying the display curve; the four cells must share one EXR.
function Variants([string]$name, [string[]]$view, [int]$width) {
    $cells = @()
    foreach ($meter in 'average', 'incident') {
        $sheet = Join-Path $Out "$name-$meter.png"
        Run $App (@('--sheet', '--seed', '42', '--width', $width, '--spp', $Spp, '--meter', $meter, '--vary', 'display=aces,agx', '--columns', '2', '--out', $sheet) + $view)
        $cells += (Join-Path $Out "$name-$meter.cells\cell01.png"), (Join-Path $Out "$name-$meter.cells\cell02.png")
    }
    $exrs = $cells | ForEach-Object { Hash ([IO.Path]::ChangeExtension($_, '.exr')) } | Select-Object -Unique
    if (@($exrs).Count -ne 1) { throw "${name}: the four variants do not share one EXR." }
    $labels = @('average + ACES', 'average + AgX', 'incident + ACES', 'incident + AgX')
    Run $App (@('--sheet-from') + $cells + @('--labels', ($labels -join ','), '--columns', '2', '--title', "$name, seed 42", '--out', (Join-Path $Out "$name-variants.png")))
    for ($i = 0; $i -lt 4; $i++) {
        $d = Describe ([IO.Path]::ChangeExtension($cells[$i], '.json'))
        $c = Clipping $cells[$i]
        Write-Host ('  {0,-14} {1,-16} {2,6:+0.00;-0.00} EV  clipped {3,6:P2}  brightest tenth {4,5:0} at saturation {5:0.00}  {6}' -f
            $name, $labels[$i], $d.Ev, $c.Clipped, $c.Bright, $c.Saturation, $d.Reading)
    }
    # Everything a function does not capture is its output: the lines above go to the host, so only the cells are returned.
    return $cells
}

'Variants (one EXR each; exposure, clipping, the meter''s reading):'
$after = @{}
foreach ($v in $list) {
    $cells = Variants $v.Name @('--recipe', $v.Recipe, '--camera', $v.Camera) $Width
    $after[$v.Name] = $cells
}

'Alpenglow sweep (zermatt-july, range camera, sun at +2 to -1.5 deg):'
$sweep = @()
for ($k = 0; $k -lt 6; $k++) { $sweep += , @() }
$values = $elevations.Split(',')
foreach ($meter in 'average', 'incident') {
    foreach ($display in 'aces', 'agx') {
        $sheet = Join-Path $Out "alpenglow-$meter-$display.png"
        Run $App @('--sheet', '--recipe', $july, '--camera', $range, '--seed', '42', '--width', $SweepWidth, '--spp', $Spp, '--meter', $meter,
                   '--display', $display, '--vary', "sunElevation=$elevations", '--columns', '6', '--out', $sheet)
        for ($k = 0; $k -lt 6; $k++) {
            $cell = Join-Path $Out ("alpenglow-$meter-$display.cells\cell{0:00}.png" -f ($k + 1))
            $sweep[$k] += $cell
            $d = Describe ([IO.Path]::ChangeExtension($cell, '.json'))
            $c = Clipping $cell
            '  sun {0,5} deg  {1,-9} {2,-5} {3,6:+0.00;-0.00} EV  clipped {4,6:P2}  brightest tenth {5,5:0} at saturation {6:0.00}  {7}' -f
                $values[$k], $meter, $display, $d.Ev, $c.Clipped, $c.Bright, $c.Saturation, $d.Reading
        }
    }
}
for ($k = 0; $k -lt 6; $k++) {
    $exrs = $sweep[$k] | ForEach-Object { Hash ([IO.Path]::ChangeExtension($_, '.exr')) } | Select-Object -Unique
    if (@($exrs).Count -ne 1) { throw "alpenglow at $($values[$k]) deg: the four variants do not share one EXR." }
}
'  every view''s and every elevation''s four variants share one EXR, byte for byte'
$rows = @(0..5 | ForEach-Object { $sweep[$_][0] }) + @(0..5 | ForEach-Object { $sweep[$_][3] })
$labels = @($values | ForEach-Object { "before $_ deg" }) + @($values | ForEach-Object { "after $_ deg" })
Run $App (@('--sheet-from') + $rows + @('--labels', ($labels -join ','), '--columns', '6', '--title', 'alpenglow: average + ACES (top), incident + AgX (bottom)',
           '--out', (Join-Path $Out 'alpenglow-before-after.png')))

'Dawn and dusk from the default camera (the sun under the horizon): the incident card is lit by the sky alone:'
$dawnDusk = @()
foreach ($meter in 'average', 'incident') {
    $sheet = Join-Path $Out "twilight-$meter.png"
    Run $App @('--sheet', '--recipe', $day, '--camera', (Join-Path $views 'default.camera.json'), '--seed', '42', '--width', $Width, '--spp', $Spp,
               '--meter', $meter, '--vary', 'time=dawn,dusk', '--columns', '2', '--out', $sheet)
    foreach ($k in 1, 2) {
        $cell = Join-Path $Out ("twilight-$meter.cells\cell{0:00}.png" -f $k)
        $dawnDusk += $cell
        $s = Get-Content ([IO.Path]::ChangeExtension($cell, '.json')) -Raw | ConvertFrom-Json
        $d = Describe ([IO.Path]::ChangeExtension($cell, '.json'))
        $c = Clipping $cell
        '  {0,-5} sun {1,6:0.00} deg  {2,-9} {3,6:+0.00;-0.00} EV  brightest tenth {4,5:0} at saturation {5:0.00}  {6}' -f
            $(if ($k -eq 1) { 'dawn' } else { 'dusk' }), $s.sun.elevationDegrees, $meter, $d.Ev, $c.Bright, $c.Saturation, $d.Reading
    }
}
Run $App (@('--sheet-from') + $dawnDusk + @('--labels', 'dawn: average + AgX,dusk: average + AgX,dawn: incident + AgX,dusk: incident + AgX', '--columns', '2',
           '--out', (Join-Path $Out 'twilight.png')))

if ($Before -ne '') {
    'Main (-Before) against this build''s --meter average --display aces:'
    $pairs = @()
    $labels = @()
    foreach ($v in $list) {
        $png = Join-Path $Out "main-$($v.Name).png"
        Run $Before @('--preview', '--recipe', $v.Recipe, '--camera', $v.Camera, '--seed', '42', '--width', $Width, '--spp', $Spp, '--out', $png)
        $old = $after[$v.Name][0]
        foreach ($ext in '.png', '.exr') {
            if ((Hash ([IO.Path]::ChangeExtension($png, $ext))) -ne (Hash ([IO.Path]::ChangeExtension($old, $ext)))) {
                throw "$($v.Name): main's $ext differs from --meter average --display aces."
            }
        }
        '  {0,-14} PNG and EXR byte-identical' -f $v.Name
        $pairs += $png, $after[$v.Name][3]
        $labels += "before: $($v.Name)", "after: $($v.Name)"
    }
    Run $App (@('--sheet-from') + $pairs + @('--labels', ($labels -join ','), '--columns', '2',
               '--title', 'before: main (average + ACES) | after: M1.10a (incident + AgX)', '--out', (Join-Path $Out 'before-after.png')))
}
"Sheets in $Out"
