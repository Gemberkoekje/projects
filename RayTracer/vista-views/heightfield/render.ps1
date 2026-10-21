# The M1.10 height-field checks (vista-plan section 10, M1.10) on the local GPU: the far field meshed (M1.9) against traced as
# height-field tiles, on the five M1.9 views at 4K, golden hour, seed 42 (--height-field-compare):
#  - size: triangles, tiles, the mesh's, the tiles' and the far grids' bytes, the acceleration structures, the process's video
#    memory, the CPU and GPU build times and the cost per pixel-sample, at 1 and 0.5 px, with 8² and 16² tiles;
#  - cost: the two rendered alternately by two renderers, three rounds; read the median ratio;
#  - image: the mesh against the height field at 2, 1 and 0.5 px (the same mesh over the world grid, so only the far field
#    differs), the noise floor (the 1 px mesh on two random streams) and 16² against 8² tiles, with difference images;
#  - a sheet per view: the height field, the 1 px mesh and their difference (white = 25% of the mean luminance).
# The GPU is often shared with other programs: read nvidia-smi first, and trust the cost as the alternating ratio.
# Build first: dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-heightfield'),
    [string]$App = (Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll'),
    [int]$ImageSpp = 128
)

New-Item -ItemType Directory -Force $Out | Out-Null
$views = Join-Path $PSScriptRoot '..'
$golden = Join-Path $views 'mountains-golden.recipe.json'
$named = [ordered]@{
    'default' = 'default.camera.json'; 'aerial' = 'aerial.camera.json'; 'down-slope' = 'down-slope.camera.json';
    'layered-ridges' = 'aerial\layered-ridges.camera.json'; 'range' = 'atmosphere\range.camera.json'
}
$cameras = $named.Values | ForEach-Object { Join-Path $views $_ }

function Run([string[]]$arguments) {
    & dotnet $App @arguments
    if ($LASTEXITCODE -ne 0) { throw "Vista.App $($arguments -join ' ') failed." }
}

Run @('--height-field-compare', '--recipe', $golden, '--cameras', ($cameras -join ','), '--size', '3840', '--errors', '1,0.5',
    '--tiles', '8,16', '--rounds', '3', '--spp', '16', '--image-width', '3840', '--image-spp', $ImageSpp, '--image-errors', '2,1,0.5',
    '--image-scale', '3', '--out', $Out)

$tiles = @()
$labels = @()
foreach ($name in $named.Keys) {
    $tiles += @((Join-Path $Out "$name-1px-height-field.png"), (Join-Path $Out "$name-1px-mesh.png"), (Join-Path $Out "$name-1px-diff.png"))
    $labels += @("$name · height field", "$name · mesh, 1 px", "$name · |ΔY| (white: 25% of the mean)")
}
Run (@('--sheet-from') + $tiles + @('--columns', '3', '--labels', ($labels -join ','), '--title',
    'M1.10: the far field as a height field against the 1 px mesh · 4K box-filtered to 1280 · golden hour', '--out', (Join-Path $Out 'compare-sheet.png')))
Write-Output "Tables: $(Join-Path $Out 'height-field-compare.md'); sheet: $(Join-Path $Out 'compare-sheet.png')"
