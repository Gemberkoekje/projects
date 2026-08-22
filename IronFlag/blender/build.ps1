<#
.SYNOPSIS
    Builds IronFlag art assets in Blender and writes them into the Unity project.

.DESCRIPTION
    Thin wrapper around blender/build.py so nobody has to remember the Blender
    command line. Locates Blender via the IRONFLAG_BLENDER environment variable,
    then via the default install folder, newest version first.

    The Unity editor menu Tools > IronFlag > Rebuild All Art from Blender runs
    the same build.py with the same arguments.

.PARAMETER Asset
    Case-insensitive substring filter, e.g. "Jeep". Omit to build everything.

.PARAMETER Out
    Output folder. Defaults to unity/Assets/RF/Art/Models.

.PARAMETER List
    List the known asset names and exit without building.

.EXAMPLE
    ./build.ps1
    Rebuilds every asset into the Unity project.

.EXAMPLE
    ./build.ps1 -Asset Jeep
    Rebuilds only the assets whose name contains "Jeep".
#>
[CmdletBinding()]
param(
    [string] $Asset = "",
    [string] $Out = "",
    [switch] $List
)

$ErrorActionPreference = "Stop"

function Resolve-Blender {
    if ($env:IRONFLAG_BLENDER -and (Test-Path $env:IRONFLAG_BLENDER)) {
        return $env:IRONFLAG_BLENDER
    }

    $roots = @("$env:ProgramFiles\Blender Foundation", "${env:ProgramFiles(x86)}\Blender Foundation")
    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        $versions = Get-ChildItem -Path $root -Directory | Sort-Object Name -Descending
        foreach ($version in $versions) {
            $candidate = Join-Path $version.FullName "blender.exe"
            if (Test-Path $candidate) { return $candidate }
        }
    }

    $onPath = Get-Command blender -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    throw "Blender not found. Set IRONFLAG_BLENDER to the full path of blender.exe."
}

$blender = Resolve-Blender
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildScript = Join-Path $scriptDirectory "build.py"

$forwarded = @()
if ($List) { $forwarded += "--list" }
if ($Asset) { $forwarded += @("--asset", $Asset) }
if ($Out) { $forwarded += @("--out", $Out) }

Write-Host "Using $blender"
& $blender --background --factory-startup --python $buildScript -- @forwarded
exit $LASTEXITCODE
