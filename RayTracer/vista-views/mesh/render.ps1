# The M1.9 view-dependent mesh checks (vista-plan section 10, M1.9) on the local GPU:
#  - the measurement table: five views at 320 px and 4K, the uniform mesh of the world grid and the view-dependent mesh at 0.5, 1 and
#    2 px, with and without the detail and the far field (--mesh-measure: triangles, memory, build time, cost per pixel-sample);
#  - the same world at every size: three views rendered at 4K, box-filtered down to 320 px, beside their 320 px previews;
#  - 4K loupes near the camera: the grid alone (no detail), the view mesh at 1 px and at 0.5 px (0.25 px would be 64 M
#    triangles, over what one acceleration structure takes);
#  - the shadow of a peak off screen: offscreen-shadow.camera.json looks away from the golden-hour sun across a valley in the shadow
#    of a ridge behind the camera, with the uniform mesh (everything kept) and the view mesh;
#  - with -Before (a main build's Vista.App.dll), before/after sheets of the views.
# The GPU is often shared with other programs: read nvidia-smi first, and trust times as ratios between rows of one run.
# Build first: dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-mesh'),
    [string]$App = (Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll'),
    [string]$Before = '',
    [int]$Spp = 64
)

New-Item -ItemType Directory -Force $Out | Out-Null
$views = Join-Path $PSScriptRoot '..'
$day = Join-Path $views 'mountains-day.recipe.json'
$golden = Join-Path $views 'mountains-golden.recipe.json'
$named = [ordered]@{
    'default' = 'default.camera.json'; 'aerial' = 'aerial.camera.json'; 'down-slope' = 'down-slope.camera.json';
    'layered-ridges' = 'aerial\layered-ridges.camera.json'; 'range' = 'atmosphere\range.camera.json'
}
$cameras = $named.Values | ForEach-Object { Join-Path $views $_ }

function Run([string]$app, [string[]]$arguments) {
    & dotnet $app @arguments
    if ($LASTEXITCODE -ne 0) { throw "Vista.App $($arguments -join ' ') failed." }
}

# The measurement table (16 spp: the cost per pixel-sample, not the look).
Run $App @('--mesh-measure', '--recipe', $golden, '--cameras', ($cameras -join ','), '--sizes', '320,3840', '--errors', '0.5,1,2',
    '--uniform', '--plain', '--spp', '16', '--out', (Join-Path $Out 'measure'))

# The same world at every size.
$three = @('default', 'aerial', 'layered-ridges') | ForEach-Object { Join-Path $views $named[$_] }
Run $App @('--mesh-measure', '--recipe', $golden, '--cameras', ($three -join ','), '--sizes', '320,3840', '--errors', '1', '--downsample',
    '--spp', $Spp, '--out', (Join-Path $Out 'sizes'))
$tiles = @()
$labels = @()
foreach ($name in @('default', 'aerial', 'layered-ridges')) {
    $tiles += @((Join-Path $Out "sizes\$name-view-1-px-320.png"), (Join-Path $Out "sizes\$name-view-1-px-3840-down.png"))
    $labels += @("$name · 320 px preview", "$name · 4K box-filtered to 320 px")
}
Run $App (@('--sheet-from') + $tiles + @('--columns', '2', '--labels', ($labels -join ','), '--title',
    'M1.9: the same world at every size · golden hour · one exposure per pair', '--out', (Join-Path $Out 'sizes-sheet.png')))

# Loupes near the camera at 4K: the grid alone, 1 px, 0.5 px.
$tiles = @()
$labels = @()
foreach ($loupe in @(@('default', '2700,1500,960,540'), @('down-slope', '1440,800,960,540'))) {
    $name = $loupe[0]
    $common = @('--preview', '--recipe', $golden, '--camera', (Join-Path $views $named[$name]), '--seed', '42', '--width', '3840',
        '--spp', $Spp, '--loupe', $loupe[1])
    Run $App ($common + @('--detail', 'off', '--out', (Join-Path $Out "loupes\$name-grid.png")))
    Run $App ($common + @('--out', (Join-Path $Out "loupes\$name-1px.png")))
    Run $App ($common + @('--mesh-error', '0.5', '--out', (Join-Path $Out "loupes\$name-half.png")))
    $tiles += @((Join-Path $Out "loupes\$name-grid.png"), (Join-Path $Out "loupes\$name-1px.png"), (Join-Path $Out "loupes\$name-half.png"))
    $labels += @("$name · the grid alone (no detail)", "$name · 1 px", "$name · 0.5 px")
}
Run $App (@('--sheet-from') + $tiles + @('--columns', '3', '--labels', ($labels -join ','), '--title',
    'M1.9: 4K loupes near the camera, golden hour', '--out', (Join-Path $Out 'loupes-sheet.png')))

# The shadow of a peak off screen.
$shadow = Join-Path $PSScriptRoot 'offscreen-shadow.camera.json'
$common = @('--preview', '--recipe', $golden, '--camera', $shadow, '--seed', '42', '--width', '1280', '--spp', $Spp)
Run $App ($common + @('--mesh', 'uniform', '--out', (Join-Path $Out 'shadow\uniform.png')))
Run $App ($common + @('--out', (Join-Path $Out 'shadow\view.png')))
$tiles = @((Join-Path $Out 'shadow\uniform.png'), (Join-Path $Out 'shadow\view.png'))
$labels = @('uniform mesh (everything kept)', 'view-dependent mesh')
if ($Before -ne '') {
    Run $Before ($common + @('--out', (Join-Path $Out 'shadow\main.png')))
    $tiles = @(Join-Path $Out 'shadow\main.png') + $tiles
    $labels = @('main (M1.8)') + $labels
}
Run $App (@('--sheet-from') + $tiles + @('--columns', $tiles.Count, '--labels', ($labels -join ','), '--title',
    'M1.9: the valley in the shadow of a ridge behind the camera, golden hour', '--out', (Join-Path $Out 'shadow-sheet.png')))

# Before and after.
if ($Before -ne '') {
    foreach ($recipe in @(@('day', $day), @('golden', $golden))) {
        $tiles = @()
        $labels = @()
        foreach ($name in $named.Keys) {
            $common = @('--preview', '--recipe', $recipe[1], '--camera', (Join-Path $views $named[$name]), '--seed', '42', '--width', '640', '--spp', $Spp)
            Run $Before ($common + @('--out', (Join-Path $Out "before-after\$($recipe[0])-$name-main.png")))
            Run $App ($common + @('--out', (Join-Path $Out "before-after\$($recipe[0])-$name.png")))
            $tiles += @((Join-Path $Out "before-after\$($recipe[0])-$name-main.png"), (Join-Path $Out "before-after\$($recipe[0])-$name.png"))
            $labels += @("$name · main (M1.8)", "$name · M1.9")
        }
        Run $App (@('--sheet-from') + $tiles + @('--columns', '2', '--labels', ($labels -join ','), '--title',
            "M1.9 before and after · $($recipe[0])", '--out', (Join-Path $Out "before-after-$($recipe[0]).png")))
    }
}
Write-Output "Table: $(Join-Path $Out 'measure\mesh-measure.md'); sheets in $Out"
