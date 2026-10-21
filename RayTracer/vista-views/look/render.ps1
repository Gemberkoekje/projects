# The M1.11b look sheet (vista-plan section 10, M1.11b) on the local GPU: {AgX base, AgX punchy} x {0, +0.5, +1 EV} of the
# incident meter's own compensation, on the four measurement views, the default view at golden hour (+2 deg), the layered ridges
# in October at +2 deg and the alpenglow sweep (+2 to -1.5 deg). Each view's six PNGs come from one EXR (two sheets, one per look,
# the compensation their axis; compared byte for byte). The script prints each variant's exposure, its mean and median (the
# largest channel, of 255), and its brightest tenth with its saturation. With -Before (a main build's Vista.App.dll) it renders
# the four measurement views with main, which must be byte-identical to this build's --look base --compensation 0 (PNG and EXR).
# Build first: dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-look'),
    [string]$App = (Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll'),
    [string]$Before = '',
    [int]$Width = 480,
    [int]$SweepWidth = 320,
    [int]$Spp = 64,
    [string]$Compensations = '0,0.5,1'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null
$views = Split-Path $PSScriptRoot -Parent
$day = Join-Path $views 'mountains-day.recipe.json'
$golden = Join-Path $views 'mountains-golden.recipe.json'
$october = Join-Path $views 'atmosphere\zermatt-october.recipe.json'
$july = Join-Path $views 'atmosphere\zermatt-july.recipe.json'
$range = Join-Path $views 'atmosphere\range.camera.json'
$ridges = Join-Path $views 'aerial\layered-ridges.camera.json'
$elevations = '2,1,0,-0.5,-1,-1.5'
$looks = 'base', 'punchy'
$steps = $Compensations.Split(',')

$list = @(
    @{ Name = 'sky'; View = @('--recipe', $day, '--camera', (Join-Path $views 'sky.camera.json')) },
    @{ Name = 'aerial'; View = @('--recipe', $day, '--camera', (Join-Path $views 'aerial.camera.json')) },
    @{ Name = 'default'; View = @('--recipe', $day, '--camera', (Join-Path $views 'default.camera.json')) },
    @{ Name = 'down-slope'; View = @('--recipe', $golden, '--camera', (Join-Path $views 'down-slope.camera.json')) },
    @{ Name = 'default-golden'; View = @('--recipe', $golden, '--camera', (Join-Path $views 'default.camera.json')) },
    @{ Name = 'ridges-october'; View = @('--recipe', $october, '--camera', $ridges, '--sun-elevation', '2') }
)

function Run([string]$dll, [string[]]$arguments) {
    & dotnet $dll @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Vista.App $($arguments -join ' ') failed." }
}

function Hash([string]$path) { (Get-FileHash -Algorithm SHA256 $path).Hash }

function Signed([string]$ev) { if ($ev.StartsWith('-')) { $ev } else { "+$ev" } }

# A PNG's tones by each pixel's largest channel (0-255): the frame's mean and median, and its brightest tenth's mean with that
# tenth's mean saturation, (max - min) / max. A dark, flat image has a low mean and a pale brightest tenth.
function Tones([string]$png) {
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
        $sum = 0.0; $chromaAll = 0.0; $p = 0
        for ($y = 0; $y -lt $bitmap.Height; $y++) {
            $row = $y * $data.Stride
            for ($x = 0; $x -lt $bitmap.Width; $x++) {
                $i = $row + 3 * $x
                $b = $bytes[$i]; $g = $bytes[$i + 1]; $r = $bytes[$i + 2]
                $max = [Math]::Max($r, [Math]::Max($g, $b)); $min = [Math]::Min($r, [Math]::Min($g, $b))
                $top[$p] = $max
                $saturation[$p] = $(if ($max -gt 0) { ($max - $min) / $max } else { 0.0 })
                $sum += $max; $chromaAll += $saturation[$p]
                $p++
            }
        }
        [Array]::Sort($top, $saturation)
        $first = [int]($pixels * 0.9)
        $brightness = 0.0; $chroma = 0.0
        for ($k = $first; $k -lt $pixels; $k++) { $brightness += $top[$k]; $chroma += $saturation[$k] }
        $n = $pixels - $first
        return @{ Mean = $sum / $pixels; Median = $top[[int]($pixels / 2)]; Saturation = $chromaAll / $pixels; Bright = $brightness / $n; BrightSaturation = $chroma / $n }
    } finally { $bitmap.Dispose() }
}

function Report([string]$name, [string]$label, [string]$cell) {
    $s = Get-Content ([IO.Path]::ChangeExtension($cell, '.json')) -Raw | ConvertFrom-Json
    $t = Tones $cell
    Write-Host ('  {0,-16} {1,-12} {2,6:+0.00;-0.00} EV  mean {3,5:0.0}  median {4,3:0}  saturation {5:0.00}  brightest tenth {6,5:0.0} at {7:0.00}' -f
        $name, $label, [double]$s.exposure.appliedEv, $t.Mean, $t.Median, $t.Saturation, $t.Bright, $t.BrightSaturation)
}

# One view under the six variants: a sheet per look varying the compensation; the six cells must share one EXR.
function Variants([string]$name, [string[]]$view) {
    $cells = @()
    $labels = @()
    foreach ($look in $looks) {
        $sheet = Join-Path $Out "$name-$look.png"
        Run $App (@('--sheet', '--seed', '42', '--width', $Width, '--spp', $Spp, '--look', $look, '--vary', "compensation=$Compensations",
                    '--columns', $steps.Count, '--out', $sheet) + $view)
        for ($k = 0; $k -lt $steps.Count; $k++) {
            $cells += Join-Path $Out ("$name-$look.cells\cell{0:00}.png" -f ($k + 1))
            $labels += "$look $(Signed $steps[$k]) EV"
        }
    }
    $exrs = $cells | ForEach-Object { Hash ([IO.Path]::ChangeExtension($_, '.exr')) } | Select-Object -Unique
    if (@($exrs).Count -ne 1) { throw "${name}: the variants do not share one EXR." }
    Run $App (@('--sheet-from') + $cells + @('--labels', ($labels -join ','), '--columns', $steps.Count, '--title', "$name, seed 42: AgX base (top), punchy (bottom)",
               '--out', (Join-Path $Out "$name-looks.png")))
    for ($i = 0; $i -lt $cells.Count; $i++) { Report $name $labels[$i] $cells[$i] }
    # Everything a function does not capture is its output: the lines above go to the host, so only the cells are returned.
    return $cells
}

'Variants (one EXR each; the exposure applied, the tones):'
$after = @{}
foreach ($v in $list) { $after[$v.Name] = Variants $v.Name $v.View }

'Alpenglow sweep (zermatt-july, range camera, sun at +2 to -1.5 deg):'
$values = $elevations.Split(',')
$sweep = @()
$sweepLabels = @()
foreach ($look in $looks) {
    foreach ($ev in $steps) {
        $sheet = Join-Path $Out "alpenglow-$look-$ev.png"
        Run $App @('--sheet', '--recipe', $july, '--camera', $range, '--seed', '42', '--width', $SweepWidth, '--spp', $Spp, '--look', $look,
                   '--compensation', $ev, '--vary', "sunElevation=$elevations", '--columns', '6', '--out', $sheet)
        for ($k = 0; $k -lt 6; $k++) {
            $cell = Join-Path $Out ("alpenglow-$look-$ev.cells\cell{0:00}.png" -f ($k + 1))
            $sweep += $cell
            $sweepLabels += "$look $(Signed $ev): $($values[$k]) deg"
            Report "sun $($values[$k]) deg" "$look $(Signed $ev)" $cell
        }
    }
}
Run $App (@('--sheet-from') + $sweep + @('--labels', ($sweepLabels -join ','), '--columns', '6',
           '--title', 'alpenglow: AgX base then punchy, each at +0, +0.5, +1 EV (rows)', '--out', (Join-Path $Out 'alpenglow-looks.png')))

if ($Before -ne '') {
    'Main (-Before) against this build''s --look base --compensation 0:'
    foreach ($v in $list | Select-Object -First 4) {
        $png = Join-Path $Out "main-$($v.Name).png"
        Run $Before (@('--preview', '--seed', '42', '--width', $Width, '--spp', $Spp, '--out', $png) + $v.View)
        $old = $after[$v.Name][0]
        foreach ($ext in '.png', '.exr') {
            if ((Hash ([IO.Path]::ChangeExtension($png, $ext))) -ne (Hash ([IO.Path]::ChangeExtension($old, $ext)))) {
                throw "$($v.Name): main's $ext differs from --look base --compensation 0."
            }
        }
        '  {0,-14} PNG and EXR byte-identical' -f $v.Name
    }
}
"Sheets in $Out"
