# The M1a.1 map (vista-plan section 10, M1a) on the local GPU: where each of the four things that make M1's hero read as a model comes
# from, on the hero's own frame (an alpine range at golden hour, seed 42, signoff/hero.camera.json), as sheets of at most 640 px a cell:
#  - map.png: the hero at 640 px with the four places boxed and numbered: 1 the grooves and planes (the lit middle range), 2 the rows
#    of dots, 3 the marbled beds (the shaded foreground), 4 the felt meadows (on a lit crest);
#  - grooves.png, dots.png, beds.png, meadows.png: each place as a loupe of the 4K frame, pixel for pixel (the beauty, denoised, beside
#    the material ids, the albedo or the normals, whichever shows the cause; the shaded places 2 EV brighter, to see into the shade),
#    and for the grooves and the dots the ground under the loupe from above as the world is made: the base terrain, the incision
#    brought back to the 8 m grid, and after the pipe-model passes (Vista.App --erosion-measure --crops). Its log, measure.log, holds
#    the grid's own patterns at each stage (GridArtefacts) and the metres a pixel covers at each place.
# -Sheets m1a2 makes M1a.2's sheets instead, -Sheets m1a3 M1a.3's, -Sheets m1a3a M1a.3a's, -Sheets m1a4 M1a.4's (see the end of the script).
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

# M1a.2 (-Sheets m1a2): the ground that gathers its water, beside M1's. Each world is made and measured again (--erosion-measure,
# about three minutes each): M1's with M1's grid transfer and passes (VISTA_EROSION_SETTINGS), and the shipped one. Its sheets:
#  - m1a-2-hero.png: the hero at 640 px, M1's and M1a.2's, and the auto camera's own pick on M1a.2's world;
#  - m1a-2-lit.png, m1a-2-near.png: 640x360 loupes of the 4K frame, pixel for pixel: the lit middle range (place 1; M1's, M1a.2's
#    as first landed, with its rounded summits and its ice laid smooth, and M1a.2's reworked) and the near face at the frame's right
#    (2 EV brighter: it lies in shade), M1's beside M1a.2's;
#  - m1a-2-ground.png: the ground under place 1 and place 2 from above (768 m, north up): the base terrain, M1's eroded ground,
#    M1a.2's incision brought back to the 8 m grid, and M1a.2's eroded ground.
# measure-m1.log and measure-m1a2.log hold the grid's patterns, the drainage and relief measures and the places' distances.
if ($make -contains 'm1a2') {
    $m1Erosion = 'RefineCubic=0;RefineChannelCurves=0;RefineFaceDetail=0;DischargeSpread=0;ThermalRate=0.25;CreepRate=0'
    # M1a.2 as first landed (the review's blobs): the incision brought back smoothly throughout, its summits rounded.
    $firstErosion = 'RefineCubic=1'
    $env:VISTA_EROSION_SETTINGS = $firstErosion
    try {
        Run (@('--preview') + $heroArgs + @('--width', '3840', '--loupe', "$($places.grooves.X),$($places.grooves.Y),640,360", '--spp', "$Spp", '--denoise', '--denoise-device', 'cuda',
            '--out', (Join-Path $Out 'loupe-lit-first.png')))
    }
    finally {
        $env:VISTA_EROSION_SETTINGS = ''
    }
    $near = @{ X = 3200; Y = 1500; Pixel = '3520,1680' }
    $cropPixels = "$($places.grooves.Pixel);$($places.dots.Pixel);$($near.Pixel)"
    foreach ($stage in @(@{ Name = 'm1'; Set = $m1Erosion }, @{ Name = 'm1a2'; Set = '' })) {
        $env:VISTA_EROSION_SETTINGS = $stage.Set
        try {
            $dir = Join-Path $Out "measure-$($stage.Name)"
            New-Item -ItemType Directory -Force $dir | Out-Null
            & dotnet $App --erosion-measure --recipe $range --seeds 42 --out $dir --crops-camera $hero --crops $cropPixels |
                Out-File -FilePath (Join-Path $Out "measure-$($stage.Name).log") -Encoding utf8
            if ($LASTEXITCODE -ne 0) { throw 'Vista.App --erosion-measure failed.' }
            Run (@('--preview') + $heroArgs + @('--width', '640', '--spp', "$Spp", '--denoise', '--denoise-device', 'cuda', '--out', (Join-Path $Out "hero-$($stage.Name).png")))
            if ($stage.Name -eq 'm1a2') {
                # The auto camera's own pick on M1a.2's world (M1's is the hero camera itself, vista-log/M1.17.md).
                Run (@('--preview', '--recipe', $range, '--seed', '42', '--auto-camera', '--finalists', '0', '--width', '640', '--spp', "$Spp", '--denoise', '--denoise-device', 'cuda',
                    '--out', (Join-Path $Out 'hero-auto-m1a2.png')))
            }
            foreach ($l in @(@{ Name = 'lit'; X = $places.grooves.X; Y = $places.grooves.Y; Extra = @() }, @{ Name = 'near'; X = $near.X; Y = $near.Y; Extra = @('--compensation', '3.5') })) {
                Run (@('--preview') + $heroArgs + @('--width', '3840', '--loupe', "$($l.X),$($l.Y),640,360", '--spp', "$Spp", '--denoise', '--denoise-device', 'cuda',
                    '--out', (Join-Path $Out "loupe-$($l.Name)-$($stage.Name).png")) + $l.Extra)
            }
        }
        finally {
            $env:VISTA_EROSION_SETTINGS = ''
        }
    }
    Sheet @((Join-Path $Out 'hero-m1.denoised.png'), (Join-Path $Out 'hero-m1a2.denoised.png'), (Join-Path $Out 'hero-auto-m1a2.denoised.png')) @('M1', 'M1a.2 · M1''s camera', 'M1a.2 · the auto camera''s pick on it') 3 `
        'M1a.2 the hero (seed 42 · golden hour · M1''s camera): M1''s ground and the ground that gathers its water' 'm1a-2-hero.png'
    Sheet @((Join-Path $Out 'loupe-lit-m1.denoised.png'), (Join-Path $Out 'loupe-lit-first.denoised.png'), (Join-Path $Out 'loupe-lit-m1a2.denoised.png')) `
        @('M1 · the lit middle range 3.8 km off · 1.2 m a pixel', 'M1a.2 first: summits rounded · a glacier''s smooth domes on the crest', 'M1a.2 reworked: high points pointed') 1 `
        'M1a.2 a loupe of the 4K frame pixel for pixel: the lit middle range' 'm1a-2-lit.png'
    Sheet @((Join-Path $Out 'loupe-near-m1.denoised.png'), (Join-Path $Out 'loupe-near-m1a2.denoised.png')) `
        @('M1 · the near face at the frame''s right 130 m off · 4 cm a pixel · +2 EV', 'M1a.2 · the same pixels') 2 `
        'M1a.2 a loupe of the 4K frame pixel for pixel: the near face' 'm1a-2-near.png'
    $g1 = Join-Path $Out 'measure-m1'
    $g2 = Join-Path $Out 'measure-m1a2'
    Sheet @((Join-Path $g2 'seed42.crop1.base.png'), (Join-Path $g1 'seed42.crop1.eroded.png'), (Join-Path $g2 'seed42.crop1.incised.png'), (Join-Path $g2 'seed42.crop1.eroded.png'),
        (Join-Path $g2 'seed42.crop2.base.png'), (Join-Path $g1 'seed42.crop2.eroded.png'), (Join-Path $g2 'seed42.crop2.incised.png'), (Join-Path $g2 'seed42.crop2.eroded.png')) `
        @('1 the lit middle range from above · the base terrain', 'M1: eroded · a comb of rills on 64 m facets', 'M1a.2: the incision brought back smoothly', 'M1a.2: eroded · gullies that gather the water',
          '2 the rows of dots from above · the base terrain', 'M1: eroded · a washboard', 'M1a.2: the incision brought back smoothly', 'M1a.2: eroded') 4 `
        'M1a.2 the ground from above (768 m · north up): M1''s and M1a.2''s' 'm1a-2-ground.png'
}

# M1a.3 (-Sheets m1a3): the washboard gone, beside M1a.2. Each world is made and measured again (--erosion-measure, about five
# minutes each; M1a.2's through VISTA_EROSION_SETTINGS, without the creep), and its views rendered:
#  - m1a-3-dots.png: the rows of dots' loupe (2 EV brighter) and its material ids, M1a.2's above M1a.3's (the normals stay in the
#    output folder: with them the sheet is over 1 MB);
#  - m1a-3-ground.png: the ground under the lit middle range and the rows of dots from above, M1a.2's and M1a.3's eroded ground;
#  - m1a-3-hero.png: the hero from M1's camera, M1a.2's and M1a.3's;
#  - m1a-3-lit.png: the lit middle range's loupe, M1a.2's and M1a.3's (today's glacier on its crest or not, by 9 cm: vista-log/M1a.3.md);
#  - m1a-3-meadow.png: M1.12's meadow view at 10:30 in July (a window of its 4K frame, raw at four times -Spp), M1a.2's and M1a.3's:
#    the parked meadow banding looked for (vista-plan section 10, parked).
# measure-m1a2.log and measure-m1a3.log hold the grid's patterns, the drainage and relief measures and the places' distances.
if ($make -contains 'm1a3') {
    $near = '3520,1680'
    $cropPixels = "$($places.grooves.Pixel);$($places.dots.Pixel);$near"
    $day = Join-Path $views 'mountains-day.recipe.json'
    $meadow = Join-Path $views 'materials\meadow.camera.json'
    foreach ($stage in @(@{ Name = 'm1a2'; Set = 'CreepRate=0' }, @{ Name = 'm1a3'; Set = '' })) {
        $env:VISTA_EROSION_SETTINGS = $stage.Set
        try {
            $dir = Join-Path $Out "measure-$($stage.Name)"
            New-Item -ItemType Directory -Force $dir | Out-Null
            & dotnet $App --erosion-measure --recipe $range --seeds 42 --out $dir --crops-camera $hero --crops $cropPixels |
                Out-File -FilePath (Join-Path $Out "measure-$($stage.Name).log") -Encoding utf8
            if ($LASTEXITCODE -ne 0) { throw 'Vista.App --erosion-measure failed.' }
            Run (@('--preview') + $heroArgs + @('--width', '640', '--spp', "$Spp", '--denoise', '--denoise-device', 'cuda', '--out', (Join-Path $Out "hero-$($stage.Name).png")))
            $p = $places.dots
            Run (@('--preview') + $heroArgs + @('--width', '3840', '--loupe', "$($p.X),$($p.Y),640,360", '--spp', "$Spp", '--denoise', '--denoise-device', 'cuda',
                '--debug', 'material,normal', '--compensation', '3.5', '--out', (Join-Path $Out "loupe-dots-$($stage.Name).png")))
            $p = $places.grooves
            Run (@('--preview') + $heroArgs + @('--width', '3840', '--loupe', "$($p.X),$($p.Y),640,360", '--spp', "$Spp", '--denoise', '--denoise-device', 'cuda',
                '--out', (Join-Path $Out "loupe-lit-$($stage.Name).png")))
            Run @('--preview', '--recipe', $day, '--time', '10:30', '--seed', '42', '--camera', $meadow, '--width', '3840', '--loupe', '1060,671,520,292',
                '--spp', "$(4 * $Spp)", '--out', (Join-Path $Out "meadow-$($stage.Name).png"))
        }
        finally {
            $env:VISTA_EROSION_SETTINGS = ''
        }
    }
    $d2 = Join-Path $Out 'loupe-dots-m1a2'
    $d3 = Join-Path $Out 'loupe-dots-m1a3'
    Sheet @("$d2.denoised.png", "$d2.material.png", "$d3.denoised.png", "$d3.material.png") `
        @('M1a.2 · the rows of dots 3.4 km off · +2 EV', 'M1a.2 · materials: scree · meadow · forest by turns in rows',
          'M1a.3 · the same pixels', 'M1a.3 · materials') 2 `
        'M1a.3 a loupe of the 4K frame pixel for pixel: the rows of dots, M1a.2 above and M1a.3 (the creep) below' 'm1a-3-dots.png'
    $g2 = Join-Path $Out 'measure-m1a2'
    $g3 = Join-Path $Out 'measure-m1a3'
    Sheet @((Join-Path $g2 'seed42.crop1.eroded.png'), (Join-Path $g3 'seed42.crop1.eroded.png'), (Join-Path $g2 'seed42.crop2.eroded.png'), (Join-Path $g3 'seed42.crop2.eroded.png')) `
        @('1 the lit middle range from above · M1a.2', '1 · M1a.3', '2 the rows of dots from above · M1a.2: a washboard on the gentle loose ground', '2 · M1a.3: smooth') 4 `
        'M1a.3 the ground from above (768 m · north up): M1a.2''s and M1a.3''s eroded ground' 'm1a-3-ground.png'
    Sheet @((Join-Path $Out 'hero-m1a2.denoised.png'), (Join-Path $Out 'hero-m1a3.denoised.png')) @('M1a.2', 'M1a.3') 2 `
        'M1a.3 the hero (seed 42 · golden hour · M1''s camera): M1a.2''s ground and the ground with the creep' 'm1a-3-hero.png'
    Sheet @((Join-Path $Out 'loupe-lit-m1a2.denoised.png'), (Join-Path $Out 'loupe-lit-m1a3.denoised.png')) `
        @('M1a.2 · the lit middle range 3.8 km off · 1.2 m a pixel', 'M1a.3 · the same pixels: today''s glacier on the crest again') 1 `
        'M1a.3 the lit middle range (a 4K loupe)' 'm1a-3-lit.png'
    Sheet @((Join-Path $Out 'meadow-m1a2.png'), (Join-Path $Out 'meadow-m1a3.png')) `
        @('M1a.2 · M1.12''s meadow view · 10:30 in July · a 520x292 window of the 4K frame', 'M1a.3 · the same window') 2 `
        'M1a.3 the meadow up close: the parked banding looked for' 'm1a-3-meadow.png'
}

# M1a.3a (-Sheets m1a3a): today's ice thins out below the ice that feeds it, beside M1a.3 (GlacierThinning=0 through
# VISTA_EROSION_SETTINGS). Each world is made and measured again, and its views rendered:
#  - m1a-3a-lit.png: the lit middle range's loupe, M1a.3's (a cap's smooth pale domes over the crest's flanks) above M1a.3a's;
#  - m1a-3a-hero.png: the hero from M1's camera, M1a.3's and M1a.3a's;
#  - m1a-3a-ice.png: the materials from above around the main massif's glaciers (the measure's map, cropped), M1a.3's and M1a.3a's;
#  - m1a-3a-aerial.png: the snow-capped mountains from 4 km up at noon (the cost ledger's aerial view), M1a.3's and M1a.3a's.
if ($make -contains 'm1a3a') {
    $cropPixels = "$($places.grooves.Pixel);$($places.dots.Pixel);3520,1680"
    $day = Join-Path $views 'mountains-day.recipe.json'
    $aerial = Join-Path $views 'aerial.camera.json'
    foreach ($stage in @(@{ Name = 'm1a3'; Set = 'GlacierThinning=0' }, @{ Name = 'm1a3a'; Set = '' })) {
        $env:VISTA_EROSION_SETTINGS = $stage.Set
        try {
            $dir = Join-Path $Out "measure-$($stage.Name)"
            New-Item -ItemType Directory -Force $dir | Out-Null
            & dotnet $App --erosion-measure --recipe $range --seeds 42 --out $dir --crops-camera $hero --crops $cropPixels |
                Out-File -FilePath (Join-Path $Out "measure-$($stage.Name).log") -Encoding utf8
            if ($LASTEXITCODE -ne 0) { throw 'Vista.App --erosion-measure failed.' }
            Run (@('--preview') + $heroArgs + @('--width', '640', '--spp', "$Spp", '--denoise', '--denoise-device', 'cuda', '--out', (Join-Path $Out "hero-$($stage.Name).png")))
            $p = $places.grooves
            Run (@('--preview') + $heroArgs + @('--width', '3840', '--loupe', "$($p.X),$($p.Y),640,360", '--spp', "$Spp", '--denoise', '--denoise-device', 'cuda',
                '--debug', 'material', '--out', (Join-Path $Out "loupe-lit-$($stage.Name).png")))
            Run @('--preview', '--recipe', $day, '--seed', '42', '--camera', $aerial, '--width', '640', '--spp', "$Spp", '--denoise', '--denoise-device', 'cuda',
                '--out', (Join-Path $Out "aerial-$($stage.Name).png"))
            # The main massif's glaciers from above: a square of the measure's material map (north up) around them.
            $map = New-Object System.Drawing.Bitmap (Join-Path $dir 'seed42.materials.png')
            $side = [int]($map.Width * 0.42)
            $rect = New-Object System.Drawing.Rectangle ([int]($map.Width * 0.28)), ([int]($map.Height * 0.28)), $side, $side
            $crop = $map.Clone($rect, $map.PixelFormat)
            $crop.Save((Join-Path $Out "ice-$($stage.Name).png"), [System.Drawing.Imaging.ImageFormat]::Png)
            $crop.Dispose()
            $map.Dispose()
        }
        finally {
            $env:VISTA_EROSION_SETTINGS = ''
        }
    }
    Sheet @((Join-Path $Out 'loupe-lit-m1a3.denoised.png'), (Join-Path $Out 'loupe-lit-m1a3a.denoised.png')) `
        @('M1a.3 · the lit middle range 3.8 km off · a cap''s smooth domes over the flanks', 'M1a.3a · the same pixels: the ice thins out below the cap') 1 `
        'M1a.3a the lit middle range (a 4K loupe)' 'm1a-3a-lit.png'
    Sheet @((Join-Path $Out 'hero-m1a3.denoised.png'), (Join-Path $Out 'hero-m1a3a.denoised.png')) @('M1a.3', 'M1a.3a') 2 `
        'M1a.3a the hero (seed 42 · golden hour · M1''s camera): today''s ice as M1a.3 laid it and thinning out below its source' 'm1a-3a-hero.png'
    Sheet @((Join-Path $Out 'ice-m1a3.png'), (Join-Path $Out 'ice-m1a3a.png')) `
        @('M1a.3 · the main massif''s glaciers from above (blue ice · cream firn · white snow) · north up', 'M1a.3a · the same ground') 2 `
        'M1a.3a today''s glaciers from above: M1a.3''s and M1a.3a''s' 'm1a-3a-ice.png'
    Sheet @((Join-Path $Out 'aerial-m1a3.denoised.png'), (Join-Path $Out 'aerial-m1a3a.denoised.png')) @('M1a.3', 'M1a.3a') 2 `
        'M1a.3a the snow-capped mountains from 4 km up at noon: the valley glaciers' 'm1a-3a-aerial.png'
}

# M1a.4 (-Sheets m1a4): the rock's structure (gullies and ribs down the fall line, the beds' ledges, the ribs' own shadows, jointed
# blocks near the camera), beside M1a.3a's rock (--rock-structure off, which renders M1a.3a bit for bit). The world is unchanged, so
# nothing is measured again. Its sheets:
#  - m1a-4-hero.png: the hero from M1's camera, M1a.3a's rock and M1a.4's;
#  - m1a-4-lit.png: the lit middle range's loupe (3.8 km off, 1.2 m a pixel), M1a.3a's above M1a.4's;
#  - m1a-4-near.png: the near face at the frame's right (130 m off, 2 EV brighter: it lies in shade), M1a.3a's beside M1a.4's;
#  - m1a-4-foreground.png: the shaded foreground (place 3, 1.5 km off, 2 EV brighter), M1a.3a's above M1a.4's;
#  - m1a-4-face.png: M1.12's rock face at 10:30 in July, the frame at 480 px (at 640 the sheet passes 1 MB) and a window of its 4K
#    frame, M1a.3a's and M1a.4's;
#  - m1a-4-low-sun.png: the golden-hour down-slope view (the cost ledger's, the sun a few degrees up), M1a.3a's and M1a.4's: the
#    lit near crest in its ribs' shadows.
if ($make -contains 'm1a4') {
    $day = Join-Path $views 'mountains-day.recipe.json'
    $face = Join-Path $views 'materials\rock-face.camera.json'
    $golden = Join-Path $views 'mountains-golden.recipe.json'
    $downSlope = Join-Path $views 'down-slope.camera.json'
    foreach ($stage in @(@{ Name = 'm1a3a'; Extra = @('--rock-structure', 'off') }, @{ Name = 'm1a4'; Extra = @() })) {
        Run (@('--preview') + $heroArgs + @('--width', '640', '--spp', "$Spp", '--denoise', '--denoise-device', 'cuda', '--out', (Join-Path $Out "hero-$($stage.Name).png")) + $stage.Extra)
        foreach ($l in @(@{ Name = 'lit'; X = $places.grooves.X; Y = $places.grooves.Y; Shade = @() },
                         @{ Name = 'near'; X = 3200; Y = 1500; Shade = @('--compensation', '3.5') },
                         @{ Name = 'foreground'; X = $places.beds.X; Y = $places.beds.Y; Shade = @('--compensation', '3.5') })) {
            Run (@('--preview') + $heroArgs + @('--width', '3840', '--loupe', "$($l.X),$($l.Y),640,360", '--spp', "$Spp", '--denoise', '--denoise-device', 'cuda',
                '--out', (Join-Path $Out "loupe-$($l.Name)-$($stage.Name).png")) + $l.Shade + $stage.Extra)
        }
        Run (@('--preview', '--recipe', $day, '--time', '10:30', '--seed', '42', '--camera', $face, '--width', '480', '--spp', "$Spp", '--denoise', '--denoise-device', 'cuda',
            '--out', (Join-Path $Out "face-$($stage.Name).png")) + $stage.Extra)
        Run (@('--preview', '--recipe', $day, '--time', '10:30', '--seed', '42', '--camera', $face, '--width', '3840', '--loupe', '1660,491,520,292', '--spp', "$Spp",
            '--denoise', '--denoise-device', 'cuda', '--out', (Join-Path $Out "face-loupe-$($stage.Name).png")) + $stage.Extra)
        Run (@('--preview', '--recipe', $golden, '--seed', '42', '--camera', $downSlope, '--width', '640', '--spp', "$Spp", '--denoise', '--denoise-device', 'cuda',
            '--out', (Join-Path $Out "low-sun-$($stage.Name).png")) + $stage.Extra)
    }
    Sheet @((Join-Path $Out 'hero-m1a3a.denoised.png'), (Join-Path $Out 'hero-m1a4.denoised.png')) @('M1a.3a', 'M1a.4: the rock''s structure') 2 `
        'M1a.4 the hero (seed 42 · golden hour · M1''s camera): M1a.3a''s rock and the rock''s structure' 'm1a-4-hero.png'
    Sheet @((Join-Path $Out 'loupe-lit-m1a3a.denoised.png'), (Join-Path $Out 'loupe-lit-m1a4.denoised.png')) `
        @('M1a.3a · the lit middle range 3.8 km off · 1.2 m a pixel', 'M1a.4 · the same pixels: gullies and ribs · the beds'' ledges · the ribs'' shadows') 1 `
        'M1a.4 the lit middle range (a 4K loupe)' 'm1a-4-lit.png'
    Sheet @((Join-Path $Out 'loupe-near-m1a3a.denoised.png'), (Join-Path $Out 'loupe-near-m1a4.denoised.png')) `
        @('M1a.3a · the near face at the frame''s right 130 m off · +2 EV', 'M1a.4 · the same pixels: gullies and jointed blocks') 2 `
        'M1a.4 the near face (a 4K loupe)' 'm1a-4-near.png'
    Sheet @((Join-Path $Out 'loupe-foreground-m1a3a.denoised.png'), (Join-Path $Out 'loupe-foreground-m1a4.denoised.png')) `
        @('M1a.3a · the shaded foreground 1.5 km off · +2 EV', 'M1a.4 · the same pixels') 1 `
        'M1a.4 the shaded foreground (a 4K loupe)' 'm1a-4-foreground.png'
    Sheet @((Join-Path $Out 'face-m1a3a.denoised.png'), (Join-Path $Out 'face-m1a4.denoised.png'), (Join-Path $Out 'face-loupe-m1a3a.denoised.png'),
        (Join-Path $Out 'face-loupe-m1a4.denoised.png')) `
        @('M1a.3a · M1.12''s rock face · 10:30 in July', 'M1a.4', 'M1a.3a · a 520x292 window of its 4K frame', 'M1a.4 · the same window') 2 `
        'M1a.4 a rock face up close: M1a.3a''s rock and the rock''s structure' 'm1a-4-face.png'
    Sheet @((Join-Path $Out 'low-sun-m1a3a.denoised.png'), (Join-Path $Out 'low-sun-m1a4.denoised.png')) `
        @('M1a.3a · golden hour down a slope · the sun a few degrees up', 'M1a.4 · the lit crest in its ribs'' shadows') 2 `
        'M1a.4 a low sun: the rock''s structure casts long shadows' 'm1a-4-low-sun.png'
}

Get-ChildItem (Join-Path $Out 'm1a-*.png') | ForEach-Object { '{0} {1:0} KB' -f $_.Name, ($_.Length / 1KB) }
