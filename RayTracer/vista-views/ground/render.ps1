# The M1a.1 map (vista-plan section 10, M1a) on the local GPU: where each of the four things that make M1's hero read as a model comes
# from, on the hero's own frame (an alpine range at golden hour, seed 42, signoff/hero.camera.json), as sheets of at most 640 px a cell:
#  - map.png: the hero at 640 px with the four places boxed and numbered: 1 the grooves and planes (the lit middle range), 2 the rows
#    of dots, 3 the marbled beds (the shaded foreground), 4 the felt meadows (on a lit crest);
#  - grooves.png, dots.png, beds.png, meadows.png: each place as a loupe of the 4K frame, pixel for pixel (the beauty, denoised, beside
#    the material ids, the albedo or the normals, whichever shows the cause; the shaded places 2 EV brighter, to see into the shade),
#    and for the grooves and the dots the ground under the loupe from above as the world is made: the base terrain, the incision
#    brought back to the 8 m grid, and after the pipe-model passes (Vista.App --erosion-measure --crops). Its log, measure.log, holds
#    the grid's own patterns at each stage (GridArtefacts) and the metres a pixel covers at each place.
# Build first: dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-ground'),
    [string]$App = '',
    [int]$Spp = 64,
    [string]$Sheets = 'map,grooves,dots,beds,meadows'
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell leaves $PSScriptRoot empty in the parameters' defaults when another parameter is given.
if ($App -eq '') { $App = Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll' }
Add-Type -AssemblyName System.Drawing
[System.Threading.Thread]::CurrentThread.CurrentCulture = [cultureinfo]::InvariantCulture
New-Item -ItemType Directory -Force $Out | Out-Null
$views = Split-Path $PSScriptRoot -Parent
$range = Join-Path $views 'autocam\range-golden.recipe.json'
$hero = Join-Path $views 'signoff\hero.camera.json'
$make = $Sheets.Split(',')
$heroArgs = @('--recipe', $range, '--seed', '42', '--camera', $hero)

function Run([string[]]$arguments) {
    & dotnet $App @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Vista.App $($arguments -join ' ') failed." }
}

function Sheet([string[]]$tiles, [string[]]$labels, [int]$columns, [string]$title, [string]$name) {
    Run (@('--sheet-from') + $tiles + @('--columns', "$columns", '--labels', ($labels -join ','), '--title', $title, '--out', (Join-Path $Out $name)))
}

# The four places: a 640x360 loupe of the 3840x2160 frame (its top-left corner), the frame pixel the crops are taken under, and
# whether it lies in shade (shown 2 EV brighter).
$places = [ordered]@{
    grooves = @{ X = 1680; Y = 670; Pixel = '2000,850'; Shade = $false; Number = 1 }
    dots    = @{ X = 1060; Y = 1080; Pixel = '1300,1250'; Shade = $true; Number = 2 }
    beds    = @{ X = 0; Y = 1620; Pixel = '300,1800'; Shade = $true; Number = 3 }
    meadows = @{ X = 640; Y = 650; Pixel = '1060,880'; Shade = $false; Number = 4 }
}

# A loupe of the hero at 4K, denoised, with its material ids, albedo and normals; returns the stem of its files.
function Loupe([string]$name) {
    $p = $places[$name]
    $stem = Join-Path $Out "loupe-$name"
    $extra = @()
    if ($p.Shade) { $extra = @('--compensation', '3.5') }
    Run (@('--preview') + $heroArgs + @('--width', '3840', '--loupe', "$($p.X),$($p.Y),640,360", '--spp', "$Spp", '--denoise', '--denoise-device', 'cuda',
        '--debug', 'albedo,normal,material', '--out', "$stem.png") + $extra)
    return $stem
}

# The crops under the four places (the sheets show the grooves' and the dots'), each place's distance, and the grid's patterns at
# each stage of the world (two minutes: the incision and the erosion run again).
$measure = Join-Path $Out 'measure'
if (($make -contains 'grooves') -or ($make -contains 'dots')) {
    New-Item -ItemType Directory -Force $measure | Out-Null
    $pixels = ($places.Values | ForEach-Object { $_.Pixel }) -join ';'
    & dotnet $App --erosion-measure --recipe $range --seeds 42 --out $measure --crops-camera $hero --crops $pixels |
        Out-File -FilePath (Join-Path $Out 'measure.log') -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw 'Vista.App --erosion-measure failed.' }
}

if ($make -contains 'map') {
    $stem = Join-Path $Out 'hero'
    Run (@('--preview') + $heroArgs + @('--width', '640', '--spp', "$Spp", '--denoise', '--denoise-device', 'cuda', '--out', "$stem.png"))
    # The four loupes boxed and numbered on the 640 px frame (a sixth of the 4K frame's pixels a side).
    $bitmap = New-Object System.Drawing.Bitmap "$stem.denoised.png"
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 255, 220, 40)), 2
    $font = New-Object System.Drawing.Font 'Consolas', 13, ([System.Drawing.FontStyle]::Bold)
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 220, 40))
    foreach ($p in $places.Values) {
        $g.DrawRectangle($pen, [int]($p.X / 6), [int]($p.Y / 6), [int](640 / 6), [int](360 / 6))
        $g.DrawString("$($p.Number)", $font, $brush, [float]($p.X / 6 + 3), [float]($p.Y / 6 + 1))
    }
    $g.Dispose()
    $boxed = Join-Path $Out 'hero-boxed.png'
    $bitmap.Save($boxed, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    Sheet @($boxed) @('1 grooves and planes · 2 rows of dots · 3 marbled beds · 4 felt meadows · each box a 640x360 loupe of the 4K frame') 1 `
        'M1a.1 the map: the hero (seed 42 · golden hour · M1''s camera) and the four places that read as a model' 'm1a-1-map.png'
}

if ($make -contains 'grooves') {
    $l = Loupe 'grooves'
    Sheet @("$l.denoised.png", "$l.material.png", "$l.normal.png",
        (Join-Path $measure 'seed42.crop1.base.png'), (Join-Path $measure 'seed42.crop1.incised.png'), (Join-Path $measure 'seed42.crop1.eroded.png')) `
        @('1 the lit middle range 3.8 km off · 1.2 m a pixel (the 8 m cell 6.6 px)', 'materials: hard rock pale · soft rock dark · scree tan', 'normals: straight parallel grooves down every face',
          'from above (768 m · north up): the base terrain', 'the 64 m incision brought back to 8 m: flat facets and straight seams', 'after the pipe model: a comb of rills down each facet') 3 `
        'M1a.1 grooves and planes: the incision''s 64 m facets combed by the pipe model''s rills' 'm1a-1-grooves.png'
}

if ($make -contains 'dots') {
    $l = Loupe 'dots'
    Sheet @("$l.denoised.png", "$l.material.png", "$l.normal.png",
        (Join-Path $measure 'seed42.crop2.base.png'), (Join-Path $measure 'seed42.crop2.incised.png'), (Join-Path $measure 'seed42.crop2.eroded.png')) `
        @('2 rows of dots 3.4 km off · +2 EV to see into the shade', 'materials: scree · meadow · forest by turns in rows', 'normals: a lattice of dimples under the dots',
          'from above (768 m · north up): the base terrain', 'the incision brought back: a flat gentle facet', 'after the pipe model: a washboard 2-4 cells apart') 3 `
        'M1a.1 rows of dots: the pipe model''s washboard on gentle loose ground, cut by the slope rules' 'm1a-1-dots.png'
}

if ($make -contains 'beds') {
    $l = Loupe 'beds'
    Sheet @("$l.denoised.png", "$l.albedo.png", "$l.material.png", "$l.normal.png") `
        @('3 marbled beds 1.5 km off · 0.5 m a pixel · +2 EV', 'albedo: the bands are colour', 'materials: hard and soft beds by height', 'normals: no step at any band') 2 `
        'M1a.1 marbled beds: the beds'' colour painted by height on smooth slopes, with no relief of their own' 'm1a-1-beds.png'
}

if ($make -contains 'meadows') {
    $l = Loupe 'meadows'
    Sheet @("$l.denoised.png", "$l.albedo.png", "$l.material.png") `
        @('4 felt meadows on a crest 3.9 km off · 1.2 m a pixel', 'albedo: flat green with clean cut edges', 'materials: meadow where the 8 m cells are gentle') 3 `
        'M1a.1 felt meadows: the 8 m rules sharpened to a hard edge and no texture at this distance' 'm1a-1-meadows.png'
}

Get-ChildItem (Join-Path $Out 'm1a-1-*.png') | ForEach-Object { '{0} {1:0} KB' -f $_.Name, ($_.Length / 1KB) }
