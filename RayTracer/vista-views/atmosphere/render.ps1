# The M1.3 atmosphere checks (vista-plan section 10, M1.3) on the local GPU: the LUT sky against the reference per
# wavelength band, and the sheets that show the reddened sun, the Earth's shadow, the Belt of Venus and peaks lit
# after the valley's sunset. Build first: dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-atmosphere'),
    [string]$App = (Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll'),
    [int]$Paths = 1048576
)

New-Item -ItemType Directory -Force $Out | Out-Null
$july = Join-Path $PSScriptRoot 'zermatt-july.recipe.json'
$october = Join-Path $PSScriptRoot 'zermatt-october.recipe.json'
function Camera([string]$name) { Join-Path $PSScriptRoot "$name.camera.json" }

function Run([string[]]$arguments) {
    & dotnet $App @arguments
    if ($LASTEXITCODE -ne 0) { throw "Vista.App $($arguments -join ' ') failed." }
}

# The LUT sky against the reference: above the horizon from the valley floor, below it from 4.2 km up.
Run @('--atmosphere-check', '--paths', $Paths, '--out', (Join-Path $Out 'check.json'))
Run @('--atmosphere-check', '--paths', ($Paths / 4), '--sun-elevations', '5,-2,-4,-6', '--observer-height', '4200',
      '--below-horizon', '--out', (Join-Path $Out 'check-below.json'))

# Sun-elevation sheets from -6 to +10 degrees, toward the sun and away from it.
$elevations = 'sunElevation=-6,-4,-2,0,2,4,6,8,10'
Run @('--sheet', '--recipe', $july, '--camera', (Camera 'toward-sun'), '--seed', '42', '--vary', $elevations, '--columns', '3',
      '--out', (Join-Path $Out 'toward-sun.png'))
Run @('--sheet', '--recipe', $july, '--camera', (Camera 'away-from-sun'), '--seed', '42', '--vary', $elevations, '--columns', '3',
      '--out', (Join-Path $Out 'away-from-sun.png'))

# Peaks lit after the valley floor's sunset, from the transmittance alone.
Run @('--sheet', '--recipe', $july, '--camera', (Camera 'range'), '--seed', '42', '--vary', 'sunElevation=2,1,0,-0.5,-1,-1.5',
      '--columns', '3', '--out', (Join-Path $Out 'peaks-lit.png'))

# The time-of-day presets on an October day: dawn and dusk lit by the sky, night black.
Run @('--sheet', '--recipe', $october, '--camera', (Camera 'range'), '--seed', '42', '--vary', 'time=dawn,day,goldenHour,dusk,night',
      '--columns', '3', '--out', (Join-Path $Out 'times.png'))
