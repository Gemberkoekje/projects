# The M1.4 aerial-perspective checks (vista-plan section 10, M1.4) on the local GPU: the LUT air along finite segments
# against the reference per wavelength band, three views rendered with the LUTs, the reference and the reference with
# the terrain's shadow in the air (by distance), and the sea-level against 3000 m sheet. Build first:
# dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-aerial'),
    [string]$App = (Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll'),
    [int]$Paths = 1048576,
    [int]$ReferenceSpp = 16384
)

New-Item -ItemType Directory -Force $Out | Out-Null
$views = Join-Path $PSScriptRoot '..'
$day = Join-Path $views 'mountains-day.recipe.json'
$golden = Join-Path $views 'mountains-golden.recipe.json'
$ridges = Join-Path $PSScriptRoot 'layered-ridges.camera.json'
$downSlope = Join-Path $views 'down-slope.camera.json'

function Run([string[]]$arguments) {
    & dotnet $App @arguments
    if ($LASTEXITCODE -ne 0) { throw "Vista.App $($arguments -join ' ') failed." }
}

# Segments of 1, 4 and 16 km: up from 2 m above Zermatt's valley floor, and down from 1.7 km above it (a camera over
# the range); a segment that reaches the ground sphere ends there.
$segments = @('--segments', '1000,4000,16000', '--sun-elevations', '30,5,-2,-6', '--paths', $Paths)
Run (@('--atmosphere-check') + $segments + @('--out', (Join-Path $Out 'segments-up.json')))
Run (@('--atmosphere-check') + $segments + @('--observer-height', '1700', '--below-horizon', '--out', (Join-Path $Out 'segments-down.json')))

# One view three ways, by distance: the layered ridges at noon (lit air) and at golden hour toward the sun (the valleys
# between the ridges in their shadow), and the down-slope view's shadowed valley at golden hour.
Run @('--atmosphere-compare', '--recipe', $day, '--camera', $ridges, '--reference-spp', $ReferenceSpp, '--out', (Join-Path $Out 'ridges-day'))
Run @('--atmosphere-compare', '--recipe', $golden, '--camera', $ridges, '--reference-spp', $ReferenceSpp, '--out', (Join-Path $Out 'ridges-golden'))
Run @('--atmosphere-compare', '--recipe', $golden, '--camera', $downSlope, '--reference-spp', $ReferenceSpp, '--out', (Join-Path $Out 'valley-golden'))

# Sea level against 3000 m: the deeper blue and the thinner haze of the same scene higher in the air.
Run @('--sheet', '--recipe', $day, '--camera', $ridges, '--seed', '42', '--width', '640', '--spp', '256',
      '--vary', 'altitude=0,3000', '--columns', '2', '--out', (Join-Path $Out 'altitude.png'))
