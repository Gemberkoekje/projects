<#
.SYNOPSIS
    Renders IronFlag sounds and music in SuperCollider and writes them into the
    Unity project.

.DESCRIPTION
    Thin wrapper around audio/build.scd so nobody has to remember the
    SuperCollider command line. Locates sclang via the IRONFLAG_SUPERCOLLIDER
    environment variable, then via the default install folder, newest version
    first.

    The Unity editor menu Tools > IronFlag > Rebuild All Audio from
    SuperCollider runs the same build.scd with the same arguments.

    Two SuperCollider quirks are handled here rather than left to the caller:
    sclang treats anything starting with a dash as one of its own options, so
    the parameters below are translated into bare words; and sclang does not
    quit when a script raises, it sits in its event loop forever, so the run is
    given a hard timeout.

.PARAMETER Sound
    Case-insensitive substring filter, e.g. "Cannon" or "Engine". Omit to
    render everything.

.PARAMETER Out
    Output folder. Defaults to unity/Assets/RF/Audio.

.PARAMETER List
    List the known sound names and exit without rendering.

.PARAMETER Listen
    Play the selection out loud instead of writing any files. This is the
    iteration loop: edit a recipe, listen, repeat, and only run a real build
    once it sounds right.

.PARAMETER Repeat
    With -Listen, how many times to play through the selection. Defaults to 1.

.PARAMETER TimeoutSeconds
    How long to wait before giving up on a render. Defaults to 600.

.EXAMPLE
    ./build.ps1
    Renders every sound into the Unity project.

.EXAMPLE
    ./build.ps1 -Sound Cannon
    Renders only the sounds whose name contains "Cannon".

.EXAMPLE
    ./build.ps1 -Listen -Sound Cannon -Repeat 3
    Plays the cannon three times without writing anything.

.EXAMPLE
    ./build.ps1 -List
    Prints every sound with its length and channel count.
#>
[CmdletBinding()]
param(
    [string] $Sound = "",
    [string] $Out = "",
    [switch] $List,
    [switch] $Listen,
    [int] $Repeat = 1,
    [int] $TimeoutSeconds = 600
)

$ErrorActionPreference = "Stop"

function Resolve-SuperCollider {
    if ($env:IRONFLAG_SUPERCOLLIDER -and (Test-Path $env:IRONFLAG_SUPERCOLLIDER)) {
        return $env:IRONFLAG_SUPERCOLLIDER
    }

    $roots = @($env:ProgramFiles, ${env:ProgramFiles(x86)})
    foreach ($root in $roots) {
        if (-not $root -or -not (Test-Path $root)) { continue }
        $versions = Get-ChildItem -Path $root -Directory -Filter "SuperCollider*" |
            Sort-Object Name -Descending
        foreach ($version in $versions) {
            $candidate = Join-Path $version.FullName "sclang.exe"
            if (Test-Path $candidate) { return $candidate }
        }
    }

    $onPath = Get-Command sclang -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    throw "SuperCollider not found. Set IRONFLAG_SUPERCOLLIDER to the full path of sclang.exe."
}

$sclang = Resolve-SuperCollider
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path

# Auditioning and rendering are the same pipeline with a different destination -
# speakers or the Unity project - so they share this wrapper.
if ($Listen) {
    $entryScript = Join-Path $scriptDirectory "audition.scd"
} else {
    $entryScript = Join-Path $scriptDirectory "build.scd"
}

# Bare words, not flags: sclang would try to parse -List itself and exit with
# "unrecognised option".
$forwarded = @($entryScript)
if ($Sound) { $forwarded += @("sound", $Sound) }
if ($Listen) {
    if ($Repeat -ne 1) { $forwarded += @("repeat", $Repeat) }
} else {
    if ($List) { $forwarded += "list" }
    if ($Out) { $forwarded += @("out", $Out) }
}

Write-Host "Using $sclang"

# sclang idles rather than exiting when a script throws, so a plain call can
# hang the build forever. Start it detached and impose the timeout ourselves.
$process = Start-Process -FilePath $sclang -ArgumentList $forwarded -NoNewWindow -PassThru
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    $process.Kill()
    throw "SuperCollider did not finish within $TimeoutSeconds seconds. A script error leaves sclang running; check the output above for an ERROR line."
}

exit $process.ExitCode
