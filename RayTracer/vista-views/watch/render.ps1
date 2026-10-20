# The M1.8 watch-mode checks (vista-plan section 10, M1.8) on the local GPU: --watch-check starts a --watch process on copies of
# the day recipe and the aerial camera, changes them one at a time (the exposure, the time of day, a camera turn, a camera move,
# the base altitude, the seed through the options file, and the seed back), checks that each render rebuilt exactly the stages
# that change touches, then renders every step again with a cold --preview process and compares the files byte for byte. It runs
# twice: at 640 px with every debug output and the denoised image (CUDA), eroding the two seeds into a fresh cache under -Out (the
# first-time cost), then at the default 320 px against that cache (the cost of a seed seen before). Needs a build:
# dotnet build Vista.App -c Release (from RayTracer/), and for the denoised image tools/fetch-oidn.ps1.
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-watch'),
    [string]$App = (Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll'),
    [int]$Spp = 64
)

New-Item -ItemType Directory -Force $Out | Out-Null
$views = Join-Path $PSScriptRoot '..'
# The worlds are eroded into a folder of their own, not the user's cache, so the first run measures eroding them.
$env:VISTA_WORLD_CACHE = Join-Path $Out 'worlds'

function Run([string[]]$arguments) {
    & dotnet $App @arguments
    if ($LASTEXITCODE -ne 0) { throw "Vista.App $($arguments -join ' ') failed." }
}

$recipe = Join-Path $views 'mountains-day.recipe.json'
$camera = Join-Path $views 'aerial.camera.json'
Run @('--watch-check', '--recipe', $recipe, '--camera', $camera, '--width', '640', '--spp', $Spp, '--debug', 'all',
      '--denoise', '--denoise-device', 'cuda', '--out', (Join-Path $Out '640'))
Run @('--watch-check', '--recipe', $recipe, '--camera', $camera, '--width', '320', '--spp', $Spp, '--out', (Join-Path $Out '320'))
