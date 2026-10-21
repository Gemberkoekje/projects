# The M1.11a drainage checks (vista-plan section 10, M1.11a) on the local GPU:
#  - the drainage measures of M1.7's erosion alone (--set IncisionSteps=0 --set PoolFillMetres=0) and of this build's (the incision first)
#    on four seeds: --erosion-measure, with the coarse grid's measures and the erosion and drainage maps;
#  - the drainage sheet: the four seeds' drainage images, M1.7's world against this build's, reduced to -MapSize pixels;
#  - with -Before (a main build's Vista.App.dll): the four seeds from the aerial camera and the seed-42 views (default, down a
#    slope, layered ridges, range), main against this build, in a low afternoon sun (the day recipe with the sun at 12 degrees,
#    which shows the relief better than golden hour's +2 degrees).
# Worlds are cached in -Worlds (default: a folder under -Out, so the user's cache is untouched). Build first:
# dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-drainage'),
    [string]$App = (Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll'),
    [string]$Before = '',
    [string]$Worlds = '',
    [int]$Width = 640,
    [int]$Spp = 64,
    [int]$MapSize = 256,
    [string]$Seeds = '42,7,1234,2026'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $Out | Out-Null
if ($Worlds -eq '') { $Worlds = Join-Path $Out 'worlds' }
$views = Split-Path $PSScriptRoot -Parent
$golden = Join-Path $views 'mountains-golden.recipe.json'
$day = Join-Path $views 'mountains-day.recipe.json'
$aerial = Join-Path $views 'aerial.camera.json'
$seedList = $Seeds.Split(',')

function Run([string]$dll, [string[]]$arguments) {
    & dotnet $dll @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Vista.App $($arguments -join ' ') failed." }
}

# A copy of a drainage image reduced to $size pixels a side, each pixel the bluest of the block it covers (the most water), so a
# channel one pixel wide survives the reduction as the drainage image keeps channels one cell wide.
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class DrainageReduce
{
    public static void Bluest(string from, string to, int size)
    {
        using (var source = new Bitmap(from))
        using (var target = new Bitmap(size, size, PixelFormat.Format24bppRgb))
        {
            int w = source.Width, h = source.Height;
            BitmapData input = source.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            var bytes = new byte[input.Stride * h];
            Marshal.Copy(input.Scan0, bytes, 0, bytes.Length);
            source.UnlockBits(input);
            BitmapData output = target.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            var result = new byte[output.Stride * size];
            for (int y = 0; y < size; y++)
            {
                int y0 = y * h / size, y1 = Math.Max((y + 1) * h / size, y0 + 1);
                for (int x = 0; x < size; x++)
                {
                    int x0 = x * w / size, x1 = Math.Max((x + 1) * w / size, x0 + 1);
                    int best = -1, bluest = int.MinValue;
                    for (int yy = y0; yy < y1; yy++)
                    {
                        for (int xx = x0; xx < x1; xx++)
                        {
                            int i = yy * input.Stride + 3 * xx;
                            int blue = bytes[i] - bytes[i + 2];
                            if (blue > bluest) { bluest = blue; best = i; }
                        }
                    }
                    int o = y * output.Stride + 3 * x;
                    result[o] = bytes[best]; result[o + 1] = bytes[best + 1]; result[o + 2] = bytes[best + 2];
                }
            }
            Marshal.Copy(result, 0, output.Scan0, result.Length);
            target.UnlockBits(output);
            target.Save(to, ImageFormat.Png);
        }
    }
}
'@
function Reduce([string]$from, [string]$to, [int]$size) { [DrainageReduce]::Bluest($from, $to, $size) }

# The measures, M1.7's erosion alone and this build's. They print the drainage of the base terrain, the coarse grid before and after
# the incision, the ground it hands the passes and the eroded world, at channels of 1 and 0.1 km2.
$env:VISTA_WORLD_CACHE = Join-Path $Worlds 'measure'
& dotnet $App --erosion-measure --recipe $golden --seeds $Seeds --set IncisionSteps=0 --set PoolFillMetres=0 --out (Join-Path $Out 'measure-m17') *> (Join-Path $Out 'measure-m17.log')
if ($LASTEXITCODE -ne 0) { throw 'The M1.7 measure failed.' }
& dotnet $App --erosion-measure --recipe $golden --seeds $Seeds --out (Join-Path $Out 'measure-m111a') *> (Join-Path $Out 'measure-m111a.log')
if ($LASTEXITCODE -ne 0) { throw 'The M1.11a measure failed.' }

# The drainage sheet: each seed's drainage, M1.7's world left, this build's right.
$tiles = @()
$labels = @()
New-Item -ItemType Directory -Force (Join-Path $Out 'drainage') | Out-Null
foreach ($seed in $seedList) {
    foreach ($side in @(@('measure-m17', 'M1.7 erosion'), @('measure-m111a', 'M1.11a incision then erosion'))) {
        $cell = Join-Path $Out "drainage\$($side[0])-$seed.png"
        Reduce (Join-Path $Out "$($side[0])\seed$seed.drainage.png") $cell $MapSize
        $tiles += $cell
        $labels += "seed $seed · $($side[1])"
    }
}
Run $App (@('--sheet-from') + $tiles + @('--columns', '2', '--labels', ($labels -join ','),
    '--title', 'M1.11a drainage · upstream area blue from 0.05 km2 to the largest river · lakes over 1 m dark blue · north up',
    '--out', (Join-Path $Out 'drainage-sheet.png')))

if ($Before -ne '') {
    # The four seeds from the aerial camera, then the seed-42 views: main left, this build right. Each build keeps its own worlds.
    $sun = @('--recipe', $day, '--sun-elevation', '12', '--width', $Width, '--spp', $Spp)
    $shots = @()
    foreach ($seed in $seedList) { $shots += , @("seed $seed · aerial", $aerial, $seed) }
    foreach ($camera in @('default.camera.json', 'down-slope.camera.json', 'aerial\layered-ridges.camera.json', 'atmosphere\range.camera.json')) {
        $name = [System.IO.Path]::GetFileNameWithoutExtension([System.IO.Path]::GetFileNameWithoutExtension($camera))
        $shots += , @("seed 42 · $name", (Join-Path $views $camera), '42')
    }
    $seedTiles = @(); $seedLabels = @(); $viewTiles = @(); $viewLabels = @()
    $k = 0
    foreach ($shot in $shots) {
        $k++
        $pair = @()
        foreach ($build in @(@('main', $Before, 'M1.11 (main)'), @('m111a', $App, 'M1.11a'))) {
            $env:VISTA_WORLD_CACHE = Join-Path $Worlds $build[0]
            $png = Join-Path $Out "renders\$($build[0])-$k.png"
            Run $build[1] (@('--preview') + $sun + @('--camera', $shot[1], '--seed', $shot[2], '--out', $png))
            $pair += , @($png, "$($shot[0]) · $($build[2])")
        }
        if ($k -le $seedList.Count) {
            $seedTiles += $pair | ForEach-Object { $_[0] }; $seedLabels += $pair | ForEach-Object { $_[1] }
        } else {
            $viewTiles += $pair | ForEach-Object { $_[0] }; $viewLabels += $pair | ForEach-Object { $_[1] }
        }
    }
    Run $App (@('--sheet-from') + $seedTiles + @('--columns', '2', '--labels', ($seedLabels -join ','),
        '--title', 'M1.11a: four seeds from the aerial camera, the sun at 12 degrees · main left, the incision right', '--out', (Join-Path $Out 'seeds-sheet.png')))
    Run $App (@('--sheet-from') + $viewTiles + @('--columns', '2', '--labels', ($viewLabels -join ','),
        '--title', 'M1.11a: the seed-42 views, the sun at 12 degrees · main left, the incision right', '--out', (Join-Path $Out 'views-sheet.png')))
}
Write-Output "Measures: $(Join-Path $Out 'measure-m17.log'), $(Join-Path $Out 'measure-m111a.log'); sheets in $Out"
