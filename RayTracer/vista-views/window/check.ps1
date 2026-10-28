# The M1.16 window check (vista-plan section 10, M1.16) on the local GPU: --window-check opens the real window off the screen on M1's
# hero scene (an alpine range at golden hour, seed 42) and walks through M1 with the methods its controls call: the new scene's auto
# camera, a larger progressive preview, dawn to dusk, the camera's nudges, turns, exposure and collision, pinned and reset sliders, the
# debug views and the denoiser, the snow-capped mountains' bare steep faces and their dawn, the saved scene opened again, then the
# estimate and the 4K final on the kept stages. It checks each run's stages, saves a picture of the window after each step in
# <Out>\shots, and three times renders the saved files with a cold --preview process: the window's preview must be the CLI's, byte for
# byte. Needs a build: dotnet build Vista.App -c Release (from RayTracer/), and for the denoised view tools/fetch-oidn.ps1.
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-window'),
    [string]$App = (Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll'),
    [int]$FinalSpp = 64,
    [switch]$NoFinal,
    [switch]$Visible
)

New-Item -ItemType Directory -Force $Out | Out-Null
$arguments = @('--window-check', '--out', $Out, '--final-spp', $FinalSpp)
if ($NoFinal) { $arguments += '--no-final' }
if ($Visible) { $arguments += '--visible' }
& dotnet $App @arguments | Tee-Object -FilePath (Join-Path $Out 'check.log')
if ($LASTEXITCODE -ne 0) { throw "The window check failed: see $(Join-Path $Out 'check.log')." }
