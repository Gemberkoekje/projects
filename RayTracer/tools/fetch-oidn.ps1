# Fetches the pinned Intel Open Image Denoise release for Vista's denoiser (vista-plan section 10, M1.6) into the
# git-ignored RayTracer/.deps/oidn folder, after checking the archive against its pinned SHA-256. Vista.App's build copies the
# DLLs it needs from there into its output; without them a render with --denoise warns and writes the raw image only.
# Never committed, never run in CI. Run from anywhere:
#   powershell -ExecutionPolicy Bypass -File RayTracer/tools/fetch-oidn.ps1
param(
    [string]$Deps = (Join-Path $PSScriptRoot '..\.deps')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue' # Invoke-WebRequest is many times slower with its progress bar

# The pin: OIDN 2.5.1 (2026-08-18), the digest GitHub publishes for the release asset.
$version = '2.5.1'
$asset = "oidn-$version.x64.windows.zip"
$url = "https://github.com/RenderKit/oidn/releases/download/v$version/$asset"
$sha256 = 'f11f91bc072a5e3a564515724cb72ab8fcfbc445c84a84c197ff9b16cc01396f'

New-Item -ItemType Directory -Force $Deps | Out-Null
$Deps = (Resolve-Path $Deps).Path
$target = Join-Path $Deps 'oidn'
$stamp = Join-Path $target 'vista-pin.txt'
$pin = "$asset sha256 $sha256"

if ((Test-Path $stamp) -and ((Get-Content $stamp -Raw).Trim() -eq $pin)) {
    Write-Host "OIDN $version is already in $target"
    exit 0
}

$archive = Join-Path $Deps $asset
if (-not (Test-Path $archive) -or (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sha256) {
    Write-Host "Downloading $url"
    Invoke-WebRequest -Uri $url -OutFile $archive -UseBasicParsing
}
$actual = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $sha256) {
    Remove-Item $archive -Force
    throw "Checksum mismatch for ${asset}: expected $sha256, got $actual. The download was deleted."
}

# Unpack beside the archive, then move the release's top folder into place, so a failed unpack leaves no half folder.
$unpack = Join-Path $Deps 'oidn-unpack'
if (Test-Path $unpack) { Remove-Item $unpack -Recurse -Force }
Expand-Archive -Path $archive -DestinationPath $unpack
$top = Join-Path $unpack "oidn-$version.x64.windows"
if (-not (Test-Path (Join-Path $top 'bin\OpenImageDenoise.dll'))) {
    throw "$asset does not hold oidn-$version.x64.windows\bin\OpenImageDenoise.dll."
}
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
Move-Item $top $target
Remove-Item $unpack -Recurse -Force
Set-Content -Path $stamp -Value $pin -Encoding ascii
Remove-Item $archive -Force
Write-Host "OIDN $version is in $target; rebuild Vista.App to copy its DLLs next to the executable."
