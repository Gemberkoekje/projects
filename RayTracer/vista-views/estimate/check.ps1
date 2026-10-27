# The M1.15 check (vista-plan section 10, M1.15) on the local GPU: for each test view, --estimate, then the final render itself,
# each a fresh process timed on the wall from outside; prints the estimate's range for the whole run against the final's wall time,
# the trace's range against the final's trace, the planned peak video memory against the final's measured peak (inside the plan's
# stated margin, 3%), and how far the live ETA was off at 10%, 25% and 50% of the trace; writes check.csv beside the renders.
#  - sky: the sky camera, a summer day (sky-heavy);
#  - aerial, default: the ledger's day views (terrain-heavy);
#  - down-slope: the ledger's golden-hour view (terrain under a low sun, the valleys' pools);
#  - dawn: the aerial view at summer dawn, the sun at -4 degrees (twilight);
#  - snow: the aerial view in mid-April, snow down to the forest;
#  - fog-sea: the pools' fog-sea camera at the first Sunnegga webcam frame's date and time with the mist pinned at 200 m (the
#    costliest view: a march through dense mist).
# -Rounds repeats the whole set; the GPU is shared (VISTA_GPU_SHARE), as the user's own renders run.
# Build first: dotnet build Vista.App -c Release (from RayTracer/).
param(
    [string]$Out = (Join-Path $env:TEMP 'vista-estimate'),
    [string]$App = (Join-Path $PSScriptRoot '..\..\Vista.App\bin\Release\net10.0-windows\Vista.App.dll'),
    [int]$Width = 3840,
    [int]$Spp = 64,
    [string]$Views = 'sky,aerial,default,down-slope,dawn,snow,fog-sea',
    [int]$Rounds = 1,
    [switch]$Keep
)

$ErrorActionPreference = 'Stop'
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
$root = Split-Path $PSScriptRoot
$all = [ordered]@{
    'sky'        = @('--recipe', "$root\mountains-day.recipe.json", '--camera', "$root\sky.camera.json")
    'aerial'     = @('--recipe', "$root\mountains-day.recipe.json", '--camera', "$root\aerial.camera.json")
    'default'    = @('--recipe', "$root\mountains-day.recipe.json", '--camera', "$root\default.camera.json")
    'down-slope' = @('--recipe', "$root\mountains-golden.recipe.json", '--camera', "$root\down-slope.camera.json")
    'dawn'       = @('--recipe', "$root\sampling\mountains-dawn.recipe.json", '--camera', "$root\aerial.camera.json")
    'snow'       = @('--recipe', "$root\snow\mountains-april.recipe.json", '--camera', "$root\aerial.camera.json")
    'fog-sea'    = @('--recipe', "$root\mountains-golden.recipe.json", '--camera', "$root\pools\fog-sea.camera.json",
                     '--date', '2026-10-12', '--time', '06:25', '--mist', '0.2')
}

function Run([string[]]$arguments) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    & dotnet $App @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Vista.App failed: $($arguments -join ' ')" }
    return $watch.Elapsed.TotalSeconds
}

function Read([string]$path) { return Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json }

function Inside($range, [double]$value) { return ($value -ge $range.low) -and ($value -le $range.high) }

New-Item -ItemType Directory -Force $Out | Out-Null
$gpu = (& nvidia-smi --query-gpu=utilization.gpu,memory.used --format=csv,noheader) -join ' '
Write-Host "GPU before: $gpu; share $(if ($env:VISTA_GPU_SHARE) { $env:VISTA_GPU_SHARE } else { 'default' })"
$rows = @()
foreach ($round in 1..$Rounds) {
    foreach ($name in $Views.Split(',')) {
        $base = Join-Path $Out "$name-r$round"
        $common = @('--preview', '--seed', '42', '--width', "$Width", '--spp', "$Spp") + $all[$name] + @('--out', "$base.png")
        $estimateSeconds = Run ($common + @('--estimate', '--noise', '1'))
        $e = (Read "$base.estimate.json").estimate
        $finalSeconds = Run $common
        $f = Read "$base.json"
        if (-not $Keep) { Remove-Item "$base.exr" -ErrorAction SilentlyContinue }

        $trace = $f.render.trace
        $eta = foreach ($c in $trace.checkpoints | Where-Object { $_.progress -le 0.5 -and $null -ne $_.remainingSeconds }) {
            ($c.remainingSeconds - $c.actualRemainingSeconds) / $c.actualRemainingSeconds
        }
        $worstEta = if ($eta) { ($eta | ForEach-Object { [math]::Abs($_) } | Measure-Object -Maximum).Maximum } else { [double]::NaN }
        $peak = $f.render.stats.peakVideoMemoryBytes
        $planned = $e.memory.plannedPeakBytes
        $row = [pscustomobject]@{
            View          = $name
            Round         = $round
            TotalLow      = [math]::Round($e.total.low, 1)
            TotalMid      = [math]::Round($e.total.mid, 1)
            TotalHigh     = [math]::Round($e.total.high, 1)
            Final         = [math]::Round($finalSeconds, 1)
            TotalInside   = Inside $e.total $finalSeconds
            TraceLow      = [math]::Round($e.trace.low, 2)
            TraceMid      = [math]::Round($e.trace.mid, 2)
            TraceHigh     = [math]::Round($e.trace.high, 2)
            TraceFinal    = [math]::Round($trace.wallSeconds, 2)
            TraceInside   = Inside $e.trace $trace.wallSeconds
            GpuEstimate   = [math]::Round($e.projection.probeGpuSeconds + [math]::Max(0, $Spp - $e.projection.probeSamples) * $e.projection.framePixels * $e.projection.costNs * 1e-9, 3)
            GpuFinal      = [math]::Round($trace.gpuSeconds, 3)
            PreviewRatio  = [math]::Round($e.preview.finalCostRatio, 2)
            PlannedMiB    = [math]::Round($planned / 1MB)
            PeakMiB       = [math]::Round($peak / 1MB)
            MemoryError   = [math]::Round(($planned - $peak) / $peak, 4)
            MemoryInside  = [math]::Abs($peak - $planned) -le 0.03 * $planned
            ProbePeakMiB  = [math]::Round($e.memory.measuredPeakBytes / 1MB)
            WorstEta      = [math]::Round($worstEta, 3)
            NoiseSteps    = [math]::Round($e.noise.stepsAtSamples, 2)
            EstimateRun   = [math]::Round($estimateSeconds, 1)
        }
        $rows += $row
        Write-Host ('{0,-10} r{1}  whole run {2,6:0.0} s in [{3,6:0.0}, {4,6:0.0}] {5,-5}  trace {6,6:0.00} s in [{7,6:0.00}, {8,6:0.00}] {9,-5}  GPU {10:0.000}/{11:0.000} s  memory {12} MiB planned, {13} MiB peak ({14:+0.0%;-0.0%})  ETA worst {15:0.0%}' -f `
            $name, $round, $row.Final, $row.TotalLow, $row.TotalHigh, $row.TotalInside, $row.TraceFinal, $row.TraceLow, $row.TraceHigh, $row.TraceInside,
            $row.GpuEstimate, $row.GpuFinal, $row.PlannedMiB, $row.PeakMiB, $row.MemoryError, $row.WorstEta)
    }
}
$rows | Export-Csv (Join-Path $Out 'check.csv') -NoTypeInformation
$inside = @($rows | Where-Object { $_.TotalInside -and $_.TraceInside }).Count
$memory = @($rows | Where-Object { $_.MemoryInside }).Count
Write-Host "$inside of $($rows.Count) runs inside both time ranges, $memory with the peak inside the memory margin; check.csv in $Out"
