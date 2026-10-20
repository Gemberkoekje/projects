# The M1.6 denoiser checks (vista-plan section 10, M1.6) on the local GPU and CPU: M1.5's four sampling views (dawn and day from
# the default camera, golden hour from the default camera and down the slope) rendered at 16 spp and denoised every way
# --denoise-compare knows (albedo and normal noisy, clean or prefiltered; the input scale automatic, physical or metered; each
# quality; the CPU and CUDA), against a 4096 spp reference, at 640 and 320 px wide; then the default settings timed at 4K on
# both devices, and one raw | denoised | reference sheet of the four views. Needs Open Image Denoise
# (powershell -ExecutionPolicy Bypass -File tools/fetch-oidn.ps1) and a build: dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-denoise'),
    [string]$App = (Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll'),
    [int]$Spp = 16,
    [int]$ReferenceSpp = 4096,
    [int]$TimingWidth = 3840
)

New-Item -ItemType Directory -Force $Out | Out-Null
$views = Join-Path $PSScriptRoot '..'
$cases = @(
    @{ Name = 'dawn';              Recipe = (Join-Path $views 'sampling\mountains-dawn.recipe.json'); Camera = (Join-Path $views 'default.camera.json');    Loupe = '240,135,160,90' },
    @{ Name = 'golden-down-slope'; Recipe = (Join-Path $views 'mountains-golden.recipe.json');        Camera = (Join-Path $views 'down-slope.camera.json'); Loupe = '200,120,160,90' },
    @{ Name = 'golden-default';    Recipe = (Join-Path $views 'mountains-golden.recipe.json');        Camera = (Join-Path $views 'default.camera.json');    Loupe = '240,135,160,90' },
    @{ Name = 'day';               Recipe = (Join-Path $views 'mountains-day.recipe.json');           Camera = (Join-Path $views 'default.camera.json');    Loupe = '240,135,160,90' }
)

function Run([string[]]$arguments) {
    & dotnet $App @arguments
    if ($LASTEXITCODE -ne 0) { throw "Vista.App $($arguments -join ' ') failed." }
}

$culture = [System.Globalization.CultureInfo]::InvariantCulture
$tiles = @()
$labels = @()
foreach ($case in $cases) {
    $dir = Join-Path $Out $case.Name
    Run @('--denoise-compare', '--recipe', $case.Recipe, '--camera', $case.Camera, '--spp', $Spp, '--reference-spp', $ReferenceSpp,
          '--width', '640', '--devices', 'cpu,cuda', '--loupe', $case.Loupe, '--out', $dir)
    # The preview's own size: the filter sees a quarter of the pixels.
    Run @('--denoise-compare', '--recipe', $case.Recipe, '--camera', $case.Camera, '--spp', $Spp, '--reference-spp', $ReferenceSpp,
          '--width', '320', '--devices', 'cpu,cuda', '--out', "$dir-320")

    $report = Get-Content (Join-Path $dir 'compare.json') -Raw | ConvertFrom-Json
    $default = $report.ways | Where-Object { $_.settings.device -eq 'cpu' -and $_.settings.quality -eq 'high' -and
        $_.settings.auxiliary -eq 'clean' -and $_.settings.inputScale -eq 'metered' } | Select-Object -First 1
    $tiles += @((Join-Path $dir 'raw.png'), (Join-Path $dir 'denoised.png'), (Join-Path $dir 'reference.png'))
    # Labels cannot hold commas (--labels is a comma-separated list), so numbers are formatted invariantly.
    $labels += @(
        [string]::Format($culture, '{0}: raw {1} spp: relMSE {2:0.0000}', $case.Name, $Spp, $report.raw.corrected),
        [string]::Format($culture, '{0}: denoised: relMSE {1:0.00000} = raw {2:0} spp', $case.Name, $default.relativeMse, $default.equalErrorSpp),
        [string]::Format($culture, '{0}: reference {1} spp', $case.Name, $ReferenceSpp))
}

# The default settings at a 4K final's size, on both devices (the reference is not used, so it is small).
Run @('--denoise-compare', '--recipe', $cases[1].Recipe, '--camera', $cases[1].Camera, '--spp', $Spp, '--reference-spp', '64',
      '--width', '640', '--devices', 'cpu,cuda', '--timing-width', $TimingWidth, '--out', (Join-Path $Out 'timing'))

# One sheet of the four views.
Run (@('--sheet-from') + $tiles + @('--out', (Join-Path $Out 'denoise-sheet.png'), '--columns', '3', '--labels', ($labels -join ','),
      '--title', "M1.6: $Spp spp denoised by Open Image Denoise (CPU, high quality, clean aux, metered scale) against $ReferenceSpp spp"))
